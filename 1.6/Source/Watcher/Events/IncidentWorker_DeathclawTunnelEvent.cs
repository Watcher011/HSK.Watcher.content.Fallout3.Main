using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Watcher.Events
{
    public class IncidentWorker_DeathclawTunnel : IncidentWorker
    {
        private static readonly List<string> DeathclawNames = new List<string>
        {
            "Крлдраав", "Шаав", "Крик", "Рык", "Полосатик",
            "Курица Розы", "Керит", "Матриарх"
        };

        private const string AntiBugFloorDefName = "HSK_AntiBugFloor";

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (map == null || !map.IsPlayerHome)
                return false;

            foreach (Building building in map.listerBuildings.allBuildingsColonist)
                return true;

            return false;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (!map.IsPlayerHome) return false;

            IntVec3 spawnCenter;
            if (!TryFindSpawnCenter(map, out spawnCenter))
            {
                Messages.Message("DeathclawTunnel_LogMessage_NoPlace".Translate(), MessageTypeDefOf.NegativeEvent);
                return false;
            }

            if (IsCellProtected(spawnCenter, map))
            {
                Messages.Message("DeathclawTunnel_LogMessage_ProtectedFloor".Translate(), MessageTypeDefOf.NegativeEvent);
                return false;
            }

            CreatePreEventEffects(map, spawnCenter);
            CreateTunnelHole(map, spawnCenter);
            DelaySpawnDeathclaws(map, spawnCenter, parms);
            SendWarningLetterDirect(parms);
            AddColonistThoughts(map);

            return true;
        }

        private bool IsCellProtected(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map)) return false;

            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null && terrain.defName == AntiBugFloorDefName)
                return true;

            foreach (Thing thing in cell.GetThingList(map))
                if (thing.def.defName == AntiBugFloorDefName)
                    return true;

            return false;
        }

        private bool TryFindSpawnCenter(Map map, out IntVec3 center)
        {
            List<Building> importantBuildings = new List<Building>();
            foreach (Building building in map.listerBuildings.allBuildingsColonist)
                if (building.def.building != null && building.def.BaseMarketValue > 500)
                    importantBuildings.Add(building);

            if (importantBuildings.Count > 0)
            {
                Building targetBuilding = importantBuildings[Rand.Range(0, importantBuildings.Count)];
                center = targetBuilding.Position;

                for (int i = 0; i < 20; i++)
                {
                    IntVec3 spawnCell = center + new IntVec3(Rand.Range(-3, 4), 0, Rand.Range(-3, 4));
                    if (spawnCell.InBounds(map) && spawnCell.Standable(map) &&
                        !spawnCell.Fogged(map) && !IsCellProtected(spawnCell, map) &&
                        spawnCell.GetRoom(map) != null)
                    {
                        center = spawnCell;
                        return true;
                    }
                }
            }

            var homeArea = map.areaManager.Home;
            bool hasActiveCells = false;
            foreach (IntVec3 cell in homeArea.ActiveCells)
            {
                hasActiveCells = true;
                break;
            }

            if (!hasActiveCells)
            {
                center = IntVec3.Invalid;
                return false;
            }

            List<IntVec3> validCells = new List<IntVec3>();
            foreach (IntVec3 cell in homeArea.ActiveCells)
                if (cell.InBounds(map) && cell.Standable(map) &&
                    !cell.Fogged(map) && !IsCellProtected(cell, map))
                    validCells.Add(cell);

            if (validCells.Count > 0)
            {
                center = validCells[Rand.Range(0, validCells.Count)];
                return true;
            }

            center = IntVec3.Invalid;
            return false;
        }

        private void CreateTunnelHole(Map map, IntVec3 center)
        {
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    IntVec3 cell = center + new IntVec3(x, 0, z);
                    if (cell.InBounds(map) && IsCellProtected(cell, map))
                    {
                        Messages.Message("DeathclawTunnel_LogMessage_CannotDestroyProtected".Translate(), MessageTypeDefOf.NegativeEvent);
                        return;
                    }
                }

            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    IntVec3 cell = center + new IntVec3(x, 0, z);
                    if (!cell.InBounds(map)) continue;

                    TerrainDef terrain = cell.GetTerrain(map);
                    if (terrain != null && terrain.layerable && terrain.defName != AntiBugFloorDefName)
                        map.terrainGrid.RemoveTopLayer(cell);

                    foreach (Thing thing in cell.GetThingList(map))
                        if (thing.def.category == ThingCategory.Item &&
                            thing.def.smallVolume &&
                            thing.def.BaseMarketValue < 100 &&
                            thing.def.defName != AntiBugFloorDefName)
                            thing.Destroy();
                }

            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail("Building_Destroyed");
            sound?.PlayOneShot(SoundInfo.InMap(new TargetInfo(center, map)));
        }

        private void CreatePreEventEffects(Map map, IntVec3 center)
        {
            var comp = map.GetComponent<MapComponent_DeathclawTunnel>();
            if (comp == null)
            {
                comp = new MapComponent_DeathclawTunnel(this, map);
                map.components.Add(comp);
            }
            comp.StartPreEvent(center);

            for (int i = 0; i < 12; i++)
            {
                IntVec3 offsetCell = center + new IntVec3(Rand.Range(-5, 5), 0, Rand.Range(-5, 5));
                if (!offsetCell.InBounds(map)) continue;

                Building building = offsetCell.GetEdifice(map);
                if (building != null && building.def.building != null &&
                    building.def.BaseMarketValue < 200 &&
                    !building.def.building.isEdifice &&
                    building.def.defName != AntiBugFloorDefName)
                    building.TakeDamage(new DamageInfo(DamageDefOf.Crush, 50f));

                FleckMaker.ThrowDustPuff(offsetCell, map, Rand.Range(2f, 3.5f));

                for (int j = 0; j < 3; j++)
                {
                    Vector3 pos = offsetCell.ToVector3Shifted() + new Vector3(Rand.Range(-0.3f, 0.3f), 0, Rand.Range(-0.3f, 0.3f));
                    FleckMaker.ThrowMicroSparks(pos, map);
                }
            }

            SoundDef sound = DefDatabase<SoundDef>.GetNamedSilentFail("Building_Destroyed");
            sound?.PlayOneShot(SoundInfo.InMap(new TargetInfo(center, map)));

            Messages.Message("DeathclawTunnel_LogMessage_Tunneling".Translate(), MessageTypeDefOf.ThreatBig);
        }

        private void DelaySpawnDeathclaws(Map map, IntVec3 center, IncidentParms parms)
        {
            var comp = map.GetComponent<MapComponent_DeathclawTunnel>();
            if (comp != null)
                comp.ScheduleDeathclawSpawn(center, parms);
            else
                SpawnDeathclawsImmediately(map, center, parms);
        }

        private void SpawnDeathclawsImmediately(Map map, IntVec3 center, IncidentParms parms)
        {
            List<Pawn> deathclaws = InternalSpawnDeathclaws(map, center, parms);
            if (deathclaws.Count > 0)
            {
                InternalSetupDeathclawBehavior(deathclaws, map);
                Messages.Message("DeathclawTunnel_LogMessage_Spawning".Translate(), MessageTypeDefOf.ThreatBig);
                SendFinalLetterDirect(deathclaws);
            }
        }

        private List<Pawn> InternalSpawnDeathclaws(Map map, IntVec3 center, IncidentParms parms)
        {
            List<Pawn> deathclaws = new List<Pawn>();
            int deathclawCount = Rand.RangeInclusive(3, 4);

            // ИСПОЛЬЗУЕМ ТОЛЬКО СУЩЕСТВУЮЩИЙ DEF ИЗ XML
            PawnKindDef deathclawKind = PawnKindDefOfLocal.Deathclaw;
            if (deathclawKind == null)
            {
                Log.Error("[DeathclawTunnel] PawnKindDefOfLocal.Deathclaw is null! Проверь XML и DefOf.");
                return deathclaws;
            }

            Faction monsterFaction = Find.FactionManager.FirstFactionOfDef(
                DefDatabase<FactionDef>.GetNamedSilentFail("Monster")) ?? Faction.OfInsects;

            List<string> shuffledNames = new List<string>(DeathclawNames);
            ShuffleList(shuffledNames);
            int nameIndex = 0;

            for (int i = 0; i < deathclawCount; i++)
            {
                try
                {
                    PawnGenerationRequest request = new PawnGenerationRequest(
                        deathclawKind, monsterFaction,
                        PawnGenerationContext.NonPlayer, map.Tile);

                    Pawn deathclaw = PawnGenerator.GeneratePawn(request);
                    deathclaw.ageTracker.AgeBiologicalTicks = (long)(Rand.Range(30, 60) * 3600000f);

                    if (nameIndex < shuffledNames.Count)
                    {
                        string name = shuffledNames[nameIndex];
                        deathclaw.Name = new NameTriple(name, name, name);
                        nameIndex++;
                    }
                    else
                    {
                        deathclaw.Name = new NameTriple("Коготь", "Коготь", "Коготь");
                    }

                    IntVec3 spawnLoc = FindSpawnLocation(map, center);
                    if (IsCellProtected(spawnLoc, map))
                        spawnLoc = FindAlternativeSpawnLocation(map, center);

                    GenSpawn.Spawn(deathclaw, spawnLoc, map, Rot4.Random);
                    deathclaws.Add(deathclaw);
                    CreateSpawnEffects(deathclaw, spawnLoc, map);
                }
                catch (Exception ex)
                {
                    Log.Error($"[DeathclawTunnel] Ошибка при создании Когтя Смерти: {ex}");
                }
            }

            return deathclaws;
        }

        private IntVec3 FindSpawnLocation(Map map, IntVec3 center)
        {
            for (int i = 0; i < 30; i++)
            {
                IntVec3 cell = center + new IntVec3(Rand.Range(-3, 3), 0, Rand.Range(-3, 3));
                if (cell.InBounds(map) && cell.Standable(map) && !cell.Fogged(map))
                    return cell;
            }
            return center;
        }

        private IntVec3 FindAlternativeSpawnLocation(Map map, IntVec3 center)
        {
            for (int radius = 1; radius <= 5; radius++)
                for (int i = 0; i < 20; i++)
                {
                    IntVec3 cell = center + new IntVec3(
                        Rand.Range(-radius, radius + 1), 0,
                        Rand.Range(-radius, radius + 1));
                    if (cell.InBounds(map) && cell.Standable(map) &&
                        !cell.Fogged(map) && !IsCellProtected(cell, map))
                        return cell;
                }
            return center;
        }

        private void InternalSetupDeathclawBehavior(List<Pawn> deathclaws, Map map)
        {
            foreach (Pawn deathclaw in deathclaws)
            {
                deathclaw.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent);
                deathclaw.mindState.canFleeIndividual = false;
            }
        }

        private void SendWarningLetterDirect(IncidentParms parms)
        {
            try
            {
                Find.LetterStack.ReceiveLetter(
                    "DeathclawTunnel_LetterLabel_Warning".Translate(),
                    "DeathclawTunnel_LetterText_Warning".Translate(),
                    LetterDefOf.ThreatBig);
            }
            catch (Exception ex)
            {
                Log.Error($"[DeathclawTunnel] Ошибка при отправке предупреждающего письма: {ex}");
            }
        }

        private void SendFinalLetterDirect(List<Pawn> deathclaws)
        {
            try
            {
                if (deathclaws == null || deathclaws.Count == 0) return;

                Find.LetterStack.ReceiveLetter(
                    "DeathclawTunnel_LetterLabel_Final".Translate(),
                    GetFinalLetterText(deathclaws),
                    LetterDefOf.ThreatBig, new LookTargets(deathclaws));
            }
            catch (Exception ex)
            {
                Log.Error($"[DeathclawTunnel] Ошибка при отправке финального письма: {ex}");
            }
        }

        private string GetFinalLetterText(List<Pawn> deathclaws)
        {
            List<string> deathclawEntries = new List<string>();
            foreach (Pawn deathclaw in deathclaws)
            {
                string name = GetShortName(deathclaw);
                string desc = GetDeathclawDescription(name);
                deathclawEntries.Add($"{name} - {desc}");
            }

            return "DeathclawTunnel_LetterText_Final".Translate(string.Join("\n", deathclawEntries))
                + GetAdditionalSatire()
                + "\n\nВАУЛТ-Тек предупреждает: 'Объекты демонстрируют признаки агрессивной реконструкции.'";
        }

        private string GetShortName(Pawn pawn)
        {
            if (pawn.Name == null) return "Коготь";
            if (pawn.Name is NameTriple nt)
                return !string.IsNullOrEmpty(nt.Nick) ? nt.Nick : nt.First;
            return pawn.Name.ToStringShort;
        }

        private string GetDeathclawDescription(string name)
        {
            string descKey = "DeathclawName_" + name.Replace(" ", "");
            string translated = descKey.Translate();
            return translated != descKey ? translated : "Специалист по быстрой перепланировке.";
        }

        private string GetAdditionalSatire()
        {
            Map map = Find.CurrentMap;
            if (map == null) return "";

            if (map.gameConditionManager.ConditionIsActive(GameConditionDefOf.ColdSnap) ||
                map.mapTemperature.OutdoorTemp < 0)
                return "\n\n" + "DeathclawTunnel_LetterText_ColdWeather".Translate();

            if (map.gameConditionManager.ConditionIsActive(GameConditionDefOf.HeatWave) ||
                map.mapTemperature.OutdoorTemp > 40)
                return "\n\n" + "DeathclawTunnel_LetterText_HotWeather".Translate();

            if (map.gameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout))
                return "\n\n" + "DeathclawTunnel_LetterText_Radioactive".Translate();

            float wealth = WealthUtility.PlayerWealth;
            if (wealth > 100000) return "\n\n" + "DeathclawTunnel_LetterText_RichColony".Translate();
            if (wealth < 20000) return "\n\n" + "DeathclawTunnel_LetterText_PoorColony".Translate();
            return "";
        }

        private void AddColonistThoughts(Map map)
        {
            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail("DeathclawTunnel_Destruction");
            if (thought == null) return;

            foreach (Pawn colonist in map.mapPawns.FreeColonists)
                colonist.needs?.mood?.thoughts?.memories?.TryGainMemory(thought);
        }

        private void CreateSpawnEffects(Pawn deathclaw, IntVec3 spawnLoc, Map map)
        {
            for (int i = 0; i < 5; i++)
            {
                FleckMaker.ThrowDustPuff(spawnLoc, map, 3f);
                for (int j = 0; j < 5; j++)
                {
                    Vector3 pos = spawnLoc.ToVector3Shifted() + new Vector3(
                        Rand.Range(-0.5f, 0.5f), 0, Rand.Range(-0.5f, 0.5f));
                    FleckMaker.ThrowMicroSparks(pos, map);
                }
            }

            SoundDef spawnSound = DefDatabase<SoundDef>.GetNamedSilentFail("Pawn_Animal_Roar");
            spawnSound?.PlayOneShot(SoundInfo.InMap(new TargetInfo(spawnLoc, map)));
        }

        private void ShuffleList<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = Rand.Range(0, n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }

        public List<Pawn> SpawnDeathclawsForComponent(Map map, IntVec3 center, IncidentParms parms)
            => InternalSpawnDeathclaws(map, center, parms);

        public void SetupDeathclawBehaviorForComponent(List<Pawn> deathclaws, Map map)
            => InternalSetupDeathclawBehavior(deathclaws, map);

        public void SendFinalLetterForComponent(List<Pawn> deathclaws)
            => SendFinalLetterDirect(deathclaws);
    }

    public class MapComponent_DeathclawTunnel : MapComponent
    {
        private IntVec3 tunnelCenter;
        private int ticksUntilSpawn;
        private bool eventActive;
        private IncidentParms storedParms;
        private IncidentWorker_DeathclawTunnel incidentWorker;

        public MapComponent_DeathclawTunnel(IncidentWorker_DeathclawTunnel worker, Map map) : base(map)
        {
            this.incidentWorker = worker;
        }

        public MapComponent_DeathclawTunnel(Map map) : base(map)
        {
            this.incidentWorker = new IncidentWorker_DeathclawTunnel();
        }

        public void StartPreEvent(IntVec3 center)
        {
            tunnelCenter = center;
            ticksUntilSpawn = 180;
            eventActive = true;
        }

        public void ScheduleDeathclawSpawn(IntVec3 center, IncidentParms parms)
        {
            tunnelCenter = center;
            storedParms = parms;
            ticksUntilSpawn = 180;
            eventActive = true;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (!eventActive || ticksUntilSpawn <= 0) return;

            ticksUntilSpawn--;

            if (ticksUntilSpawn > 0 && ticksUntilSpawn % 30 == 0 &&
                tunnelCenter.IsValid && tunnelCenter.InBounds(map))
            {
                for (int i = 0; i < 3; i++)
                {
                    IntVec3 offset = tunnelCenter + new IntVec3(
                        Rand.Range(-3, 3), 0, Rand.Range(-3, 3));
                    if (!offset.InBounds(map)) continue;

                    FleckMaker.ThrowDustPuff(offset, map, 2f);
                    for (int j = 0; j < 3; j++)
                    {
                        Vector3 pos = offset.ToVector3Shifted() + new Vector3(
                            Rand.Range(-0.2f, 0.2f), 0, Rand.Range(-0.2f, 0.2f));
                        FleckMaker.ThrowMicroSparks(pos, map);
                    }

                    if (ticksUntilSpawn < 60 && i == 0)
                        Find.CameraDriver.shaker.DoShake(0.5f);
                }
            }

            if (ticksUntilSpawn == 0 && eventActive)
            {
                ExecuteDelayedSpawn();
                eventActive = false;
            }
        }

        private void ExecuteDelayedSpawn()
        {
            try
            {
                var deathclaws = incidentWorker.SpawnDeathclawsForComponent(map, tunnelCenter, storedParms);
                if (deathclaws.Count > 0)
                {
                    incidentWorker.SetupDeathclawBehaviorForComponent(deathclaws, map);
                    incidentWorker.SendFinalLetterForComponent(deathclaws);
                    Messages.Message("DeathclawTunnel_LogMessage_SpawningComplete".Translate(), MessageTypeDefOf.ThreatBig);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[DeathclawTunnel] Ошибка при отложенном спавне: {ex}");
            }
        }
    }
}