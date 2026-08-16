using Mono.Unix.Native;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace Watcher.Comps
{
    public class CompPoweredApparel : ThingComp
    {
        public CompProperties_PoweredApparel Props => (CompProperties_PoweredApparel)props;
        public CompRefuelable refuelableComp;
        public bool wasPowered = true;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            refuelableComp = parent.GetComp<CompRefuelable>();
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            refuelableComp = parent.GetComp<CompRefuelable>();
            UpdateApparelStats();
        }

        public override void CompTick()
        {
            base.CompTick();

            if (Find.TickManager.TicksGame % 60 == 0) // Проверяем каждую секунду
            {
                UpdateApparelStats();
                CheckFuelWarnings();
            }
        }

        private void UpdateApparelStats()
        {
            bool isPowered = refuelableComp != null && refuelableComp.HasFuel;

            if (isPowered != wasPowered)
            {
                wasPowered = isPowered;
                Notify_StatsChanged();
            }
        }

        private void CheckFuelWarnings()
        {
            if (refuelableComp == null) return;

            Pawn wearer = GetWearer();
            if (wearer == null || !wearer.IsColonistPlayerControlled) return;

            float fuelPercent = refuelableComp.Fuel / refuelableComp.Props.fuelCapacity;

            if (fuelPercent <= 0f)
            {
                if (Rand.MTBEventOccurs(10f, 1f, 60f))
                {
                    Messages.Message(Props.outOfFuelMessage.Translate(wearer.LabelShort, parent.Label), MessageTypeDefOf.CautionInput);
                }
            }
            else if (fuelPercent <= Props.lowFuelThreshold)
            {
                if (Rand.MTBEventOccurs(30f, 1f, 60f))
                {
                    Messages.Message(Props.lowFuelMessage.Translate(wearer.LabelShort, parent.Label), MessageTypeDefOf.NeutralEvent);
                }
            }
        }

        private Pawn GetWearer()
        {
            Apparel apparel = parent as Apparel;
            return apparel?.Wearer;
        }

        private void Notify_StatsChanged()
        {
            Pawn wearer = GetWearer();
            if (wearer != null)
            {
                wearer.apparel.Notify_ApparelChanged();
            }
        }

        public float GetStatOffset(StatDef stat)
        {
            if (refuelableComp == null || !refuelableComp.HasFuel)
            {
                if (Props.unpoweredStats.TryGetValue(stat, out float unpoweredValue))
                {
                    return unpoweredValue;
                }
            }
            else
            {
                if (Props.poweredStats.TryGetValue(stat, out float poweredValue))
                {
                    return poweredValue;
                }
            }
            return 0f;
        }

        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            yield return new StatDrawEntry(
                StatCategoryDefOf.Apparel,
                "Требует топлива",
                "Да",
                "Броня требует регулярной заправки для работы на полную мощность",
                1000
            );

            if (refuelableComp != null)
            {
                yield return new StatDrawEntry(
                    StatCategoryDefOf.Apparel,
                    "Расход топлива",
                    refuelableComp.Props.fuelConsumptionRate.ToString("F1") + "/день",
                    "Скорость расхода топлива при активном использовании",
                    999
                );
            }

            yield return new StatDrawEntry(
                StatCategoryDefOf.Apparel,
                "Статы при питании",
                "Максимальные",
                "Все характеристики работают на полную мощность",
                998
            );

            yield return new StatDrawEntry(
                StatCategoryDefOf.Apparel,
                "Статы без питания",
                "Пониженные",
                "Характеристики снижены при отсутствии топлива",
                997
            );
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref wasPowered, "wasPowered", true);
        }
    }

    public class CompProperties_PoweredApparel : CompProperties
    {
        public Dictionary<StatDef, float> poweredStats = new Dictionary<StatDef, float>();
        public Dictionary<StatDef, float> unpoweredStats = new Dictionary<StatDef, float>();
        public float lowFuelThreshold = 0.2f;
        public string lowFuelMessage = "Low fuel warning";
        public string outOfFuelMessage = "Out of fuel";

        public CompProperties_PoweredApparel()
        {
            compClass = typeof(CompPoweredApparel);
        }
    }

}
