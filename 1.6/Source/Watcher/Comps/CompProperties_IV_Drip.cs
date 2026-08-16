using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Watcher.Comps
{
    public class CompProperties_IV_Drip : CompProperties
    {
        public float radius;
        public int requiredTicksNear;
        public List<HediffDef> hediffsToTreat;
        public float fuelConsumedPerTreatment;

        public CompProperties_IV_Drip()
        {
            compClass = typeof(CompIV_Drip);
            radius = 2f;
            requiredTicksNear = 250;
            fuelConsumedPerTreatment = 1f;
        }
    }

    public class CompIV_Drip : ThingComp
    {
        private CompRefuelable refuelable;
        private CompPowerTrader power;
        private CompFlickable flickable;

        private Dictionary<Pawn, int> pawnsNearTicks = new Dictionary<Pawn, int>();
        private Dictionary<Pawn, IntVec3> lastPositions = new Dictionary<Pawn, IntVec3>();

        public CompProperties_IV_Drip Props => (CompProperties_IV_Drip)props;

        public override void PostPostMake()
        {
            base.PostPostMake();
            refuelable = parent.GetComp<CompRefuelable>();
            power = parent.GetComp<CompPowerTrader>();
            flickable = parent.GetComp<CompFlickable>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref pawnsNearTicks, "pawnsNearTicks", LookMode.Reference, LookMode.Value);
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!parent.IsHashIntervalTick(60))
                return;

            if (flickable != null && !flickable.SwitchIsOn)
            {
                ClearTracking();
                return;
            }

            if (power != null && !power.PowerOn)
            {
                ClearTracking();
                return;
            }

            if (refuelable != null && !refuelable.HasFuel)
            {
                ClearTracking();
                return;
            }

            ProcessNearbyPawns();
        }

        private void ClearTracking()
        {
            pawnsNearTicks.Clear();
            lastPositions.Clear();
        }

        private void ProcessNearbyPawns()
        {
            if (parent.Map == null) return;

            var pawnsInRadius = parent.Map.mapPawns.AllPawnsSpawned
                .Where(p => p != null && !p.Dead && p.RaceProps?.IsFlesh == true
                    && p.Position.DistanceTo(parent.Position) <= Props.radius)
                .ToList();

            var gonePawns = pawnsNearTicks.Keys.Where(p => !pawnsInRadius.Contains(p)).ToList();
            foreach (var pawn in gonePawns)
            {
                pawnsNearTicks.Remove(pawn);
                lastPositions.Remove(pawn);
            }

            foreach (Pawn pawn in pawnsInRadius)
            {
                if (!HasTreatableCondition(pawn))
                {
                    if (pawnsNearTicks.ContainsKey(pawn))
                    {
                        pawnsNearTicks.Remove(pawn);
                        lastPositions.Remove(pawn);
                    }
                    continue;
                }

                if (lastPositions.ContainsKey(pawn) && lastPositions[pawn] != pawn.Position)
                {
                    pawnsNearTicks[pawn] = 0;
                }
                lastPositions[pawn] = pawn.Position;

                if (!pawnsNearTicks.ContainsKey(pawn))
                    pawnsNearTicks[pawn] = 0;

                pawnsNearTicks[pawn] += 60;

                if (pawnsNearTicks[pawn] >= Props.requiredTicksNear)
                {
                    TreatPawn(pawn);
                }
            }
        }

        private bool HasTreatableCondition(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return false;

            if (Props.hediffsToTreat != null)
            {
                foreach (var hediffDef in Props.hediffsToTreat)
                {
                    if (hediffDef == null) continue;
                    var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                    if (hediff != null && hediff.Severity > 0.001f)
                        return true;
                }
            }

            return false;
        }

        private void TreatPawn(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null) return;

            bool treated = false;

            if (Props.hediffsToTreat != null)
            {
                foreach (var hediffDef in Props.hediffsToTreat)
                {
                    if (hediffDef == null) continue;
                    var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                    if (hediff != null && hediff.Severity > 0.001f)
                    {
                        pawn.health.RemoveHediff(hediff);
                        treated = true;
                    }
                }
            }

            if (treated)
            {
                if (refuelable != null)
                {
                    refuelable.ConsumeFuel(Props.fuelConsumedPerTreatment);
                }

                pawnsNearTicks.Remove(pawn);
                lastPositions.Remove(pawn);

                FleckMaker.ThrowMetaIcon(parent.Position, parent.Map, FleckDefOf.HealingCross);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (flickable != null && !flickable.SwitchIsOn)
                return "IV_Drip_SwitchedOff".Translate();

            if (power != null && !power.PowerOn)
                return "IV_Drip_NoPower".Translate();

            if (refuelable != null && !refuelable.HasFuel)
                return "IV_Drip_RequiresHemogenPacks".Translate();

            int waiting = pawnsNearTicks.Count;
            int ready = pawnsNearTicks.Count(kvp => kvp.Value >= Props.requiredTicksNear);

            if (ready > 0)
                return "IV_Drip_TreatingPatient".Translate();
            else if (waiting > 0)
                return "IV_Drip_WaitingPatients".Translate(waiting);

            return "IV_Drip_Ready".Translate();
        }
    }
}