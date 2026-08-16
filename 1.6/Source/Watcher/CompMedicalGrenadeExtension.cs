using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

//Этот код добавляет в игру механику медицинской пены - вещество, которое залечивает раны и покрывает поверхность пеной.

namespace Watcher.Comps
{
    // === DEFMODEXTENSION ===
    public class MedicalFoamExtension : DefModExtension
    {
        public float tendQualityMin = 0.8f;
        public float tendQualityMax = 1.0f;
        public int maxWounds = 5;
        public float radius = 3.9f;
    }

    // === DEFOF (исправлено - статический конструктор обязателен) ===
    [DefOf]
    public static class WatcherHediffDefOf
    {
        public static HediffDef Watcher_CoveredInFoam;

        static WatcherHediffDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WatcherHediffDefOf));
        }
    }

    // === DAMAGEWORKER ДЛЯ CE ===
    public class DamageWorker_MedicalFoam_CE : DamageWorker
    {
        public override DamageResult Apply(DamageInfo dinfo, Thing thing)
        {
            DamageResult result = new DamageResult();

            if (thing == null || !(thing is Pawn pawn))
                return result;

            if (pawn.health?.hediffSet == null)
                return result;

            if (!pawn.RaceProps.Humanlike)
                return result;

            var ext = dinfo.Instigator?.def.GetModExtension<MedicalFoamExtension>();
            float tendQuality = Rand.Range(ext?.tendQualityMin ?? 0.8f, ext?.tendQualityMax ?? 1.0f);
            int maxWounds = ext?.maxWounds ?? 5;

            try
            {
                var hediffs = pawn.health.hediffSet.hediffs
                    .Where(h => h != null && (
                        h.Bleeding ||
                        (h is Hediff_Injury inj && !inj.IsPermanent() && inj.CanHealNaturally())
                    ))
                    .OrderByDescending(h => h.Severity)
                    .Take(maxWounds)
                    .ToList();

                int treatedCount = 0;

                foreach (var hediff in hediffs)
                {
                    if (hediff == null) continue;

                    if (hediff is Hediff_Injury injury)
                    {
                        injury.Tended(tendQuality, tendQuality);
                        injury.Heal(injury.Severity * 0.3f);
                        treatedCount++;
                    }
                    else if (hediff.Bleeding)
                    {
                        hediff.Tended(tendQuality, tendQuality);
                        treatedCount++;
                    }
                }

                ApplyFoamVisuals(pawn);
                SpawnGroundFoam(pawn, ext?.radius ?? 3.9f);

                if (treatedCount > 0 && pawn.Faction == Faction.OfPlayer)
                {
                    Messages.Message($"Medical foam sealed {treatedCount} wounds on {pawn.LabelShort}!",
                        pawn, MessageTypeDefOf.PositiveEvent);
                }
            }
            catch (Exception ex)
            {
                //Log.Warning($"[Watcher] MedicalFoam CE error: {ex.Message}");
            }

            return result;
        }

        private void ApplyFoamVisuals(Pawn pawn)
        {
            if (WatcherHediffDefOf.Watcher_CoveredInFoam == null) return;

            var oldFoam = pawn.health.hediffSet.GetFirstHediffOfDef(WatcherHediffDefOf.Watcher_CoveredInFoam);
            if (oldFoam != null)
                pawn.health.RemoveHediff(oldFoam);

            var foam = HediffMaker.MakeHediff(WatcherHediffDefOf.Watcher_CoveredInFoam, pawn);
            if (foam != null)
            {
                foam.Severity = Rand.Range(0.8f, 1.2f);
                pawn.health.AddHediff(foam);
            }
        }

        private void SpawnGroundFoam(Pawn pawn, float radius)
        {
            if (pawn.Map == null || !pawn.Position.IsValid) return;

            var foamDef = ThingDef.Named("Filth_FireFoam") ?? ThingDef.Named("Filth_Ash");
            if (foamDef == null) return;

            FilthMaker.TryMakeFilth(pawn.Position, pawn.Map, foamDef, 3);

            int cells = GenRadial.NumCellsInRadius(radius);
            for (int i = 1; i < cells && i < 20; i++)
            {
                IntVec3 cell = pawn.Position + GenRadial.RadialPattern[i];
                if (cell.InBounds(pawn.Map) && Rand.Chance(0.6f))
                {
                    FilthMaker.TryMakeFilth(cell, pawn.Map, foamDef, Rand.Range(1, 2));
                }
            }
        }
    }

    // === HEDIFF ПЕНЫ ===
    public class Hediff_CoveredInFoam : HediffWithComps
    {
        private int tickCounter = 0;

        public override void Tick()
        {
            base.Tick();
            tickCounter++;
            if (tickCounter < 60) return;
            tickCounter = 0;

            if (pawn?.Spawned != true || pawn.Map == null) return;

            if (Rand.Chance(0.3f))
            {
                IntVec3 cell = pawn.Position + GenAdj.AdjacentCells[Rand.Range(0, 8)];
                if (cell.InBounds(pawn.Map))
                {
                    var foamDef = ThingDef.Named("Filth_FireFoam") ?? ThingDef.Named("Filth_Ash");
                    if (foamDef != null)
                    {
                        try { FilthMaker.TryMakeFilth(cell, pawn.Map, foamDef, 1); }
                        catch { }
                    }
                }
            }
        }
    }
}