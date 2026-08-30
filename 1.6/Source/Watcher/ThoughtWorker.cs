using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

// Удобная база для добавления новых черт характера в RimWorld 1.6
// На базе существующего кода Toxic + RadiationLover

namespace Watcher
{
    // ==================== БАЗОВЫЙ КЛАСС ДЛЯ THOUGHTWORKER ====================

    public abstract class ThoughtWorker_TraitBase : ThoughtWorker
    {
        protected abstract string TraitDefName { get; }

        protected virtual bool CheckTrait(Pawn p)
        {
            if (p?.story?.traits == null) return false;
            return p.story.traits.HasTrait(DefDatabase<TraitDef>.GetNamedSilentFail(TraitDefName));
        }

        protected sealed override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!CheckTrait(p))
                return ThoughtState.Inactive;

            return GetThoughtState(p);
        }

        protected virtual ThoughtState GetThoughtState(Pawn p)
        {
            return ThoughtState.ActiveDefault;
        }
    }

    // ==================== УТИЛИТЫ ====================

    public static class HediffHelper
    {
        public static bool HasHediff(Pawn p, string hediffDefName)
        {
            if (p?.health?.hediffSet == null) return false;
            var def = HediffDef.Named(hediffDefName);
            return def != null && p.health.hediffSet.HasHediff(def);
        }

        public static Hediff GetFirstHediff(Pawn p, string hediffDefName)
        {
            if (p?.health?.hediffSet == null) return null;
            var def = HediffDef.Named(hediffDefName);
            return def != null ? p.health.hediffSet.GetFirstHediffOfDef(def) : null;
        }

        public static float GetHediffSeverity(Pawn p, string hediffDefName)
        {
            var hediff = GetFirstHediff(p, hediffDefName);
            return hediff?.Severity ?? 0f;
        }
    }

    // ==================== СУЩЕСТВУЮЩИЙ КОД: RADIATION LOVER ====================

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

    // ==================== СУЩЕСТВУЮЩИЙ КОД: TOXIC ====================

    public class ThoughtWorker_Toxic : ThoughtWorker
    {
        private static Dictionary<Pawn, int> mentalBreaksWitnessed = new Dictionary<Pawn, int>();
        private static Dictionary<Pawn, HashSet<Pawn>> witnessedBreaksByPawn = new Dictionary<Pawn, HashSet<Pawn>>();
        private const int BREAKS_FOR_INSPIRATION = 5;

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!p.story.traits.HasTrait(TraitDef.Named("Toxic")))
            {
                return ThoughtState.Inactive;
            }

            if (!mentalBreaksWitnessed.ContainsKey(p))
                mentalBreaksWitnessed[p] = 0;
            if (!witnessedBreaksByPawn.ContainsKey(p))
                witnessedBreaksByPawn[p] = new HashSet<Pawn>();

            foreach (Pawn nearbyPawn in p.Map.mapPawns.AllPawnsSpawned)
            {
                if (nearbyPawn == p) continue;
                if (nearbyPawn.Downed || nearbyPawn.Dead) continue;

                if (nearbyPawn.Position.DistanceTo(p.Position) > 15f)
                    continue;

                if (nearbyPawn.InMentalState)
                {
                    if (!witnessedBreaksByPawn[p].Contains(nearbyPawn))
                    {
                        witnessedBreaksByPawn[p].Add(nearbyPawn);
                        mentalBreaksWitnessed[p]++;
                        CheckForInspiration(p);

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

            if (count % BREAKS_FOR_INSPIRATION == 0 && count > 0)
            {
                InspirationDef inspirationDef = GetRandomInspirationForToxic();
                if (inspirationDef != null)
                {
                    toxicPawn.mindState.inspirationHandler.TryStartInspiration(inspirationDef);
                    Messages.Message($"{toxicPawn.LabelShort} получает вдохновение от страданий других!",
                        toxicPawn, MessageTypeDefOf.PositiveEvent);
                }
            }
        }

        private InspirationDef GetRandomInspirationForToxic()
        {
            List<InspirationDef> possibleInspirations = new List<InspirationDef>();

            string[] inspirationNames = new string[]
            {
                "InspiredArt",
                "InspiredMining",
                "InspiredHunting",
                "InspiredTaming",
                "InspiredTrade",
                "InspiredRecruitment",
                "InspiredSurgery",
                "InspiredCreativity",
                "InspiredResearch",
            };

            foreach (string name in inspirationNames)
            {
                InspirationDef def = DefDatabase<InspirationDef>.GetNamedSilentFail(name);
                if (def != null)
                    possibleInspirations.Add(def);
            }

            return possibleInspirations.Count == 0 ? null : possibleInspirations.RandomElement();
        }

        public static void ResetCounter(Pawn p)
        {
            mentalBreaksWitnessed.Remove(p);
            witnessedBreaksByPawn.Remove(p);
        }
    }

    public class JobGiver_ToxicInsult : ThinkNode_JobGiver
    {
        private static Dictionary<Pawn, int> lastInsultTicks = new Dictionary<Pawn, int>();
        private static Dictionary<Pawn, int> insultIntervals = new Dictionary<Pawn, int>();

        private const int MIN_INTERVAL = 300000;
        private const int MAX_INTERVAL = 600000;

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

    // ==================== НОВАЯ ЧЕРТА: СОЛНЕЧНАЯ БАТАРЕЙКА ====================

    public class ThoughtWorker_SolarBattery : ThoughtWorker_TraitBase
    {
        protected override string TraitDefName => "SolarBattery";

        private const string HEDIFF_BRIGHT = "Enlighten_Bright";
        private const string HEDIFF_CHARGED = "SolarBattery_Charged";

        protected override ThoughtState GetThoughtState(Pawn p)
        {
            bool hasBright = HediffHelper.HasHediff(p, HEDIFF_BRIGHT);

            // Добавляем/убираем хедифф скорости
            Hediff chargedHediff = HediffHelper.GetFirstHediff(p, HEDIFF_CHARGED);

            if (hasBright && chargedHediff == null)
            {
                // Выдаём бонусный хедифф
                HediffDef chargedDef = HediffDef.Named(HEDIFF_CHARGED);
                if (chargedDef != null)
                {
                    p.health.AddHediff(chargedDef);
                }
            }
            else if (!hasBright && chargedHediff != null)
            {
                // Убираем бонусный хедифф
                p.health.RemoveHediff(chargedHediff);
            }

            if (!hasBright)
                return ThoughtState.Inactive;

            return ThoughtState.ActiveDefault;
        }
    }

    // ==================== НОВАЯ ЧЕРТА: КРЕПКИЙ ХРЕБЕТ ====================

    public class ThoughtWorker_StrongSpine : ThoughtWorker_TraitBase
    {
        protected override string TraitDefName => "StrongSpine";

        private const string HEDIFF_DEF_NAME = "StrongSpine_Hediff";

        protected override ThoughtState GetThoughtState(Pawn p)
        {
            // Выдаём хедифф если его нет
            Hediff hediff = HediffHelper.GetFirstHediff(p, HEDIFF_DEF_NAME);
            if (hediff == null)
            {
                HediffDef def = HediffDef.Named(HEDIFF_DEF_NAME);
                if (def != null)
                {
                    p.health.AddHediff(def);
                }
            }

            return ThoughtState.ActiveDefault;
        }
    }


    // ==================== НОВАЯ ЧЕРТА:  ЗАРЯД БОДРОСТИ ====================

    public class ThoughtWorker_VigorCharge : ThoughtWorker_TraitBase
    {
        protected override string TraitDefName => "VigorCharge";

        private const string HEDIFF_DEF_NAME = "VigorCharge_Hediff";

        protected override ThoughtState GetThoughtState(Pawn p)
        {
            Hediff hediff = HediffHelper.GetFirstHediff(p, HEDIFF_DEF_NAME);
            if (hediff == null)
            {
                HediffDef def = HediffDef.Named(HEDIFF_DEF_NAME);
                if (def != null)
                {
                    p.health.AddHediff(def);
                }
            }

            return ThoughtState.ActiveDefault;
        }
    }

    // ==================== НОВАЯ ЧЕРТА: КОМИКСОМАН ====================
    public class MapComponent_ComicsFan : MapComponent
    {
        private int checkInterval = 250;
        private int nextCheckTick = 0;
        private Dictionary<Pawn, int> lastDrawTicks = new Dictionary<Pawn, int>();
        private Dictionary<Pawn, int> nextIntervalTicks = new Dictionary<Pawn, int>();

        private const int MIN_INTERVAL_TICKS = 25000;   // ~10 часов
        private const int MAX_INTERVAL_TICKS = 1250000;  // ~2 дня

        public MapComponent_ComicsFan(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int currentTick = Find.TickManager.TicksGame;
            if (currentTick < nextCheckTick) return;
            nextCheckTick = currentTick + checkInterval;

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!IsValidComicsFan(pawn)) continue;
                TryTriggerComicsCreation(pawn, currentTick);
            }
        }

        private bool IsValidComicsFan(Pawn pawn)
        {
            if (pawn?.story?.traits == null) return false;
            if (!pawn.story.traits.HasTrait(TraitDef.Named("ComicsFan"))) return false;
            if (pawn.Downed || pawn.Dead || pawn.InMentalState) return false;

            if (pawn.CurJob != null)
            {
                if (pawn.CurJob.def == JobDefOf.LayDown ||
                    pawn.CurJob.def == JobDefOf.Ingest ||
                    pawn.CurJob.def == JobDefOf.FleeAndCower ||
                    pawn.CurJob.def == JobDefOf.Vomit)
                    return false;
            }
            return true;
        }

        private void TryTriggerComicsCreation(Pawn pawn, int currentTick)
        {
            if (!nextIntervalTicks.ContainsKey(pawn))
            {
                nextIntervalTicks[pawn] = Rand.Range(MIN_INTERVAL_TICKS, MAX_INTERVAL_TICKS + 1);
                lastDrawTicks[pawn] = currentTick;
                return;
            }

            if (currentTick - lastDrawTicks[pawn] < nextIntervalTicks[pawn])
                return;

            CreateComics(pawn);
            lastDrawTicks[pawn] = currentTick;
            nextIntervalTicks[pawn] = Rand.Range(MIN_INTERVAL_TICKS, MAX_INTERVAL_TICKS + 1);
        }

        private void CreateComics(Pawn pawn)
        {
            if (pawn?.Map == null) return;

            ThingDef comicsDef = ThingDef.Named("ComicsBook");
            if (comicsDef == null)
            {
                Log.Warning("[ComicsFan] ComicsBook ThingDef not found!");
                return;
            }

            Thing comics = ThingMaker.MakeThing(comicsDef);
            if (comics == null)
            {
                Log.Warning("[ComicsFan] Failed to create ComicsBook thing!");
                return;
            }

            // Генерируем книгу через ванильный метод
            Book book = comics as Book;
            if (book != null)
            {
                book.GenerateBook(pawn, null);
            }
            else
            {
                Log.Warning("[ComicsFan] ComicsBook is not a Book type!");
            }

            GenPlace.TryPlaceThing(comics, pawn.Position, pawn.Map, ThingPlaceMode.Near);

            Messages.Message(
                $"{pawn.LabelShort} drew a new issue of \"{book?.Title ?? "Grognak"}\"!",
                comics,
                MessageTypeDefOf.PositiveEvent
            );

            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(
                ThoughtDef.Named("ComicsFan_CreatedComics")
            );

            Log.Message($"[ComicsFan] {pawn.LabelShort} created: {book?.Title ?? "Unknown"}");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref lastDrawTicks, "lastDrawTicks", LookMode.Reference, LookMode.Value);
            Scribe_Collections.Look(ref nextIntervalTicks, "nextIntervalTicks", LookMode.Reference, LookMode.Value);
        }
    }

    public class ThoughtWorker_ComicsFan : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p?.story?.traits == null) return ThoughtState.Inactive;
            if (!p.story.traits.HasTrait(TraitDef.Named("ComicsFan"))) return ThoughtState.Inactive;
            return ThoughtState.ActiveDefault;
        }
    }

    public class ThoughtWorker_ComicsFanCreated : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            return ThoughtState.ActiveDefault;
        }
    }
}