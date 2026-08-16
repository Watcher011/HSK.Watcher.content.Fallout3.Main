using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

//Этот код добавляет в игру новую черту характера "Токсичный" - персонаж получает вдохновение от страданий других и периодически оскорбляет окружающих.

namespace Watcher
{
    // Существующий код RadiationLover
    public class ThoughtWorker_RadiationLover : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!p.story.traits.HasTrait(TraitDef.Named("RadiationLover")))
            {
                return ThoughtState.Inactive;
            }

            Hediff radiationHediff = p.health.hediffSet.GetFirstHediffOfDef(
                HediffDef.Named("RimatomicsRadiation"));

            if (radiationHediff == null)
            {
                return ThoughtState.Inactive;
            }

            float severity = radiationHediff.Severity;

            if (severity < 0.04f)
                return ThoughtState.ActiveAtStage(0);
            else if (severity < 0.2f)
                return ThoughtState.ActiveAtStage(1);
            else if (severity < 0.4f)
                return ThoughtState.ActiveAtStage(2);
            else if (severity < 0.6f)
                return ThoughtState.ActiveAtStage(3);
            else if (severity < 0.8f)
                return ThoughtState.ActiveAtStage(4);
            else
                return ThoughtState.ActiveAtStage(5);
        }
    }

    // НОВЫЙ КОД: Черта "Токсичный"
    public class ThoughtWorker_Toxic : ThoughtWorker
    {
        // Словари для отслеживания срывов и вдохновения
        private static Dictionary<Pawn, int> mentalBreaksWitnessed = new Dictionary<Pawn, int>();
        private static Dictionary<Pawn, HashSet<Pawn>> witnessedBreaksByPawn = new Dictionary<Pawn, HashSet<Pawn>>();
        private const int BREAKS_FOR_INSPIRATION = 5;

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!p.story.traits.HasTrait(TraitDef.Named("Toxic")))
            {
                return ThoughtState.Inactive;
            }

            // Инициализируем словари если нужно
            if (!mentalBreaksWitnessed.ContainsKey(p))
                mentalBreaksWitnessed[p] = 0;
            if (!witnessedBreaksByPawn.ContainsKey(p))
                witnessedBreaksByPawn[p] = new HashSet<Pawn>();

            // Ищем пешек в радиусе 15 клеток с ментальным срывом
            foreach (Pawn nearbyPawn in p.Map.mapPawns.AllPawnsSpawned)
            {
                if (nearbyPawn == p) continue;
                if (nearbyPawn.Downed || nearbyPawn.Dead) continue;

                if (nearbyPawn.Position.DistanceTo(p.Position) > 15f)
                    continue;

                if (nearbyPawn.InMentalState)
                {
                    // Проверяем, не видели ли мы уже этот срыв от этой пешки
                    if (!witnessedBreaksByPawn[p].Contains(nearbyPawn))
                    {
                        // Новый срыв!
                        witnessedBreaksByPawn[p].Add(nearbyPawn);
                        mentalBreaksWitnessed[p]++;

                        // Проверяем на вдохновение
                        CheckForInspiration(p);

                        // Очищаем если слишком много записей
                        if (witnessedBreaksByPawn[p].Count > 50)
                            witnessedBreaksByPawn[p].RemoveWhere(witnessedPawn => !witnessedPawn.InMentalState);
                    }

                    return ThoughtState.ActiveDefault;
                }
            }

            return ThoughtState.Inactive;
        }

        private void CheckForInspiration(Pawn toxicPawn)
        {
            int count = mentalBreaksWitnessed[toxicPawn];

            // Выдаем вдохновение каждые 5 срывов
            if (count % BREAKS_FOR_INSPIRATION == 0 && count > 0)
            {
                // Пытаемся выдать случайное вдохновение
                InspirationDef inspirationDef = GetRandomInspirationForToxic();
                if (inspirationDef != null)
                {
                    toxicPawn.mindState.inspirationHandler.TryStartInspiration(inspirationDef);

                    // Сообщение в лог
                    Messages.Message($"{toxicPawn.LabelShort} получает вдохновение от страданий других!",
                        toxicPawn, MessageTypeDefOf.PositiveEvent);
                }
            }
        }

        private InspirationDef GetRandomInspirationForToxic()
        {
            // Получаем все доступные вдохновения через DefDatabase
            List<InspirationDef> possibleInspirations = new List<InspirationDef>();

            // Проверяем все известные вдохновения из базовой игры и DLC
            string[] inspirationNames = new string[]
            {
                "InspiredArt",           // Вдохновение искусства (базовая игра)
                "InspiredMining",        // Вдохновение горнодобычи (базовая игра)
                "InspiredHunting",       // Вдохновение охоты (базовая игра)
                "InspiredTaming",        // Вдохновение приручения (базовая игра)
                "InspiredTrade",         // Вдохновение торговли (Royalty)
                "InspiredRecruitment",   // Вдохновение вербовки (Royalty)
                "InspiredSurgery",       // Вдохновение хирургии (Royalty)
                "InspiredCreativity",    // Вдохновение творчества (Ideology)
                "InspiredResearch",      // Вдохновение исследований (Ideology/Biotech)
            };

            foreach (string name in inspirationNames)
            {
                InspirationDef def = DefDatabase<InspirationDef>.GetNamedSilentFail(name);
                if (def != null)
                    possibleInspirations.Add(def);
            }

            if (possibleInspirations.Count == 0)
                return null;

            return possibleInspirations.RandomElement();
        }

        public static void ResetCounter(Pawn p)
        {
            if (mentalBreaksWitnessed.ContainsKey(p))
                mentalBreaksWitnessed.Remove(p);
            if (witnessedBreaksByPawn.ContainsKey(p))
                witnessedBreaksByPawn.Remove(p);
        }
    }

    // JobGiver с интервалом 5-10 дней
    public class JobGiver_ToxicInsult : ThinkNode_JobGiver
    {
        private static Dictionary<Pawn, int> lastInsultTicks = new Dictionary<Pawn, int>();
        private static Dictionary<Pawn, int> insultIntervals = new Dictionary<Pawn, int>();

        private const int MIN_INTERVAL = 300000; // 5 дней
        private const int MAX_INTERVAL = 600000; // 10 дней

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!pawn.story.traits.HasTrait(TraitDef.Named("Toxic")))
                return null;

            int currentTick = Find.TickManager.TicksGame;

            if (!insultIntervals.ContainsKey(pawn))
            {
                insultIntervals[pawn] = Rand.Range(MIN_INTERVAL, MAX_INTERVAL + 1);
            }

            if (lastInsultTicks.ContainsKey(pawn))
            {
                int lastTick = lastInsultTicks[pawn];
                int interval = insultIntervals[pawn];

                if (currentTick - lastTick < interval)
                {
                    return null;
                }
            }

            Pawn target = FindTargetForInsult(pawn);
            if (target == null)
                return null;

            Job job = JobMaker.MakeJob(JobDefOf.Insult, target);
            job.count = 1;

            lastInsultTicks[pawn] = currentTick;
            insultIntervals[pawn] = Rand.Range(MIN_INTERVAL, MAX_INTERVAL + 1);

            return job;
        }

        private Pawn FindTargetForInsult(Pawn insultingPawn)
        {
            Pawn bestTarget = null;
            float bestDistance = float.MaxValue;

            foreach (Pawn potentialTarget in insultingPawn.Map.mapPawns.AllPawnsSpawned)
            {
                if (potentialTarget == insultingPawn) continue;
                if (potentialTarget.Downed || potentialTarget.Dead) continue;
                if (!potentialTarget.RaceProps.Humanlike) continue;

                if (potentialTarget.Faction == insultingPawn.Faction &&
                    insultingPawn.relations.OpinionOf(potentialTarget) > 20)
                    continue;

                float distance = potentialTarget.Position.DistanceTo(insultingPawn.Position);
                if (distance < 20f && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTarget = potentialTarget;
                }
            }

            return bestTarget;
        }
    }
}