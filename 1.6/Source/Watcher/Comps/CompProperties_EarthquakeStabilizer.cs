using RimWorld;
using Verse;
using System.Linq;

namespace Watcher.Comps
{
    public class CompProperties_WatcherSeismicStabilizer : CompProperties
    {
        public bool destroyAfterUse = true;
        public int rechargeTicks = 60000;
        public int maxUses = -1;
        public bool showLoreWarning = true;

        public CompProperties_WatcherSeismicStabilizer()
        {
            compClass = typeof(CompWatcherSeismicStabilizer);
        }
    }

    public class CompWatcherSeismicStabilizer : ThingComp
    {
        private bool watcherActivated = false;
        private int watcherRechargeCountdown = 0;
        private int watcherUsesRemaining = -1;
        private bool watcherIsBroken = false;

        private CompPowerTrader watcherPowerComp;
        private CompFlickable watcherFlickableComp;

        private CompProperties_WatcherSeismicStabilizer WatcherProps => (CompProperties_WatcherSeismicStabilizer)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            watcherPowerComp = parent.GetComp<CompPowerTrader>();
            watcherFlickableComp = parent.GetComp<CompFlickable>();

            if (!respawningAfterLoad)
            {
                watcherUsesRemaining = WatcherProps.maxUses;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref watcherActivated, "watcherActivated", false);
            Scribe_Values.Look(ref watcherRechargeCountdown, "watcherRechargeCountdown", 0);
            Scribe_Values.Look(ref watcherUsesRemaining, "watcherUsesRemaining", -1);
            Scribe_Values.Look(ref watcherIsBroken, "watcherIsBroken", false);
        }

        public override void CompTick()
        {
            base.CompTick();

            if (watcherIsBroken) return;

            if (watcherRechargeCountdown > 0)
            {
                watcherRechargeCountdown--;
                if (watcherRechargeCountdown <= 0)
                {
                    watcherActivated = false;
                }
                return;
            }

            if (watcherActivated) return;

            if (!WatcherHasPowerAndSwitchedOn()) return;

            if (WatcherIsEarthquakeActive())
            {
                WatcherActivateStabilizer();
            }
        }

        private bool WatcherHasPowerAndSwitchedOn()
        {
            if (watcherPowerComp != null && !watcherPowerComp.PowerOn) return false;
            if (watcherFlickableComp != null && !watcherFlickableComp.SwitchIsOn) return false;
            return true;
        }

        private bool WatcherIsEarthquakeActive()
        {
            if (parent.Map == null) return false;

            return parent.Map.gameConditionManager.ActiveConditions
                .Any(gc => gc.def.defName == "Earthquake");
        }

        private void WatcherActivateStabilizer()
        {
            watcherActivated = true;

            if (parent.Map != null)
            {
                var earthquakeCondition = parent.Map.gameConditionManager.ActiveConditions
                    .FirstOrDefault(gc => gc.def.defName == "Earthquake");

                if (earthquakeCondition != null)
                {
                    earthquakeCondition.End();
                    WatcherSendStabilizationLetter();
                }
            }

            if (watcherUsesRemaining > 0)
            {
                watcherUsesRemaining--;
                if (watcherUsesRemaining == 0)
                {
                    watcherIsBroken = true;
                    Messages.Message("Watcher_StabilizerBroken".Translate(parent.Label),
                        parent, MessageTypeDefOf.NegativeEvent);
                    return;
                }
            }

            if (WatcherProps.destroyAfterUse)
            {
                parent.Destroy(DestroyMode.Vanish);
            }
            else
            {
                watcherRechargeCountdown = WatcherProps.rechargeTicks;
                Messages.Message("Watcher_StabilizerRecharging".Translate(parent.Label,
                    (WatcherProps.rechargeTicks / 2500f).ToString("F1")),
                    parent, MessageTypeDefOf.NeutralEvent);
            }
        }

        private void WatcherSendStabilizationLetter()
        {
            LetterDef letterDef = DefDatabase<LetterDef>.GetNamed("Watcher_SeismicStabilized", false);
            if (letterDef == null)
                letterDef = LetterDefOf.PositiveEvent;

            string label = "Watcher_SeismicStabilized_Label".Translate();

            string textKey = WatcherProps.destroyAfterUse
                ? "Watcher_SeismicStabilized_Text"
                : "Watcher_SeismicStabilized_Text_Reusable";

            string text = textKey.Translate(parent.Label);

            Find.LetterStack.ReceiveLetter(label, text, letterDef, new LookTargets(parent));
        }

        public override string CompInspectStringExtra()
        {
            if (watcherIsBroken) return "Watcher_StabilizerBrokenInspect".Translate();

            if (watcherRechargeCountdown > 0)
            {
                float hoursLeft = watcherRechargeCountdown / 2500f;
                return "Watcher_RechargingHours".Translate(hoursLeft.ToString("F1"));
            }

            if (watcherActivated) return "Watcher_Stabilizing".Translate();

            if (!WatcherHasPowerAndSwitchedOn())
                return "Watcher_NeedsPowerOrOff".Translate();

            if (WatcherIsEarthquakeActive())
                return "Watcher_EarthquakeDetected".Translate();

            string status = "Watcher_MonitoringSeismic".Translate();

            if (WatcherProps.showLoreWarning)
            {
                if (WatcherProps.destroyAfterUse)
                {
                    status += "\n" + "Watcher_Tverd_SingleUseWarning".Translate();
                }
                else
                {
                    status += "\n" + "Watcher_Tverd_ReusableWarning".Translate();
                }
            }

            if (watcherUsesRemaining > 0)
            {
                status += " (" + "Watcher_UsesRemaining".Translate(watcherUsesRemaining) + ")";
            }
            else if (watcherUsesRemaining == -1 && !WatcherProps.destroyAfterUse)
            {
                status += " (" + "Watcher_InfiniteUses".Translate() + ")";
            }

            return status;
        }
    }
}