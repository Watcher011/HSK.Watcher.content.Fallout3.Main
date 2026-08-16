using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

//Этот код добавляет в игру катастрофическое погодное явление - радиоактивный торнадо, который спавнит радиоактивных тараканов и создаёт зону токсичного заражения.

namespace Watcher.FalloutRimworld.RadNado
{
    [DefOf]
    public static class RadNadoDefOf
    {
        public static GameConditionDef RadNado;
        public static IncidentDef RadNadoIncident;

        static RadNadoDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RadNadoDefOf));
        }
    }

    [StaticConstructorOnStartup]
    public static class RadNadoMod
    {
        static RadNadoMod()
        {
            //Log.Message("[RadNado] Mod initialized");
        }
    }

    // Кастомное игровое условие
    public class GameCondition_RadNado : GameCondition
    {
        private int tornadoSpawnDelay = 60;
        private Thing radTornado;
        private int lastRoachSpawnTick = -9999;
        private int totalSpawnedRoaches = 0;
        private const int MAX_ROACHES = 20;
        private GameCondition toxicFallout;
        private int lastRoachCheckTick = -9999;

        // Храним список наших тараканов для лучшего контроля
        private List<Pawn> spawnedRoaches = new List<Pawn>();

        // Мягкий зелено-оранжевый цвет
        private readonly Color softGreenOrange = new Color(0.7f, 0.8f, 0.4f);
        private readonly Color skyColor = new Color(0.8f, 0.9f, 0.6f);
        private readonly Color skyShadow = new Color(0.7f, 0.8f, 0.5f);

        // Список для отслеживания радиоактивных торнадо
        private static HashSet<Thing> radTornadoes = new HashSet<Thing>();

        public override string Label
        {
            get
            {
                return "RadNado.Condition.Label".Translate();
            }
        }

        public override string TooltipString
        {
            get
            {
                return "RadNado.Condition.Description".Translate();
            }
        }

        public override void Init()
        {
            base.Init();
            AddToxicFallout();
        }

        public override void GameConditionTick()
        {
            base.GameConditionTick();

            // Спавним торнадо с задержкой
            if (tornadoSpawnDelay > 0)
            {
                tornadoSpawnDelay--;
                if (tornadoSpawnDelay == 0)
                {
                    SpawnRadTornado();
                }
            }

            // Если торнадо существует, обрабатываем его эффекты
            if (radTornado != null && !radTornado.Destroyed)
            {
                ProcessTornadoEffects();
            }

            // Проверяем тараканов каждые 30 тиков - чаще для лучшего контроля
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick - lastRoachCheckTick > 30)
            {
                ForceRoachesToAttack();
                lastRoachCheckTick = currentTick;
            }
        }

        private void ForceRoachesToAttack()
        {
            if (SingleMap == null) return;

            try
            {
                // Очищаем список от мертвых/уничтоженных тараканов
                spawnedRoaches.RemoveAll(p => p == null || p.Dead || p.Destroyed);

                // Проверяем наших тараканов
                foreach (Pawn roach in spawnedRoaches)
                {
                    if (roach == null || roach.Dead || roach.Downed || roach.Map == null) continue;

                    // Сильно принудительно заставляем атаковать
                    ForceSingleRoachToAttack(roach);
                }

                // Также проверяем других насекомых на карте на всякий случай
                foreach (Pawn pawn in SingleMap.mapPawns.AllPawnsSpawned)
                {
                    if (pawn == null || pawn.Dead || pawn.Downed) continue;

                    // Если это насекомое из фракции насекомых и не в нашем списке
                    if (pawn.def != null && pawn.def.race != null && pawn.def.race.Insect &&
                        pawn.Faction != null && pawn.Faction == Faction.OfInsects &&
                        !spawnedRoaches.Contains(pawn))
                    {
                        ForceSingleRoachToAttack(pawn);
                    }
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"[RadNado] Error forcing roaches to attack: {ex}");
            }
        }

        private void ForceSingleRoachToAttack(Pawn roach)
        {
            try
            {
                // Всегда устанавливаем фракцию насекомых
                if (roach.Faction != Faction.OfInsects)
                {
                    roach.SetFaction(Faction.OfInsects, null);
                }

                // Всегда устанавливаем ментальное состояние
                if (roach.mindState != null && roach.MentalState == null)
                {
                    roach.mindState.mentalStateHandler.TryStartMentalState(
                        MentalStateDefOf.ManhunterPermanent
                    );
                }

                // КРИТИЧЕСКИ ВАЖНО: Отключаем CompDestroyer перед установкой задания
                DisableOrOverrideDestroyerComp(roach);

                // Всегда проверяем текущее задание
                if (roach.CurJob == null || roach.CurJob.def != JobDefOf.AttackMelee)
                {
                    // Ищем ближайшую цель
                    Pawn target = FindBestAttackTarget(roach);
                    if (target != null)
                    {
                        // Полностью перехватываем управление - отменяем все текущие задания
                        roach.jobs.StopAll();
                        roach.jobs.ClearQueuedJobs();

                        // Создаем новое задание атаки
                        Job attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                        attackJob.maxNumMeleeAttacks = 999;
                        attackJob.expiryInterval = 150; // Уменьшаем интервал для более частых проверок
                        attackJob.checkOverrideOnExpire = true;

                        // Запускаем задание с максимальным приоритетом
                        roach.jobs.StartJob(
                            attackJob,
                            JobCondition.InterruptForced,
                            null,
                            resumeCurJobAfterwards: false,
                            cancelBusyStances: true,
                            thinkTree: null,
                            fromQueue: false,
                            canReturnCurJobToPool: false
                        );

                        // Устанавливаем долг атаковать
                        if (roach.mindState != null)
                        {
                            roach.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);
                            roach.mindState.canFleeIndividual = false; // Запрещаем бегство
                        }

                        // Дополнительно: устанавливаем флаг, что мы контролируем этого таракана
                        SetRoachControlledFlag(roach, true);
                    }
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"[RadNado] Error forcing single roach to attack: {ex}");
            }
        }

        private void DisableOrOverrideDestroyerComp(Pawn roach)
        {
            try
            {
                // Пытаемся найти и отключить CompDestroyer
                var comps = roach.AllComps;
                if (comps != null)
                {
                    foreach (var comp in comps)
                    {
                        if (comp != null)
                        {
                            string compTypeName = comp.GetType().Name;

                            // Если это CompDestroyer или похожий комп
                            if (compTypeName.Contains("Destroyer") || compTypeName.Contains("Watcher.Comps"))
                            {
                                //Log.Message($"[RadNado] Found interfering comp: {compTypeName}, attempting to disable");

                                // Пытаемся отключить через рефлексию
                                TryDisableCompThroughReflection(comp);
                            }
                        }
                    }
                }

                // Отключаем режим покоя для насекомых
                CompCanBeDormant dormantComp = roach.GetComp<CompCanBeDormant>();
                if (dormantComp != null)
                {
                    dormantComp.WakeUp();
                    // Пытаемся установить флаг, чтобы комп не засыпал
                    TrySetDormantCompDisabled(dormantComp);
                }

                // Вместо отключения ThinkTree устанавливаем приоритетное мышление для атаки
                if (roach.mindState != null)
                {
                    // Устанавливаем мышление манхантера
                    roach.mindState.mentalStateHandler.TryStartMentalState(
                        MentalStateDefOf.ManhunterPermanent,
                        forceWake: true
                    );
                }

            }
            catch (Exception ex)
            {
                //Log.Error($"[RadNado] Error disabling/overriding comps: {ex}");
            }
        }

        private void TryDisableCompThroughReflection(object comp)
        {
            try
            {
                Type compType = comp.GetType();

                // Пытаемся найти и установить поле/свойство, отключающее комп
                var props = compType.GetProperties(System.Reflection.BindingFlags.Instance |
                                                  System.Reflection.BindingFlags.Public |
                                                  System.Reflection.BindingFlags.NonPublic);

                foreach (var prop in props)
                {
                    if (prop.Name.Contains("Active") || prop.Name.Contains("Enabled") ||
                        prop.Name.Contains("Should") || prop.Name.Contains("Can"))
                    {
                        try
                        {
                            if (prop.CanWrite)
                            {
                                prop.SetValue(comp, false);
                                Log.Message($"[RadNado] Set {prop.Name} to false");
                            }
                        }
                        catch { }
                    }
                }

                // Также проверяем поля
                var fields = compType.GetFields(System.Reflection.BindingFlags.Instance |
                                               System.Reflection.BindingFlags.Public |
                                               System.Reflection.BindingFlags.NonPublic);

                foreach (var field in fields)
                {
                    if (field.Name.Contains("active") || field.Name.Contains("enabled") ||
                        field.Name.Contains("should") || field.Name.Contains("can"))
                    {
                        try
                        {
                            if (!field.IsInitOnly)
                            {
                                field.SetValue(comp, false);
                                Log.Message($"[RadNado] Set field {field.Name} to false");
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"[RadNado] Error in reflection: {ex}");
            }
        }

        private void TrySetDormantCompDisabled(CompCanBeDormant comp)
        {
            try
            {
                // Пытаемся установить флаг, чтобы комп не мог заснуть
                var type = comp.GetType();
                var field = type.GetField("canBeDormant",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);

                if (field != null)
                {
                    field.SetValue(comp, false);
                }
            }
            catch { }
        }

        private void SetRoachControlledFlag(Pawn roach, bool controlled)
        {
            try
            {
                // Используем хеддифф для маркировки контролируемых тараканов
                if (controlled)
                {
                    HealthUtility.AdjustSeverity(roach, HediffDef.Named("RadNadoControlled"), 1f);
                }
                else
                {
                    HealthUtility.AdjustSeverity(roach, HediffDef.Named("RadNadoControlled"), -1f);
                }
            }
            catch
            {
                // Если хеддифф не существует, пропускаем
                // Создаем временный маркер через другой способ
                if (controlled)
                {
                    // Добавляем временный хеддифф
                    HealthUtility.AdjustSeverity(roach, HediffDefOf.ToxicBuildup, 0.1f);
                }
            }
        }

        private Pawn FindBestAttackTarget(Pawn roach)
        {
            if (roach.Map == null) return null;

            Pawn bestTarget = null;
            float bestScore = float.MinValue;

            foreach (Pawn pawn in roach.Map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn == null || pawn.Dead || pawn.Downed || !pawn.RaceProps.Humanlike) continue;

                // Проверяем доступность
                if (!roach.CanReach(pawn, PathEndMode.Touch, Danger.Deadly)) continue;

                // Считаем оценку (ближе = лучше)
                float distance = pawn.Position.DistanceTo(roach.Position);
                if (distance > 50f) continue; // Слишком далеко

                float score = 100f / (distance + 1f);

                // Дополнительные бонусы
                if (pawn.IsFighting()) score += 20f; // Предпочитаем сражающихся
                if (pawn.Drafted) score += 15f; // Предпочитаем драфтнутых

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = pawn;
                }
            }

            // Если не нашли колонистов, ищем животных
            if (bestTarget == null)
            {
                foreach (Pawn pawn in roach.Map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn == null || pawn.Dead || pawn.Downed || pawn.Faction == Faction.OfPlayer) continue;
                    if (pawn.RaceProps.Animal && !pawn.RaceProps.IsMechanoid)
                    {
                        if (!roach.CanReach(pawn, PathEndMode.Touch, Danger.Deadly)) continue;

                        float distance = pawn.Position.DistanceTo(roach.Position);
                        if (distance > 50f) continue;

                        float score = 80f / (distance + 1f);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestTarget = pawn;
                        }
                    }
                }
            }

            return bestTarget;
        }

        private void AddToxicFallout()
        {
            if (SingleMap == null) return;

            GameConditionDef toxicDef = GameConditionDefOf.ToxicFallout;
            if (toxicDef != null && !SingleMap.gameConditionManager.ConditionIsActive(toxicDef))
            {
                toxicFallout = GameConditionMaker.MakeCondition(toxicDef, this.Duration);
                SingleMap.gameConditionManager.RegisterCondition(toxicFallout);
            }
        }

        private void SpawnRadTornado()
        {
            if (SingleMap == null) return;

            IntVec3 spawnPos;
            if (TryFindEdgeSpawnPosition(SingleMap, out spawnPos))
            {
                ThingDef tornadoDef = DefDatabase<ThingDef>.GetNamed("Tornado");
                if (tornadoDef != null)
                {
                    radTornado = GenSpawn.Spawn(tornadoDef, spawnPos, SingleMap);
                    radTornadoes.Add(radTornado);

                    // Эффект при спавне торнадо
                    FleckMaker.ThrowSmoke(spawnPos.ToVector3Shifted(), SingleMap, 2f);
                }
            }
        }

        private bool TryFindEdgeSpawnPosition(Map map, out IntVec3 result)
        {
            int edgeOffset = 5;
            int mapSizeX = map.Size.x;
            int mapSizeZ = map.Size.z;

            int minX = edgeOffset;
            int maxX = mapSizeX - edgeOffset - 1;
            int minZ = edgeOffset;
            int maxZ = mapSizeZ - edgeOffset - 1;

            Rot4 edgeSide = new Rot4(Rand.Range(0, 4));

            for (int i = 0; i < 30; i++)
            {
                IntVec3 candidate;

                switch (edgeSide.AsInt)
                {
                    case 0: // Север
                        candidate = new IntVec3(Rand.Range(minX, maxX + 1), 0, maxZ);
                        break;
                    case 1: // Восток
                        candidate = new IntVec3(maxX, 0, Rand.Range(minZ, maxZ + 1));
                        break;
                    case 2: // Юг
                        candidate = new IntVec3(Rand.Range(minX, maxX + 1), 0, minZ);
                        break;
                    case 3: // Запад
                        candidate = new IntVec3(minX, 0, Rand.Range(minZ, maxZ + 1));
                        break;
                    default:
                        candidate = map.Center;
                        break;
                }

                if (candidate.InBounds(map) &&
                    candidate.Standable(map) &&
                    !candidate.Roofed(map) &&
                    !candidate.Fogged(map) &&
                    IsValidSpawnCell(candidate, map))
                {
                    result = candidate;
                    return true;
                }

                edgeSide = new Rot4(Rand.Range(0, 4));
            }

            return TryFindFallbackSpawnPosition(map, out result);
        }

        private bool TryFindFallbackSpawnPosition(Map map, out IntVec3 result)
        {
            int mapSizeX = map.Size.x;
            int mapSizeZ = map.Size.z;

            IntVec3[] corners = new IntVec3[]
            {
                new IntVec3(2, 0, 2),
                new IntVec3(2, 0, mapSizeZ - 3),
                new IntVec3(mapSizeX - 3, 0, 2),
                new IntVec3(mapSizeX - 3, 0, mapSizeZ - 3)
            };

            foreach (IntVec3 corner in corners)
            {
                if (corner.InBounds(map) && corner.Standable(map) && !corner.Roofed(map))
                {
                    result = corner;
                    return true;
                }
            }

            for (int i = 0; i < 20; i++)
            {
                IntVec3 candidate = GenerateRandomEdgePosition(map, 10);

                if (candidate.InBounds(map) &&
                    candidate.Standable(map) &&
                    !candidate.Roofed(map) &&
                    IsValidSpawnCell(candidate, map))
                {
                    result = candidate;
                    return true;
                }
            }

            result = map.Center;
            return map.Center.InBounds(map);
        }

        private IntVec3 GenerateRandomEdgePosition(Map map, int maxDistanceFromEdge)
        {
            int mapSizeX = map.Size.x;
            int mapSizeZ = map.Size.z;

            int side = Rand.Range(0, 4);

            switch (side)
            {
                case 0: // Северная граница
                    return new IntVec3(Rand.Range(0, mapSizeX), 0, Rand.Range(mapSizeZ - maxDistanceFromEdge, mapSizeZ));
                case 1: // Восточная граница
                    return new IntVec3(Rand.Range(mapSizeX - maxDistanceFromEdge, mapSizeX), 0, Rand.Range(0, mapSizeZ));
                case 2: // Южная граница
                    return new IntVec3(Rand.Range(0, mapSizeX), 0, Rand.Range(0, maxDistanceFromEdge));
                case 3: // Западная граница
                    return new IntVec3(Rand.Range(0, maxDistanceFromEdge), 0, Rand.Range(0, mapSizeZ));
                default:
                    return map.Center;
            }
        }

        private bool IsValidSpawnCell(IntVec3 cell, Map map)
        {
            if (cell.GetTerrain(map).affordances == null) return false;

            Building edifice = cell.GetEdifice(map);
            if (edifice != null && edifice.def.passability == Traversability.Impassable)
                return false;

            return true;
        }

        private void ProcessTornadoEffects()
        {
            int currentTick = Find.TickManager.TicksGame;

            if (currentTick % 60 == 0)
            {
                CreateGlowEffect();
            }

            if (currentTick - lastRoachSpawnTick > Rand.RangeInclusive(200, 400) && totalSpawnedRoaches < MAX_ROACHES)
            {
                SpawnRadroaches();
                lastRoachSpawnTick = currentTick;
            }

            if (currentTick % 30 == 0 && radTornado != null && !radTornado.Destroyed)
            {
                CreateAdditionalEffects();
            }
        }

        private void CreateGlowEffect()
        {
            if (radTornado == null || radTornado.Destroyed) return;

            for (int i = 0; i < 3; i++)
            {
                IntVec3 cell = radTornado.Position + new IntVec3(Rand.Range(-6, 7), 0, Rand.Range(-6, 7));
                if (cell.InBounds(radTornado.Map) && cell.Standable(radTornado.Map))
                {
                    FleckMaker.ThrowDustPuff(
                        cell.ToVector3Shifted() + new Vector3(Rand.Range(-0.5f, 0.5f), 0f, Rand.Range(-0.5f, 0.5f)),
                        radTornado.Map,
                        1.2f
                    );
                }
            }
        }

        private void CreateAdditionalEffects()
        {
            if (radTornado == null || radTornado.Destroyed) return;

            for (int i = 0; i < 2; i++)
            {
                IntVec3 cell = radTornado.Position + new IntVec3(Rand.Range(-4, 5), 0, Rand.Range(-4, 5));
                if (cell.InBounds(radTornado.Map))
                {
                    FleckMaker.ThrowDustPuff(cell.ToVector3Shifted(), radTornado.Map, 0.8f);
                }
            }
        }

        private void SpawnRadroaches()
        {
            if (radTornado == null || radTornado.Destroyed) return;
            if (totalSpawnedRoaches >= MAX_ROACHES) return;

            PawnKindDef roachDef = GetRoachDef();
            if (roachDef == null) return;

            int numRoaches = Rand.RangeInclusive(1, 3);
            numRoaches = Mathf.Min(numRoaches, MAX_ROACHES - totalSpawnedRoaches);

            for (int i = 0; i < numRoaches; i++)
            {
                IntVec3 spawnCell = GetRoachSpawnCell();
                if (spawnCell.IsValid && spawnCell.InBounds(radTornado.Map))
                {
                    if (TrySpawnRoach(roachDef, spawnCell))
                    {
                        totalSpawnedRoaches++;
                    }
                }
            }
        }

        private bool TrySpawnRoach(PawnKindDef roachDef, IntVec3 spawnCell)
        {
            try
            {
                // Создаем специального агрессивного таракана
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind: roachDef,
                    faction: Faction.OfInsects,
                    context: PawnGenerationContext.NonPlayer,
                    tile: -1,
                    forceGenerateNewPawn: true,
                    developmentalStages: DevelopmentalStage.Adult,
                    forceBaselinerChance: 0f,
                    allowAddictions: false,
                    fixedBiologicalAge: 1f,
                    fixedChronologicalAge: 1f,
                    fixedGender: Gender.Male,
                    validatorPreGear: (Pawn p) => true,
                    validatorPostGear: (Pawn p) => true
                );

                Pawn newRoach = PawnGenerator.GeneratePawn(request);
                if (newRoach == null) return false;

                if (GenPlace.TryPlaceThing(newRoach, spawnCell, radTornado.Map, ThingPlaceMode.Near))
                {
                    // Добавляем в список наших тараканов
                    spawnedRoaches.Add(newRoach);

                    // Устанавливаем агрессивные настройки
                    SetupAggressiveRoach(newRoach);

                    //Log.Message($"[RadNado] Spawned aggressive roach at {spawnCell}");
                    return true;
                }
                else
                {
                    newRoach.Destroy();
                    return false;
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"[RadNado] Error spawning roach: {ex}");
                return false;
            }
        }

        private void SetupAggressiveRoach(Pawn roach)
        {
            try
            {
                // 1. Устанавливаем фракцию
                roach.SetFaction(Faction.OfInsects, null);

                // 2. Устанавливаем ментальное состояние
                if (roach.mindState != null)
                {
                    roach.mindState.mentalStateHandler.TryStartMentalState(
                        MentalStateDefOf.ManhunterPermanent,
                        forceWake: true
                    );
                }

                // 3. Отключаем все мешающие компы (ОЧЕНЬ ВАЖНО!)
                DisableOrOverrideDestroyerComp(roach);

                // 4. Устанавливаем долг
                if (roach.mindState != null)
                {
                    roach.mindState.duty = new PawnDuty(DutyDefOf.AssaultColony);
                    roach.mindState.canFleeIndividual = false;
                }

                // 5. Немедленно атакуем
                Pawn target = FindBestAttackTarget(roach);
                if (target != null)
                {
                    // Отменяем все задания
                    roach.jobs.StopAll();
                    roach.jobs.ClearQueuedJobs();

                    // Создаем задание атаки
                    Job attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                    attackJob.maxNumMeleeAttacks = 999;
                    attackJob.expiryInterval = 150; // Частые проверки
                    attackJob.checkOverrideOnExpire = true;

                    // Запускаем задание с максимальным контролем
                    roach.jobs.StartJob(
                        attackJob,
                        JobCondition.InterruptForced,
                        null,
                        resumeCurJobAfterwards: false,
                        cancelBusyStances: true
                    );
                }

                // 6. Добавляем токсичное отравление
                HealthUtility.AdjustSeverity(roach, HediffDefOf.ToxicBuildup, 0.2f);

                // 7. Эффект при спавне
                FleckMaker.ThrowDustPuff(roach.Position.ToVector3Shifted(), radTornado.Map, 0.6f);

                // 8. Маркируем как контролируемого
                SetRoachControlledFlag(roach, true);
            }
            catch (Exception ex)
            {
                //Log.Error($"[RadNado] Error setting up aggressive roach: {ex}");
            }
        }

        private PawnKindDef GetRoachDef()
        {
            // Проверяем различные варианты
            PawnKindDef def =DefDatabase<PawnKindDef>.GetNamedSilentFail("GlowingRadroach") ??
                             DefDatabase<PawnKindDef>.GetNamedSilentFail("Megascarab") ??
                             DefDatabase<PawnKindDef>.GetNamedSilentFail("Spelopede");

            if (def == null)
            {
                // Ищем любого насекомого
                foreach (PawnKindDef pawnKind in DefDatabase<PawnKindDef>.AllDefs)
                {
                    if (pawnKind.race != null && pawnKind.race.race != null &&
                        pawnKind.race.race.Insect)
                    {
                        def = pawnKind;
                        break;
                    }
                }
            }

            if (def == null)
            {
                def = PawnKindDefOf.Megascarab;
            }

            return def;
        }

        private IntVec3 GetRoachSpawnCell()
        {
            if (radTornado == null) return IntVec3.Invalid;

            for (int i = 0; i < 20; i++)
            {
                IntVec3 candidate = radTornado.Position + new IntVec3(Rand.RangeInclusive(-8, 8), 0, Rand.RangeInclusive(-8, 8));
                if (candidate.InBounds(radTornado.Map) &&
                    candidate.Walkable(radTornado.Map) &&
                    !candidate.Fogged(radTornado.Map) &&
                    (candidate - radTornado.Position).LengthHorizontal >= 3)
                {
                    return candidate;
                }
            }

            return radTornado.Position;
        }

        public override float SkyTargetLerpFactor(Map map)
        {
            return 1f;
        }

        public override SkyTarget? SkyTarget(Map map)
        {
            return new SkyTarget(0.85f, new SkyColorSet(skyColor, Color.white, skyShadow, 1.1f), 1f, 1f);
        }

        public override void End()
        {
            base.End();

            radTornadoes.RemoveWhere(t => t == radTornado);

            // Снимаем контроль с тараканов
            foreach (Pawn roach in spawnedRoaches)
            {
                if (roach != null && !roach.Destroyed)
                {
                    SetRoachControlledFlag(roach, false);
                }
            }
            spawnedRoaches.Clear();

            if (toxicFallout != null && SingleMap != null)
            {
                toxicFallout.End();
            }
        }

        public static bool IsRadTornado(Thing tornado)
        {
            return radTornadoes.Contains(tornado);
        }
    }

    // Кастомный инцидент
    public class IncidentWorker_RadNado : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (map == null) return false;

            if (HasBugArtBuilding(map)) return false;

            return map.gameConditionManager != null;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            if (map == null) return false;

            if (HasBugArtBuilding(map))
            {
                ShowBlockedMessage(map);
                return false;
            }

            GameConditionDef radNadoDef = RadNadoDefOf.RadNado;
            if (radNadoDef == null) return false;

            if (map.gameConditionManager.ConditionIsActive(radNadoDef)) return false;

            GameCondition radNado = GameConditionMaker.MakeCondition(radNadoDef, 22500);
            map.gameConditionManager.RegisterCondition(radNado);

            ShowTornadoMessage(map);
            return true;
        }

        private void ShowTornadoMessage(Map map)
        {
            string messageLabel = "RadNado.Incident.LetterLabel".Translate();
            string messageText = "RadNado.Incident.LetterText".Translate();

            Find.LetterStack.ReceiveLetter(
                messageLabel,
                messageText,
                LetterDefOf.ThreatBig,
                new LookTargets(map.Center, map)
            );
        }

        private void ShowBlockedMessage(Map map)
        {
            string messageLabel = "RadNado.Blocked.LetterLabel".Translate();
            string messageText = "RadNado.Blocked.LetterText".Translate();

            Thing bugArt = GetBugArtBuilding(map);
            if (bugArt != null)
            {
                messageText += "\n\n" + "RadNado.Blocked.StatueLocation".Translate(bugArt.Position.x, bugArt.Position.z);
            }

            Find.LetterStack.ReceiveLetter(
                messageLabel,
                messageText,
                LetterDefOf.PositiveEvent,
                new LookTargets(map.Center, map)
            );
        }

        private bool HasBugArtBuilding(Map map)
        {
            if (map == null) return false;

            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing.def != null && thing.def.defName == "BugArt")
                {
                    return true;
                }
            }

            return false;
        }

        private Thing GetBugArtBuilding(Map map)
        {
            if (map == null) return null;

            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing.def != null && thing.def.defName == "BugArt")
                {
                    return thing;
                }
            }

            return null;
        }
    }
}