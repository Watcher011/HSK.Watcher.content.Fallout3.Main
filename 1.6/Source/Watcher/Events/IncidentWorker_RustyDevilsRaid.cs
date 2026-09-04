using LudeonTK;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Watcher.Events
{
    // ======== НАСТРОЙКИ МОДА ========

    public class RustyDevilsModSettings : ModSettings
    {
        public bool enableMechBetrayal = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableMechBetrayal, "enableMechBetrayal", true);
            base.ExposeData();
        }
    }

    // ======== КЛАСС МОДА ========

    public class RustyDevilsMod : Mod
    {
        public static RustyDevilsModSettings Settings;

        public RustyDevilsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<RustyDevilsModSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled(
                "RustyDevils.Settings.MechBetrayalLabel".Translate(),
                ref Settings.enableMechBetrayal,
                "RustyDevils.Settings.MechBetrayalTooltip".Translate()
            );

            listing.End();
        }

        public override string SettingsCategory()
        {
            return "Rusty Devils";
        }
    }

    // ======== ИВЕНТ ========

    public class IncidentWorker_RustyDevilsRaid : IncidentWorker
    {
        private const float MinDaysPassed = 45f;
        private const int MinColonistCount = 5;
        private const int MinWealth = 80000;
        private const int MinDaysSinceLastRaid = 35;
        private const float ThreatMultiplier = 1.6f;

        private static int lastRustyDevilsRaidTick = -999999;

        // ======== DEBUG: Вызов через меню отладки ========

        [DebugAction("Incidents", "Rusty Devils Raid", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DebugTriggerRustyDevilsRaid()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Log.Warning("RustyDevils: No current map for debug raid.");
                return;
            }

            Faction rustyDevils = GetOrCreateRustyDevilsFaction();
            if (rustyDevils == null)
            {
                Log.Error("RustyDevils: Failed to get or create RustyDevils faction!");
                return;
            }

            if (!rustyDevils.HostileTo(Faction.OfPlayer))
                rustyDevils.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile);

            IncidentWorker_RustyDevilsRaid worker = new IncidentWorker_RustyDevilsRaid();
            IncidentParms parms = new IncidentParms
            {
                target = map,
                faction = rustyDevils,
                points = StorytellerUtility.DefaultThreatPointsNow(map) * ThreatMultiplier,
                raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn
            };

            bool result = worker.TryExecute(parms);
            Log.Message($"RustyDevils: Debug raid triggered. Result: {result}");
        }

        // ======== ОСНОВНОЙ КЛАСС ========

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            try
            {
                Map map = (Map)parms.target;
                if (map == null) return false;

                Faction rustyDevils = GetRustyDevilsFaction();
                if (rustyDevils == null || rustyDevils.defeated) return false;
                if (!rustyDevils.HostileTo(Faction.OfPlayer)) return false;

                if (GenDate.DaysPassedFloat < MinDaysPassed) return false;
                if (map.mapPawns.FreeColonistsCount < MinColonistCount) return false;
                if (map.wealthWatcher.WealthTotal < MinWealth) return false;

                int ticksSinceLast = Find.TickManager.TicksGame - lastRustyDevilsRaidTick;
                if (ticksSinceLast < MinDaysSinceLastRaid * 60000 && lastRustyDevilsRaidTick > 0)
                    return false;

                return true;
            }
            catch { return false; }
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (map == null) return false;

            Faction rustyDevils = parms.faction ?? GetRustyDevilsFaction();
            if (rustyDevils == null) return false;

            lastRustyDevilsRaidTick = Find.TickManager.TicksGame;

            // === НАХОДИМ ТОЧКУ СПАВНА КАК В ОБЫЧНЫХ РЕЙДАХ ===
            IntVec3 spawnCenter = parms.spawnCenter.IsValid
                ? parms.spawnCenter
                : CellFinder.RandomEdgeCell(map);

            // === ПРЕДАТЕЛЬСТВО МЕХАНОИДОВ ИГРОКА (через настройки) ===
            int betrayedMechCount = 0;
            if (RustyDevilsMod.Settings != null && RustyDevilsMod.Settings.enableMechBetrayal)
            {
                betrayedMechCount = BetrayPlayerMechs(map, rustyDevils);
            }

            // === СОЗДАЕМ РЕЙД С КАСТОМНЫМИ БОЙЦАМИ И МЕХАНОИДАМИ ===
            bool raidSuccess = SpawnCustomRaidForce(map, rustyDevils, spawnCenter);

            // Создаем эффекты
            CreateEffects(map, spawnCenter);

            // === ОТПРАВЛЯЕМ ТОЛЬКО НАШЕ КАСТОМНОЕ ПИСЬМО ===
            if (raidSuccess)
            {
                SendRaidLetter(map, rustyDevils, betrayedMechCount);
            }

            return raidSuccess;
        }

        // ======== ПРОВЕРКА КЛЕТКИ ДЛЯ СПАВНА ========

        private bool IsValidSpawnCell(IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map)) return false;
            if (cell.Fogged(map)) return false;
            if (!cell.Standable(map)) return false;
            if (!cell.Walkable(map)) return false;

            // Проверяем, что клетка не занята зданием
            Building building = cell.GetEdifice(map);
            if (building != null)
            {
                // Не спавним в зданиях
                if (building.def.IsEdifice())
                {
                    return false;
                }
            }

            // Проверяем, что клетка не заминирована или заблокирована
            List<Thing> things = cell.GetThingList(map);
            foreach (Thing thing in things)
            {
                if (thing.def.category == ThingCategory.Building && thing.def.IsEdifice())
                {
                    return false;
                }
                // Проверяем на объекты, которые блокируют проход
                if (thing.def.passability == Traversability.Impassable)
                {
                    return false;
                }
            }

            // Проверяем тип местности (исключаем горы и скалы)
            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null)
            {
                string terrainName = terrain.defName.ToLower();
                // Исключаем горные и скалистые местности
                if (terrainName.Contains("mountain") ||
                    terrainName.Contains("rock") ||
                    terrainName.Contains("wall") ||
                    terrainName.Contains("stone") ||
                    terrainName.Contains("cave") ||
                    terrainName.Contains("granite") ||
                    terrainName.Contains("marble") ||
                    terrainName.Contains("limestone") ||
                    terrainName.Contains("slate") ||
                    terrainName.Contains("sandstone"))
                {
                    return false;
                }

                // Проверяем, что это не глубокий снег или вода
                if (terrain.passability == Traversability.Impassable)
                {
                    return false;
                }
            }

            return true;
        }

        // ======== ПОИСК КЛЕТКИ ДЛЯ СПАВНА С ПРОВЕРКОЙ ========

        private IntVec3 FindSpawnCellNear(IntVec3 center, Map map, int radius)
        {
            if (map == null) return center;

            // Пытаемся найти валидную клетку
            IntVec3 result;
            if (CellFinder.TryFindRandomCellNear(center, map, radius,
                c => IsValidSpawnCell(c, map),
                out result))
            {
                return result;
            }

            // Запасной вариант - ищем просто проходимую клетку
            if (CellFinder.TryFindRandomCellNear(center, map, radius,
                c => c.Standable(map) && !c.Fogged(map) && c.Walkable(map),
                out result))
            {
                return result;
            }

            // Абсолютный запасной вариант
            return CellFinder.RandomClosewalkCellNear(center, map, radius);
        }

        // ======== МЕТОД ДЛЯ СОЗДАНИЯ РЕЙДА С КАСТОМНЫМИ БОЙЦАМИ И МЕХАНОИДАМИ ========

        private bool SpawnCustomRaidForce(Map map, Faction faction, IntVec3 spawnCenter)
        {
            try
            {
                List<Pawn> pawns = new List<Pawn>();

                // === 1. СОЗДАЕМ ЛЮДЕЙ (ВАШИ КАСТОМНЫЕ БОЙЦЫ) ===

                // Rusty_mechanic - 10 штук
                for (int i = 0; i < 10; i++)
                {
                    PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Rusty_mechanic");
                    if (kind != null)
                    {
                        Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                        if (pawn != null) pawns.Add(pawn);
                    }
                }

                // Rusty_mechanic_Advanced - 6 штук
                for (int i = 0; i < 6; i++)
                {
                    PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Rusty_mechanic_Advanced");
                    if (kind != null)
                    {
                        Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                        if (pawn != null) pawns.Add(pawn);
                    }
                }

                // Rusty_technician - 5 штук
                for (int i = 0; i < 5; i++)
                {
                    PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Rusty_technician");
                    if (kind != null)
                    {
                        Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                        if (pawn != null) pawns.Add(pawn);
                    }
                }

                // Rusty_technician_Advanced - 3 штуки
                for (int i = 0; i < 3; i++)
                {
                    PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Rusty_technician_Advanced");
                    if (kind != null)
                    {
                        Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
                        if (pawn != null) pawns.Add(pawn);
                    }
                }

                // === 2. СОЗДАЕМ МЕХАНОИДОВ ===

                // ОБЯЗАТЕЛЬНО создаем 1 SentryBot
                PawnKindDef sentryKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Mech_SentryBot");
                if (sentryKind != null)
                {
                    Pawn sentry = PawnGenerator.GeneratePawn(sentryKind, faction);
                    if (sentry != null)
                    {
                        pawns.Add(sentry);
                        Log.Message("RustyDevils: Spawned mandatory SentryBot.");
                    }
                }
                else
                {
                    Log.Warning("RustyDevils: Mech_SentryBot not found!");
                }

                // Список остальных механоидов для спавна
                List<string> mechDefs = new List<string>
                {
                    "Mech_CentipedeGunner",
                    "Mech_CentipedeBurner",
                    "Mech_Scyther",
                    "Mech_Lancer"
                };

                // Спавним 2-4 дополнительных механоидов
                int extraMechCount = Rand.Range(2, 5);
                for (int i = 0; i < extraMechCount; i++)
                {
                    string defName = mechDefs.RandomElement();
                    PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail(defName);
                    if (kind != null)
                    {
                        Pawn mech = PawnGenerator.GeneratePawn(kind, faction);
                        if (mech != null) pawns.Add(mech);
                    }
                }

                if (pawns.Count == 0)
                {
                    Log.Error("RustyDevils: Failed to generate any pawns!");
                    return false;
                }

                Log.Message($"RustyDevils: Generated {pawns.Count} pawns for raid (humans + mechs).");

                // Спавним всех бойцов с проверкой клеток
                foreach (Pawn pawn in pawns)
                {
                    if (pawn == null) continue;

                    IntVec3 pos = FindSpawnCellNear(spawnCenter, map, 10);
                    if (pos.IsValid && IsValidSpawnCell(pos, map))
                    {
                        GenSpawn.Spawn(pawn, pos, map);
                        FleckMaker.ThrowSmoke(pos.ToVector3Shifted(), map, 1.5f);
                    }
                    else
                    {
                        // Запасной вариант - ищем любую проходимую клетку
                        IntVec3 fallbackPos = CellFinder.RandomClosewalkCellNear(spawnCenter, map, 15);
                        if (fallbackPos.IsValid)
                        {
                            GenSpawn.Spawn(pawn, fallbackPos, map);
                            FleckMaker.ThrowSmoke(fallbackPos.ToVector3Shifted(), map, 1.5f);
                        }
                        else
                        {
                            Log.Warning($"RustyDevils: Failed to find spawn position for pawn!");
                        }
                    }
                }

                // Создаем Lord для рейда
                LordJob_AssaultColony assaultJob = new LordJob_AssaultColony(faction, canKidnap: true, canTimeoutOrFlee: false);
                Lord lord = LordMaker.MakeNewLord(faction, assaultJob, map, pawns);

                // НЕ ДОБАВЛЯЕМ ПОВТОРНО - они уже добавлены через MakeNewLord
                // Удален дублирующий цикл AddPawn

                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error($"RustyDevils: Failed to spawn custom raid - {ex.Message}");
                return false;
            }
        }

        // ======== МЕТОДЫ ДЛЯ ПИСЕМ ========

        private void SendRaidLetter(Map map, Faction faction, int betrayedMechCount = 0)
        {
            try
            {
                // Основное письмо о рейде - используем {0} для подстановки названия фракции
                string title = "RustyDevils.Letter.Title".Translate();
                string text = "RustyDevils.Letter.Text".Translate(faction.Name);

                // Создаем LookTargets для письма
                List<Thing> targets = new List<Thing>();

                // Добавляем вражеских юнитов если они есть на карте
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                {
                    if (pawn.Faction == faction && pawn.Spawned)
                    {
                        targets.Add(pawn);
                        if (targets.Count >= 5) break;
                    }
                }

                LookTargets lookTargets = new LookTargets(targets);

                // Отправляем основное письмо
                Find.LetterStack.ReceiveLetter(
                    title,
                    text,
                    LetterDefOf.ThreatBig,
                    lookTargets,
                    faction
                );

                // Если было предательство механоидов - отправляем дополнительное письмо
                if (betrayedMechCount > 0)
                {
                    SendMechBetrayalLetter(map, betrayedMechCount);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"RustyDevils: Failed to send letter - {ex.Message}");
            }
        }

        private void SendMechBetrayalLetter(Map map, int count)
        {
            try
            {
                string title = "RustyDevils.MechBetrayal.Letter.Title".Translate();
                string text = "RustyDevils.MechBetrayal.Letter.Text".Translate(count);

                // Ищем предавших механоидов для LookTargets
                List<Thing> traitors = new List<Thing>();
                Faction rustyDevils = GetRustyDevilsFaction();

                if (rustyDevils != null)
                {
                    foreach (Pawn pawn in map.mapPawns.AllPawns)
                    {
                        if (pawn.Faction == rustyDevils && pawn.RaceProps.IsMechanoid)
                        {
                            traitors.Add(pawn);
                            if (traitors.Count >= 3) break;
                        }
                    }
                }

                LookTargets lookTargets = new LookTargets(traitors);

                Find.LetterStack.ReceiveLetter(
                    title,
                    text,
                    LetterDefOf.ThreatBig,
                    lookTargets
                );
            }
            catch (System.Exception ex)
            {
                Log.Error($"RustyDevils: Failed to send betrayal letter - {ex.Message}");
            }
        }

        // ======== ПРЕДАТЕЛЬСТВО МЕХАНОИДОВ ========

        private int BetrayPlayerMechs(Map map, Faction rustyDevils)
        {
            // Получаем ВСЕХ механоидов игрока (и боевых, и рабочих)
            List<Pawn> playerMechs = map.mapPawns.AllPawns
                .Where(p => p.RaceProps.IsMechanoid && p.Faction == Faction.OfPlayer)
                .ToList();

            if (playerMechs.Count == 0) return 0;

            int betrayedCount = 0;

            foreach (Pawn mech in playerMechs)
            {
                if (mech == null || mech.Dead) continue;

                IntVec3 oldPos = mech.Position;
                mech.SetFaction(rustyDevils);
                AddToRaidLord(mech, map, rustyDevils, null);

                FleckMaker.ThrowSmoke(oldPos.ToVector3Shifted(), map, 2f);
                FleckMaker.ThrowLightningGlow(oldPos.ToVector3Shifted(), map, 1.5f);
                betrayedCount++;
            }

            return betrayedCount;
        }

        // ======== СПАВН И ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ========

        private Lord FindRaidLord(Map map, Faction faction)
        {
            if (map?.lordManager == null) return null;

            foreach (Lord lord in map.lordManager.lords)
            {
                if (lord.faction == faction && lord.LordJob is LordJob_AssaultColony)
                    return lord;
            }
            return null;
        }

        private void AddToRaidLord(Pawn pawn, Map map, Faction faction, Lord existingLord)
        {
            if (pawn == null || pawn.Dead || map == null) return;

            if (existingLord != null)
            {
                existingLord.AddPawn(pawn);
                return;
            }

            LordJob_AssaultColony assaultJob = new LordJob_AssaultColony(faction, canKidnap: true, canTimeoutOrFlee: false);
            LordMaker.MakeNewLord(faction, assaultJob, map, new List<Pawn> { pawn });
        }

        private void CreateEffects(Map map, IntVec3 center)
        {
            if (map == null) return;

            for (int i = 0; i < 8; i++)
            {
                IntVec3 pos = center + new IntVec3(Rand.Range(-4, 4), 0, Rand.Range(-4, 4));
                if (pos.InBounds(map))
                    FleckMaker.ThrowSmoke(pos.ToVector3Shifted(), map, Rand.Range(2f, 3f));
            }
        }

        private static Faction GetRustyDevilsFaction()
        {
            FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail("RustyDevils");
            if (def == null) return null;
            return Find.FactionManager.FirstFactionOfDef(def);
        }

        private static Faction GetOrCreateRustyDevilsFaction()
        {
            Faction faction = GetRustyDevilsFaction();
            if (faction != null) return faction;

            FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail("RustyDevils");
            if (def == null) return null;

            FactionGeneratorParms parms = new FactionGeneratorParms(def);
            faction = FactionGenerator.NewGeneratedFaction(parms);
            Find.FactionManager.Add(faction);
            faction.SetRelationDirect(Faction.OfPlayer, FactionRelationKind.Hostile);

            return faction;
        }
    }
}