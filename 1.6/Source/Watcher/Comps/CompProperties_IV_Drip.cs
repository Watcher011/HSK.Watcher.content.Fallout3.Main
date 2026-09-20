using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Watcher.Comps
{
    public class CompProperties_IV_Drip : CompProperties
    {
        public int requiredTicksNear;
        public List<HediffDef> hediffsToTreat;
        public float fuelConsumedPerTreatment;

        // Хедиф, который капельница накладывает и постоянно поддерживает на целевом
        // уровне severity, пока пациент лежит в привязанной кровати, а машина
        // включена/запитана/с топливом. Никак не связан с hediffsToTreat — работает
        // всегда, независимо от того, есть ли что лечить (например, StimpakBasic).
        public HediffDef maintainedHediff;

        // Целевая severity, которую капельница поддерживает. -1 (по умолчанию)
        // означает "взять initialSeverity из самого HediffDef", чтобы не дублировать
        // это значение в двух местах (XML хедифа и XML капельницы).
        public float maintainedHediffSeverity = -1f;

        // Необязательный расход топлива за каждый интервал поддержки (по умолчанию 0 —
        // поддержка бесплатна). Если > 0, при нехватке топлива поддержка на этом
        // интервале просто пропускается (хедиф спокойно начнёт угасать сам).
        public float fuelConsumedPerMaintainTick;

        public CompProperties_IV_Drip()
        {
            compClass = typeof(CompIV_Drip);
            requiredTicksNear = 250;
            fuelConsumedPerTreatment = 1f;
        }
    }

    /// <summary>
    /// Капельница-стойка для кровати. Лечит только тех, кто в данный момент лежит
    /// в кровати рядом (в радиусе CompProperties_Facility.maxDistance) — не любых
    /// пешек поблизости. Радиус поиска кровати задаётся тем же maxDistance, что и
    /// у facility-бонуса (MedicalTendQualityOffset и т.д.), чтобы не расходиться
    /// с визуальной "подключённостью", которую показывает PlaceWorker.
    ///
    /// Помимо лечения hediffsToTreat (например, снятие BloodLoss после
    /// requiredTicksNear тиков), параллельно поддерживает maintainedHediff
    /// (например, StimpakBasic) на целевой severity всё время, пока пациент
    /// подключён — независимо от прогресса лечения кровотечения.
    /// </summary>
    public class CompIV_Drip : ThingComp
    {
        private CompRefuelable refuelable;
        private CompPowerTrader power;
        private CompFlickable flickable;
        private CompProperties_Facility facilityProps;

        // Сколько тиков подряд пациент непрерывно лежит в привязанной кровати
        // с невылеченным состоянием.
        private Dictionary<Pawn, int> pawnsNearTicks = new Dictionary<Pawn, int>();

        public CompProperties_IV_Drip Props => (CompProperties_IV_Drip)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            refuelable = parent.GetComp<CompRefuelable>();
            power = parent.GetComp<CompPowerTrader>();
            flickable = parent.GetComp<CompFlickable>();
            // Берём maxDistance прямо из деф-данных (не из рантайм-компонента) —
            // CompProperties_Facility.maxDistance это обычное поле в XML, доступное
            // через GetCompProperties, без зависимости от внутреннего API CompFacility,
            // которое отличается между версиями игры.
            facilityProps = parent.def.GetCompProperties<CompProperties_Facility>();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref pawnsNearTicks, "pawnsNearTicks", LookMode.Reference, LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pawnsNearTicks == null)
                {
                    pawnsNearTicks = new Dictionary<Pawn, int>();
                }

                List<Pawn> badKeys = pawnsNearTicks.Keys
                    .Where(p => p == null || p.Dead || p.Destroyed)
                    .ToList();

                foreach (Pawn badKey in badKeys)
                {
                    pawnsNearTicks.Remove(badKey);
                }
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            pawnsNearTicks.Clear();
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!parent.IsHashIntervalTick(60))
                return;

            if (flickable != null && !flickable.SwitchIsOn)
            {
                pawnsNearTicks.Clear();
                return;
            }

            if (power != null && !power.PowerOn)
            {
                pawnsNearTicks.Clear();
                return;
            }

            if (refuelable != null && !refuelable.HasFuel)
            {
                pawnsNearTicks.Clear();
                return;
            }

            ProcessBedOccupants();
        }

        /// <summary>
        /// Собирает пациентов из кроватей рядом с капельницей. Ищет клетки в радиусе
        /// CompProperties_Facility.maxDistance (fallback 2, если facility-компонента
        /// почему-то нет в def), находит на них Building_Bed через GetEdifice и
        /// возвращает текущих обитателей каждой найденной кровати.
        /// </summary>
        private IEnumerable<Pawn> GetLinkedBedOccupants()
        {
            if (parent.Map == null) yield break;

            float maxDistance = facilityProps != null ? facilityProps.maxDistance : 2f;

            HashSet<Building_Bed> bedsFound = new HashSet<Building_Bed>();

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, maxDistance, true))
            {
                if (!cell.InBounds(parent.Map)) continue;

                if (cell.GetEdifice(parent.Map) is Building_Bed bed)
                {
                    bedsFound.Add(bed);
                }
            }

            foreach (Building_Bed bed in bedsFound)
            {
                foreach (Pawn occupant in bed.CurOccupants)
                {
                    yield return occupant;
                }
            }
        }

        private void ProcessBedOccupants()
        {
            List<Pawn> occupants = GetLinkedBedOccupants()
                .Where(p => p != null && !p.Dead && p.RaceProps?.IsFlesh == true)
                .ToList();

            // Пациент встал/увезён/сменил кровать — сбрасываем накопленный прогресс.
            List<Pawn> gonePawns = pawnsNearTicks.Keys.Where(p => !occupants.Contains(p)).ToList();
            foreach (Pawn pawn in gonePawns)
            {
                pawnsNearTicks.Remove(pawn);
            }

            foreach (Pawn pawn in occupants)
            {
                // Поддержка StimpakBasic (или любого другого настроенного хедифа) —
                // безусловно, пока пациент подключён. Не зависит от наличия
                // кровотечения: работает как отдельный, параллельный эффект.
                MaintainSupportHediff(pawn);

                if (!HasTreatableCondition(pawn))
                {
                    pawnsNearTicks.Remove(pawn);
                    continue;
                }

                if (!pawnsNearTicks.ContainsKey(pawn))
                    pawnsNearTicks[pawn] = 0;

                pawnsNearTicks[pawn] += 60;

                if (pawnsNearTicks[pawn] >= Props.requiredTicksNear)
                {
                    TreatPawn(pawn);
                }
            }
        }

        /// <summary>
        /// Накладывает Props.maintainedHediff, если его ещё нет, и подтягивает severity
        /// вверх до целевого значения, если она успела просесть (из-за встроенного
        /// в сам HediffDef severityPerDay). Никогда не понижает severity сама —
        /// если у хедифа уже выше целевого уровня (например, от других источников),
        /// капельница это не трогает.
        /// </summary>
        private void MaintainSupportHediff(Pawn pawn)
        {
            if (Props.maintainedHediff == null) return;
            if (pawn?.health?.hediffSet == null) return;

            if (Props.fuelConsumedPerMaintainTick > 0f)
            {
                if (refuelable != null && refuelable.Fuel < Props.fuelConsumedPerMaintainTick)
                    return;

                refuelable?.ConsumeFuel(Props.fuelConsumedPerMaintainTick);
            }

            float targetSeverity = Props.maintainedHediffSeverity >= 0f
                ? Props.maintainedHediffSeverity
                : Props.maintainedHediff.initialSeverity;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.maintainedHediff);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(Props.maintainedHediff, pawn);
                hediff.Severity = targetSeverity;
                pawn.health.AddHediff(hediff);
            }
            else if (hediff.Severity < targetSeverity)
            {
                hediff.Severity = targetSeverity;
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

            if (refuelable != null && refuelable.Fuel < Props.fuelConsumedPerTreatment)
                return;

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