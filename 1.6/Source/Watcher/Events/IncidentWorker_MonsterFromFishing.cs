using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;
using Watcher.Events;

//Этот код добавляет в игру систему слежения за радиоактивной рыбой, которая при накоплении в больших количествах призывает монстра-Заглота (Glutton).

namespace Watcher.Events
{
    // ============================================
    // ОСНОВНОЙ КЛАСС: FALLOUT РЫБНЫЙ ТРЕКЕР С КУЛДАУНОМ
    // ============================================

    public class FalloutFishTracker : MapComponent
    {
        private int currentFishCount = 0;
        private int lastCheckTick = 0;
        private const int CHECK_INTERVAL = 200;

        // Настройки в стиле Fallout
        private const int MIN_FISH_FOR_MONSTER = 3;
        private const int MAX_FISH_FOR_MONSTER = 5;
        private const float CHANCE_IN_RANGE = 0.8f;

        // Кулдаун между событиями (5 дней = 300000 тиков)
        private const int COOLDOWN_TICKS = 3000000;
        private int lastMonsterTick = -COOLDOWN_TICKS;
        private bool eventTriggeredThisCycle = false;

        private static readonly string[] TRACKED_FISH =
        {
            "Fish_ThreeCrawler",
            "Fish_Radharvester"
        };

        public FalloutFishTracker(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            int currentTick = Find.TickManager.TicksGame;

            // Сбрасываем флаг события каждый день
            if (currentTick % 60000 == 0) // Каждый игровой день
            {
                eventTriggeredThisCycle = false;
            }

            if (currentTick - lastCheckTick > CHECK_INTERVAL)
            {
                lastCheckTick = currentTick;
                CheckFishAndSpawnMonster();
            }
        }

        private void CheckFishAndSpawnMonster()
        {
            int newCount = CountAllFishOnMap();

            if (newCount != currentFishCount)
            {
                currentFishCount = newCount;

                if (Prefs.DevMode)
                {
                    Log.Message($"[Fallout Fish] Обнаружено {currentFishCount} единиц био-материала класса 'Рыба'");
                }

                CheckMonsterSpawnConditions();
            }
        }

        private int CountAllFishOnMap()
        {
            int total = 0;

            try
            {
                // 1. Рыба на земле и в хранилищах
                List<Thing> allThings = map.listerThings.AllThings;
                for (int i = 0; i < allThings.Count; i++)
                {
                    Thing thing = allThings[i];
                    if (IsTrackedFish(thing))
                    {
                        total += thing.stackCount;
                    }
                }

                // 2. Рыба в инвентарях колонистов
                List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn pawn = colonists[i];

                    // Инвентарь
                    if (pawn.inventory != null)
                    {
                        var container = pawn.inventory.innerContainer;
                        for (int j = 0; j < container.Count; j++)
                        {
                            Thing thing = container[j];
                            if (IsTrackedFish(thing))
                            {
                                total += thing.stackCount;
                            }
                        }
                    }

                    // То, что несут
                    if (pawn.carryTracker != null && pawn.carryTracker.CarriedThing != null)
                    {
                        Thing carried = pawn.carryTracker.CarriedThing;
                        if (IsTrackedFish(carried))
                        {
                            total += carried.stackCount;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"[Fallout Fish] Ошибка сканирования био-материала: {ex}");
            }

            return total;
        }

        private bool IsTrackedFish(Thing thing)
        {
            if (thing == null || thing.def == null) return false;

            string defName = thing.def.defName;
            for (int i = 0; i < TRACKED_FISH.Length; i++)
            {
                if (defName == TRACKED_FISH[i])
                    return true;
            }

            return false;
        }

        private void CheckMonsterSpawnConditions()
        {
            // Проверяем кулдаун
            int currentTick = Find.TickManager.TicksGame;
            int ticksSinceLastMonster = currentTick - lastMonsterTick;

            if (ticksSinceLastMonster < COOLDOWN_TICKS)
            {
                int daysLeft = (COOLDOWN_TICKS - ticksSinceLastMonster) / 60000;
                if (Prefs.DevMode)
                {
                    Log.Message($"[Fallout Fish] Кулдаун: до следующего события {daysLeft} дней");
                }
                return;
            }

            // Проверяем, не сработало ли уже событие в этом цикле
            if (eventTriggeredThisCycle)
            {
                if (Prefs.DevMode)
                {
                    Log.Message("[Fallout Fish] Событие уже сработало в этом цикле");
                }
                return;
            }

            // Проверяем условия по количеству рыбы
            if (currentFishCount >= MIN_FISH_FOR_MONSTER && currentFishCount <= MAX_FISH_FOR_MONSTER)
            {
                if (Rand.Value < CHANCE_IN_RANGE)
                {
                    SpawnGluttonMonster();
                }
            }
            else if (currentFishCount > MAX_FISH_FOR_MONSTER)
            {
                SpawnGluttonMonster();
            }
        }

        private void SpawnGluttonMonster()
        {
            try
            {
                int currentTick = Find.TickManager.TicksGame;

                // Фиксируем время события
                lastMonsterTick = currentTick;
                eventTriggeredThisCycle = true;

                Pawn targetPawn = FindTargetForMonster();
                if (targetPawn == null) return;

                IntVec3 spawnCell = FindWaterSpawnCell(targetPawn.Position);
                if (!spawnCell.IsValid) return;

                Pawn glutton = CreateGlutton();
                if (glutton == null) return;

                GenSpawn.Spawn(glutton, spawnCell, map);

                MakeGluttonHostile(glutton, targetPawn);

                SendGluttonEventLetter(glutton, targetPawn, currentFishCount);

                Log.Message($"[Fallout Fish] Заглот атаковал! Триггер: {currentFishCount} рыб, цель: {targetPawn.LabelShort}");
                Log.Message($"[Fallout Fish] Следующее нападение возможно через 5 дней");
            }
            catch (Exception ex)
            {
                //Log.Error($"[Fallout Fish] Критический сбой протокола: {ex}");
            }
        }

        private Pawn FindTargetForMonster()
        {
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            if (colonists.Count == 0) return null;

            // Ищем колониста с рыбой в инвентаре
            foreach (Pawn colonist in colonists)
            {
                if (colonist.inventory != null)
                {
                    foreach (Thing thing in colonist.inventory.innerContainer)
                    {
                        if (IsTrackedFish(thing))
                        {
                            return colonist;
                        }
                    }
                }

                if (colonist.carryTracker != null && colonist.carryTracker.CarriedThing != null)
                {
                    if (IsTrackedFish(colonist.carryTracker.CarriedThing))
                    {
                        return colonist;
                    }
                }
            }

            // Если никто не несет рыбу - случайный колонист
            return colonists.RandomElement();
        }

        private IntVec3 FindWaterSpawnCell(IntVec3 nearPos)
        {
            for (int i = 0; i < 25; i++)
            {
                IntVec3 testCell = nearPos + new IntVec3(
                    Rand.RangeInclusive(-12, 12),
                    0,
                    Rand.RangeInclusive(-12, 12)
                );

                if (testCell.InBounds(map) &&
                    map.terrainGrid.TerrainAt(testCell).IsWater &&
                    testCell.Walkable(map))
                {
                    return testCell;
                }
            }

            return nearPos;
        }

        private Pawn CreateGlutton()
        {
            PawnKindDef gluttonDef = DefDatabase<PawnKindDef>.GetNamed("Glutton", false);
            if (gluttonDef == null)
            {
                //Log.Warning("[Fallout Fish] Деф Заглота 'Glutton' не найден!");
                return null;
            }

            return PawnGenerator.GeneratePawn(gluttonDef, null);
        }

        private void MakeGluttonHostile(Pawn glutton, Pawn target)
        {
            if (glutton.mindState != null)
            {
                glutton.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter);

                if (glutton.jobs != null && target != null)
                {
                    Job attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                    glutton.jobs.StartJob(attackJob, JobCondition.InterruptForced);
                }
            }
        }

        private void SendGluttonEventLetter(Pawn glutton, Pawn targetPawn, int fishCount)
        {
            try
            {
                string letterKey = GetRandomGluttonLetterKey();

                // Добавляем информацию о кулдауне
                string cooldownInfo = "\n\n[Протокол: Заглот охотится в одиночку. Следующая атака через 5 дней]";

                string label = letterKey.Translate(
                    glutton.LabelCap,
                    targetPawn.LabelShort,
                    fishCount
                ).CapitalizeFirst();

                string text = (letterKey + "_Desc").Translate(
                    glutton.LabelCap,
                    targetPawn.LabelShort,
                    fishCount
                ) + cooldownInfo;

                Letter letter = LetterMaker.MakeLetter(
                    label,
                    text,
                    LetterDefOf.ThreatBig,
                    new LookTargets(glutton)
                );

                Find.LetterStack.ReceiveLetter(letter);

                string quickMessage = (letterKey + "_Quick").Translate(
                    glutton.LabelCap,
                    fishCount,
                    targetPawn.LabelShort
                );

                Messages.Message(quickMessage + " (Кулдаун: 5 дней)", MessageTypeDefOf.ThreatBig);

                Log.Message($"[Fallout Fish] {label}");
            }
            catch (Exception ex)
            {
                Log.Error($"[Fallout Fish] Ошибка системы оповещения: {ex}");

                Messages.Message($"ВНИМАНИЕ: Заглот {glutton.LabelCap} атаковал! Вы украли его рыбу ({fishCount} шт.)",
                    MessageTypeDefOf.ThreatBig);
            }
        }

        private string GetRandomGluttonLetterKey()
        {
            List<string> letterKeys = new List<string>
            {
                "Glutton_VaultTec_Hunger",
                "Glutton_Radiation_Dinner",
                "Glutton_NukaCola_Service",
                "Glutton_Brotherhood_Justice",
                "Glutton_Enclave_Experiment",
                "Glutton_Wasteland_Chef",
                "Glutton_Tax_Collector",
                "Glutton_Food_Critic",
                "Glutton_Delivery_Service",
                "Glutton_Buffet_Manager"
            };

            return letterKeys.RandomElement();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref currentFishCount, "currentFishCount", 0);
            Scribe_Values.Look(ref lastCheckTick, "lastCheckTick", 0);
            Scribe_Values.Look(ref lastMonsterTick, "lastMonsterTick", -COOLDOWN_TICKS);
            Scribe_Values.Look(ref eventTriggeredThisCycle, "eventTriggeredThisCycle", false);
        }
    }

    // ============================================
    // HARMONY ПАТЧ - ТОЛЬКО ДЛЯ FALLOUTFISHTRACKER
    // ============================================

    [HarmonyPatch(typeof(Map), "FinalizeInit")]
    public static class Patch_Map_FalloutInit
    {
        [HarmonyPostfix]
        public static void Postfix(Map __instance)
        {
            try
            {
                if (__instance.GetComponent<FalloutFishTracker>() == null)
                {
                    __instance.components.Add(new FalloutFishTracker(__instance));
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"[Fallout Fish] Ошибка инициализации протокола: {ex}");
            }
        }
    }
}

// ============================================
// ИНИЦИАЛИЗАЦИЯ - ИСПРАВЛЕННАЯ ВЕРСИЯ
// ============================================

[StaticConstructorOnStartup]
public static class FalloutFishInitializer
{
    static FalloutFishInitializer()
    {
        try
        {
            var harmony = new Harmony("Watcher.FalloutFishEvents");

            // Патчим только нужные классы вручную, игнорируя PatchAll
            PatchMapFinalizeInit(harmony);

            Log.Message("[Fallout Fish Events] Протокол инициализирован успешно");
        }
        catch (Exception ex)
        {
            //Log.Error($"[Fallout Fish Events] Критический сбой: {ex}");
        }
    }

    private static void PatchMapFinalizeInit(Harmony harmony)
    {
        try
        {
            // Явно указываем метод для патча
            var originalMethod = AccessTools.Method(typeof(Map), "FinalizeInit");
            if (originalMethod == null)
            {
                //Log.Error("[Fallout Fish] Не найден метод Map.FinalizeInit");
                return;
            }

            var postfixMethod = AccessTools.Method(typeof(Patch_Map_FalloutInit), "Postfix");
            if (postfixMethod == null)
            {
                //Log.Error("[Fallout Fish] Не найден метод Postfix");
                return;
            }

            harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfixMethod));
            //Log.Message("[Fallout Fish] Map.FinalizeInit успешно пропатчен");
        }
        catch (Exception ex)
        {
            //Log.Error($"[Fallout Fish] Ошибка патча: {ex}");
        }
    }
}