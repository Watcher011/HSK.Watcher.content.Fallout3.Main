using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

//Некоторые колонисты имеют нестандартные эстетические предпочтения - им нравится одежда, снятая с мёртвых.
//    В отличие от обычных колонистов, которые получают отрицательную мысль от "мертвецкой одежды", эти получают положительный бафф.

namespace Watcher.GameComponents
{
    [DefOf]
    public static class WatcherTraitDefOf
    {
        public static TraitDef DeadmanFashionLover;

        static WatcherTraitDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WatcherTraitDefOf));
        }
    }

    public class ThoughtWorker_DeadmanFashionHappy : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (p.story?.traits?.HasTrait(WatcherTraitDefOf.DeadmanFashionLover) != true)
                return ThoughtState.Inactive;

            int deadmanCount = CountDeadmanApparel(p);

            if (deadmanCount > 0)
            {
                int stage = Mathf.Min(deadmanCount - 1, 3);
                return ThoughtState.ActiveAtStage(stage);
            }

            return ThoughtState.Inactive;
        }

        public static int CountDeadmanApparel(Pawn p)
        {
            int count = 0;
            List<Apparel> wornApparel = p.apparel?.WornApparel;

            if (wornApparel == null) return 0;

            foreach (Apparel apparel in wornApparel)
            {
                if (IsDeadmanApparel(apparel))
                {
                    count++;
                }
            }

            return count;
        }

        public static bool IsDeadmanApparel(Apparel apparel)
        {
            if (apparel == null) return false;

            if (apparel.WornByCorpse)
                return true;

            CompBiocodable biocode = apparel.TryGetComp<CompBiocodable>();
            if (biocode != null && biocode.Biocoded && biocode.CodedPawn != null && biocode.CodedPawn.Dead)
                return true;

            CompDeadmanMarker marker = apparel.TryGetComp<CompDeadmanMarker>();
            if (marker != null && marker.IsFromCorpse)
                return true;

            return false;
        }
    }

    public class CompDeadmanFashionWatcher : ThingComp
    {
        private int lastDeadmanCount = -1;

        public override void CompTick()
        {
            base.CompTick();

            if (parent.IsHashIntervalTick(250))
            {
                Pawn pawn = parent as Pawn;
                if (pawn?.story?.traits?.HasTrait(WatcherTraitDefOf.DeadmanFashionLover) == true)
                {
                    int currentCount = ThoughtWorker_DeadmanFashionHappy.CountDeadmanApparel(pawn);
                    if (currentCount != lastDeadmanCount)
                    {
                        lastDeadmanCount = currentCount;
                        pawn.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty();
                    }
                }
            }
        }
    }

    public class CompProperties_DeadmanFashionWatcher : CompProperties
    {
        public CompProperties_DeadmanFashionWatcher()
        {
            this.compClass = typeof(CompDeadmanFashionWatcher);
        }
    }

    public class CompDeadmanMarker : ThingComp
    {
        public bool IsFromCorpse = false;
        public string OriginalOwnerName = null;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref IsFromCorpse, "IsFromCorpse", false);
            Scribe_Values.Look(ref OriginalOwnerName, "OriginalOwnerName", null);
        }
    }

    public class CompProperties_DeadmanMarker : CompProperties
    {
        public CompProperties_DeadmanMarker()
        {
            this.compClass = typeof(CompDeadmanMarker);
        }
    }

    [StaticConstructorOnStartup]
    public static class DeadmanFashionHarmony
    {
        static DeadmanFashionHarmony()
        {
            var harmony = new Harmony("Watcher.DeadmanFashion");

            PatchDefGenerator(harmony);
            PatchDeadMansApparelWorker(harmony);
            PatchCorpseButcher(harmony);
            PatchInitializeComps(harmony);
        }

        static void PatchDefGenerator(Harmony harmony)
        {
            try
            {
                var method = typeof(DefGenerator).GetMethod("GenerateImpliedDefs_PreResolve",
                    BindingFlags.Static | BindingFlags.Public);
                if (method != null)
                {
                    var postfix = typeof(DeadmanFashionHarmony).GetMethod("DefGeneratorPostfix",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
            catch { }
        }

        static void PatchDeadMansApparelWorker(Harmony harmony)
        {
            try
            {
                var workerType = typeof(ThoughtWorker).Assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "ThoughtWorker_DeadMansApparel");

                if (workerType != null)
                {
                    var method = workerType.GetMethod("CurrentStateInternal",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (method != null)
                    {
                        var prefix = typeof(DeadmanFashionHarmony).GetMethod("DeadMansApparelPrefix",
                            BindingFlags.Static | BindingFlags.NonPublic);
                        harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                    }
                }
            }
            catch { }
        }

        static void PatchCorpseButcher(Harmony harmony)
        {
            try
            {
                var method = typeof(Corpse).GetMethod("ButcherProducts",
                    BindingFlags.Instance | BindingFlags.Public);
                if (method != null)
                {
                    var postfix = typeof(DeadmanFashionHarmony).GetMethod("ButcherPostfix",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
            catch { }
        }

        static void PatchInitializeComps(Harmony harmony)
        {
            try
            {
                var method = typeof(ThingWithComps).GetMethod("InitializeComps",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method != null)
                {
                    var postfix = typeof(DeadmanFashionHarmony).GetMethod("InitializeCompsPostfix",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
            }
            catch { }
        }

        static void DefGeneratorPostfix()
        {
            var thoughtDef = DefDatabase<ThoughtDef>.GetNamed("DeadMansApparel", false);
            if (thoughtDef != null)
            {
                if (thoughtDef.nullifyingTraits == null)
                    thoughtDef.nullifyingTraits = new List<TraitDef>();

                if (!thoughtDef.nullifyingTraits.Contains(WatcherTraitDefOf.DeadmanFashionLover))
                {
                    thoughtDef.nullifyingTraits.Add(WatcherTraitDefOf.DeadmanFashionLover);
                }
            }
        }

        static bool DeadMansApparelPrefix(ThoughtWorker __instance, Pawn p, ref ThoughtState __result)
        {
            if (p.story?.traits?.HasTrait(WatcherTraitDefOf.DeadmanFashionLover) == true)
            {
                __result = ThoughtState.Inactive;
                return false;
            }
            return true;
        }

        static void ButcherPostfix(Corpse __instance, ref IEnumerable<Thing> __result)
        {
            if (__result == null) return;

            var innerPawn = __instance.InnerPawn;
            if (innerPawn == null) return;

            foreach (Thing thing in __result.ToList())
            {
                if (thing is Apparel apparel)
                {
                    if (!apparel.AllComps.Any(c => c is CompDeadmanMarker))
                    {
                        var comp = new CompDeadmanMarker();
                        comp.parent = apparel;
                        apparel.AllComps.Add(comp);
                        comp.Initialize(new CompProperties_DeadmanMarker());
                    }

                    var marker = apparel.GetComp<CompDeadmanMarker>();
                    if (marker != null)
                    {
                        marker.IsFromCorpse = true;
                        marker.OriginalOwnerName = innerPawn.Name?.ToStringFull ?? "Unknown";
                    }
                }
            }
        }

        static void InitializeCompsPostfix(ThingWithComps __instance)
        {
            if (__instance is Pawn pawn && !pawn.AllComps.Any(c => c is CompDeadmanFashionWatcher))
            {
                var comp = new CompDeadmanFashionWatcher();
                comp.parent = pawn;
                pawn.AllComps.Add(comp);
                comp.Initialize(new CompProperties_DeadmanFashionWatcher());
            }
        }
    }
}