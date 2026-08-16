using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Watcher.Events
{
    /// <summary>
    /// Ивент: "Переборщили с удобрением" - агрессивные растения атакуют из-за переполненных гидропонных установок 
    /// Если игрок использует гидропонные установки (особенно примитивные или перегруженные),
    /// они могут "переудобриться" и породить мутировавшие растения-споры, которые атакуют колонию
    /// </summary>
    public class IncidentWorker_OverfertilizedHydroponics : IncidentWorker
    {
        // Константы для баланса
        private const int MIN_SPORE_PLANTS = 2;
        private const int MAX_SPORE_PLANTS = 5;
        private const int MIN_FILTH_PILES = 1;
        private const int MAX_FILTH_PILES = 2;
        private const float SPAWN_RADIUS = 3f;
        private const float MIN_BUILDING_DISTANCE = 1.5f;

        // DefName'ы целевых построек (проверяются в этом порядке)
        private static readonly string[] TargetBuildingDefs = new string[]
        {
            "PrimitiveHydroponic",
            "HydroponicsBasin",
            "Agrarian",
            "FungiponicsBasin",
            "NewHydroponicsBasin",
            "ClutterAlloyHydroponicsBasinVS"
        };

        // Ленивая инициализация Def'ов
        private static ThingDef FilthNFRADDef
        {
            get
            {
                if (_filthNFRADDef == null)
                {
                    _filthNFRADDef = DefDatabase<ThingDef>.GetNamed("Filth_NFRAD", false);
                }
                return _filthNFRADDef;
            }
        }
        private static ThingDef _filthNFRADDef;

        private static PawnKindDef SporePlantDef
        {
            get
            {
                if (_sporePlantDef == null)
                {
                    _sporePlantDef = DefDatabase<PawnKindDef>.GetNamed("SporePlant", false);
                }
                return _sporePlantDef;
            }
        }
        private static PawnKindDef _sporePlantDef;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            return GetValidTargetBuildings(map).Any();
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;

            List<Building> targetBuildings = GetValidTargetBuildings(map).ToList();
            if (!targetBuildings.Any())
                return false;

            Building targetBuilding = targetBuildings.RandomElement();
            IntVec3 spawnCenter = targetBuilding.Position;

            int filthCount = SpawnFilthAroundBuilding(map, spawnCenter);
            int plantCount = SpawnSporePlants(map, spawnCenter);

            SendNotificationLetter(map, targetBuilding, plantCount);

            return true;
        }

        private IEnumerable<Building> GetValidTargetBuildings(Map map)
        {
            foreach (string defName in TargetBuildingDefs)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamed(defName, false);
                if (def == null) continue;

                foreach (Thing thing in map.listerThings.ThingsOfDef(def))
                {
                    if (thing is Building building && building.Spawned && !building.Destroyed)
                    {
                        if (HasEnoughSpaceAround(building.Position, map))
                        {
                            yield return building;
                        }
                    }
                }
            }
        }

        private bool HasEnoughSpaceAround(IntVec3 center, Map map)
        {
            int freeCells = 0;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, SPAWN_RADIUS, true))
            {
                if (cell.InBounds(map) && cell.Walkable(map))
                {
                    freeCells++;
                }
            }
            return freeCells >= MIN_FILTH_PILES;
        }

        private int SpawnFilthAroundBuilding(Map map, IntVec3 center)
        {
            int spawnedCount = 0;
            int targetCount = Rand.Range(MIN_FILTH_PILES, MAX_FILTH_PILES + 1);

            ThingDef filthDef = FilthNFRADDef ?? ThingDefOf.Filth_Dirt;
            if (filthDef == null)
            {
                //Log.Warning("[Watcher.Events] Filth def not found, skipping filth spawn");
                return 0;
            }

            List<IntVec3> validCells = GenRadial.RadialCellsAround(center, SPAWN_RADIUS, true)
                .Where(c => c.InBounds(map) && c.Walkable(map) && c != center)
                .ToList();

            validCells.Shuffle();

            for (int i = 0; i < Math.Min(targetCount, validCells.Count); i++)
            {
                IntVec3 cell = validCells[i];

                if (IsValidFilthLocation(cell, map, center))
                {
                    SpawnFilthDirect(cell, map, filthDef);
                    spawnedCount++;
                }
            }

            return spawnedCount;
        }

        private void SpawnFilthDirect(IntVec3 cell, Map map, ThingDef filthDef)
        {
            try
            {
                if (!FilthMaker.CanMakeFilth(cell, map, filthDef))
                    return;

                Thing filth = ThingMaker.MakeThing(filthDef);
                if (filth != null)
                {
                    GenSpawn.Spawn(filth, cell, map);
                }
            }
            catch (Exception ex)
            {
                //Log.Warning($"[Watcher.Events] Failed to spawn filth at {cell}: {ex.Message}");
            }
        }

        private bool IsValidFilthLocation(IntVec3 cell, Map map, IntVec3 centerBuilding)
        {
            foreach (Thing thing in cell.GetThingList(map))
            {
                if (thing is Building building && building.Position != centerBuilding)
                {
                    if (cell.DistanceTo(building.Position) < MIN_BUILDING_DISTANCE)
                        return false;
                }
            }
            return true;
        }

        private int SpawnSporePlants(Map map, IntVec3 center)
        {
            int spawnedCount = 0;
            int targetCount = Rand.Range(MIN_SPORE_PLANTS, MAX_SPORE_PLANTS + 1);

            if (SporePlantDef == null)
            {
                //Log.Warning("[Watcher.Events] SporePlant PawnKindDef not found!");
                return 0;
            }

            List<IntVec3> spawnCells = GenRadial.RadialCellsAround(center, SPAWN_RADIUS + 1, true)
                .Where(c => c.InBounds(map)
                    && c.Walkable(map)
                    && !c.Fogged(map)
                    && c != center
                    && c.DistanceTo(center) > 1.5f)
                .ToList();

            spawnCells.Shuffle();

            Faction insectFaction = GetOrCreateInsectFaction();

            for (int i = 0; i < Math.Min(targetCount, spawnCells.Count); i++)
            {
                IntVec3 cell = spawnCells[i];

                if (!cell.Impassable(map) && !cell.GetThingList(map).Any(t => t is Pawn))
                {
                    Pawn sporePlant = PawnGenerator.GeneratePawn(SporePlantDef, insectFaction);
                    if (sporePlant != null)
                    {
                        GenSpawn.Spawn(sporePlant, cell, map);
                        MakePlantAggressive(sporePlant);
                        spawnedCount++;
                    }
                }
            }

            return spawnedCount;
        }

        private void MakePlantAggressive(Pawn plant)
        {
            if (plant == null) return;

            try
            {
                if (!plant.Faction.HostileTo(Faction.OfPlayer))
                {
                    plant.SetFaction(Faction.OfInsects);
                }

                if (plant.mindState?.mentalStateHandler != null)
                {
                    bool success = TrySetMentalState(plant, MentalStateDefOf.Manhunter);

                    if (!success)
                    {
                        TrySetMentalState(plant, MentalStateDefOf.ManhunterPermanent);
                    }
                }

                if (plant.playerSettings != null)
                {
                    plant.playerSettings.hostilityResponse = HostilityResponseMode.Attack;
                }
            }
            catch (Exception ex)
            {
                //Log.Warning($"[Watcher.Events] Failed to make plant aggressive: {ex.Message}");
            }
        }

        private bool TrySetMentalState(Pawn plant, MentalStateDef stateDef)
        {
            try
            {
                MethodInfo method = plant.mindState.mentalStateHandler.GetType().GetMethod(
                    "TryStartMentalState",
                    BindingFlags.Public | BindingFlags.Instance
                );

                if (method == null) return false;

                ParameterInfo[] parameters = method.GetParameters();
                object[] args = new object[parameters.Length];

                for (int i = 0; i < parameters.Length; i++)
                {
                    Type paramType = parameters[i].ParameterType;

                    if (paramType == typeof(MentalStateDef))
                        args[i] = stateDef;
                    else if (paramType == typeof(string))
                        args[i] = null;
                    else if (paramType == typeof(bool))
                        args[i] = i == 2;
                    else if (paramType == typeof(Pawn))
                        args[i] = null;
                    else
                        args[i] = null;
                }

                object result = method.Invoke(plant.mindState.mentalStateHandler, args);
                return result is bool && (bool)result;
            }
            catch
            {
                return false;
            }
        }

        private Faction GetOrCreateInsectFaction()
        {
            Faction faction = FactionUtility.DefaultFactionFrom(FactionDefOf.Insect);

            if (faction == null)
            {
                faction = Find.FactionManager.AllFactions
                    .FirstOrDefault(f => f.def == FactionDefOf.Insect);
            }

            if (faction == null)
            {
                try
                {
                    FactionGeneratorParms parms = new FactionGeneratorParms(FactionDefOf.Insect);
                    faction = FactionGenerator.NewGeneratedFaction(parms);
                    Find.FactionManager.Add(faction);
                }
                catch (Exception ex)
                {
                    //Log.Warning($"[Watcher.Events] Failed to create insect faction: {ex.Message}");
                    faction = Faction.OfMechanoids;
                }
            }

            if (faction != null && !faction.HostileTo(Faction.OfPlayer))
            {
                faction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile);
            }

            return faction ?? Faction.OfMechanoids;
        }

        private void SendNotificationLetter(Map map, Building targetBuilding, int plantCount)
        {
            string label = "LetterLabelOverfertilizedHydroponics".Translate();
            string text = "LetterTextOverfertilizedHydroponics".Translate(
                targetBuilding.Label,
                plantCount.ToString()
            );

            LetterDef letterDef = plantCount >= 4 ? LetterDefOf.ThreatBig : LetterDefOf.ThreatSmall;

            Find.LetterStack.ReceiveLetter(
                label,
                text,
                letterDef,
                new LookTargets(targetBuilding.Position, map)
            );
        }
    }
}