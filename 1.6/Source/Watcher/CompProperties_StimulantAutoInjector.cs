using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;

//Этот код добавляет в игру механику автоматического введения стимуляторов - пояс с ампулами, который сам делает уколы, когда колонист устал или голоден.

namespace Watcher.Comps
{
    public class CompProperties_StimulantAutoInjector : CompProperties
    {
        public HediffDef hediffDef;
        public float severityPerCharge = 0.25f;
        public int checkIntervalTicks = 60;
        public int maxSafeDoses = 2;
        public float overdoseUnconsciousDurationDays = 1.0f;

        public CompProperties_StimulantAutoInjector()
        {
            compClass = typeof(CompStimulantAutoInjector);
        }
    }

    public class CompProperties_AbilityInjectStimulants : CompProperties_AbilityEffect
    {
        public HediffDef hediffDef;
        public float severity = 0.5f;
        public int ammoCost = 1;

        public CompProperties_AbilityInjectStimulants()
        {
            compClass = typeof(CompAbilityEffectInjectStimulants);
        }
    }

    public class CompStimulantAutoInjector : ThingComp
    {
        public CompProperties_StimulantAutoInjector Props => (CompProperties_StimulantAutoInjector)props;

        private int ticksUntilCheck = 0;
        private Pawn lastWearer;
        private CompApparelReloadable reloadableComp;
        private int dosesApplied = 0;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            reloadableComp = parent.GetComp<CompApparelReloadable>();
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            if (pawn.abilities == null) return;

            if (!pawn.abilities.AllAbilitiesForReading.Any(a => a.def.defName == "Watcher_InjectStimulants"))
            {
                var abilityDef = DefDatabase<AbilityDef>.GetNamed("Watcher_InjectStimulants", false);
                if (abilityDef != null)
                {
                    pawn.abilities.GainAbility(abilityDef);
                }
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            if (pawn.abilities == null) return;

            var ability = pawn.abilities.AllAbilitiesForReading.FirstOrDefault(a => a.def.defName == "Watcher_InjectStimulants");
            if (ability != null)
            {
                pawn.abilities.RemoveAbility(ability.def);
            }
            dosesApplied = 0;
        }

        public override void CompTick()
        {
            base.CompTick();

            Pawn currentWearer = null;
            if (parent is Apparel apparel)
            {
                currentWearer = apparel.Wearer;
            }

            if (currentWearer != lastWearer && lastWearer != null)
            {
                ticksUntilCheck = 0;
                dosesApplied = 0;
            }
            lastWearer = currentWearer;

            if (currentWearer == null) return;
            if (reloadableComp == null || reloadableComp.RemainingCharges <= 0) return;

            bool inCombatMode = IsInCombatMode(currentWearer);

            if (inCombatMode)
            {
                ticksUntilCheck--;
                if (ticksUntilCheck <= 0)
                {
                    ticksUntilCheck = Props.checkIntervalTicks;
                    TryAutoInject(currentWearer);
                }
            }
            else
            {
                ticksUntilCheck = Props.checkIntervalTicks;
            }
        }

        private bool IsInCombatMode(Pawn pawn)
        {
            if (pawn?.jobs?.curJob == null) return false;

            var curJob = pawn.jobs.curJob;
            var driver = pawn.jobs.curDriver;
            var stance = pawn.stances?.curStance;

            string jobDefName = curJob.def?.defName ?? "";
            string driverName = driver?.GetType().Name ?? "";
            string stanceName = stance?.GetType().Name ?? "";
            string verbType = curJob.verbToUse?.GetType().Name ?? "";

            if (jobDefName == "CastAbility" || jobDefName.Contains("Cast") || jobDefName.Contains("Ability"))
                return true;

            if (driver is JobDriver_CastAbility)
                return true;

            if (driverName.Contains("Cast") || driverName.Contains("Ability"))
                return true;

            if (stance is Stance_Warmup || stance is Stance_Cooldown)
                return true;

            if (stanceName.Contains("Warmup") || stanceName.Contains("Cooldown"))
                return true;

            if (curJob.verbToUse is Verb_CastAbility)
                return true;

            if (verbType.Contains("Cast") || verbType.Contains("Ability"))
                return true;

            if (pawn.abilities != null)
            {
                foreach (var ability in pawn.abilities.AllAbilitiesForReading)
                {
                    if (ability.Casting)
                        return true;
                }
            }

            if (pawn.Drafted && jobDefName == "Wait_Combat")
                return true;

            if (jobDefName == "AttackMelee" || jobDefName == "AttackStatic")
                return true;

            if (driver is JobDriver_Goto && pawn.Drafted)
                return true;

            if (jobDefName.Contains("Attack") || jobDefName.Contains("Combat"))
                return true;

            if (pawn.mindState?.enemyTarget != null && pawn.Drafted)
                return true;

            return false;
        }

        private void TryAutoInject(Pawn wearer)
        {
            float restLevel = wearer.needs?.rest?.CurLevelPercentage ?? 1f;
            float foodLevel = wearer.needs?.food?.CurLevelPercentage ?? 1f;

            bool needRest = restLevel < 0.20f;
            bool needFood = foodLevel < 0.20f;

            bool isVeryTired = false;
            bool isVeryHungry = false;

            try
            {
                if (wearer.needs?.mood?.thoughts?.memories != null)
                {
                    foreach (var memory in wearer.needs.mood.thoughts.memories.Memories)
                    {
                        if (memory?.def == null) continue;

                        string name = memory.def.defName;
                        int stage = memory.CurStageIndex;

                        if ((name == "Sleepy" || name == "Tired") && stage >= 2)
                            isVeryTired = true;

                        if ((name == "Hungry" && stage >= 2) || name == "UrgentlyHungry" || name == "Starving")
                            isVeryHungry = true;
                    }
                }
            }
            catch (System.Exception e)
            {
                //Log.Warning($"[StimulantBelt] Error reading thoughts: {e.Message}");
            }

            bool needInjection = (needRest || isVeryTired) || (needFood || isVeryHungry);

            if (needInjection && reloadableComp.RemainingCharges > 0)
            {
                InjectStimulants(wearer, true);
            }
        }

        public void InjectStimulants(Pawn wearer, bool isAuto)
        {
            if (dosesApplied >= Props.maxSafeDoses)
            {
                Overdose(wearer);
                return;
            }

            if (reloadableComp != null)
            {
                reloadableComp.UsedOnce();
            }

            dosesApplied++;

            Hediff hediff = wearer.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(Props.hediffDef, wearer);
                hediff.Severity = Props.severityPerCharge;
                wearer.health.AddHediff(hediff);
            }
            else
            {
                hediff.Severity = System.Math.Min(hediff.Severity + Props.severityPerCharge, 1.0f);
            }

            Need_Rest rest = wearer.needs?.TryGetNeed<Need_Rest>();
            if (rest != null) rest.CurLevel = 1.0f;

            Need_Food food = wearer.needs?.TryGetNeed<Need_Food>();
            if (food != null) food.CurLevel = 1.0f;

            if (wearer.Map != null)
            {
                string text = isAuto
                    ? $"Auto-Stim {dosesApplied}/{Props.maxSafeDoses}"
                    : $"Injected {dosesApplied}/{Props.maxSafeDoses}";

                MoteMaker.ThrowText(wearer.DrawPos, wearer.Map, text, UnityEngine.Color.cyan);
            }

            if (!isAuto)
            {
                Messages.Message("StimulantBelt_ManualInjected".Translate(wearer.LabelShort, dosesApplied, Props.maxSafeDoses, reloadableComp?.RemainingCharges ?? 0, reloadableComp?.MaxCharges ?? 0),
                    wearer, MessageTypeDefOf.PositiveEvent);
            }
        }

        private void Overdose(Pawn pawn)
        {
            HediffDef overdoseDef = DefDatabase<HediffDef>.GetNamed("Watcher_StimulantOverdose", false);

            if (overdoseDef != null)
            {
                Hediff overdose = HediffMaker.MakeHediff(overdoseDef, pawn);
                pawn.health.AddHediff(overdose);
            }
            else
            {
                Hediff anesthetic = HediffMaker.MakeHediff(HediffDefOf.Anesthetic, pawn);
                pawn.health.AddHediff(anesthetic);
            }

            Messages.Message("StimulantBelt_Overdose".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NegativeHealthEvent);
            dosesApplied = 0;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksUntilCheck, "ticksUntilCheck", 0);
            Scribe_References.Look(ref lastWearer, "lastWearer");
            Scribe_Values.Look(ref dosesApplied, "dosesApplied", 0);
        }

        public override string CompInspectStringExtra()
        {
            if (reloadableComp == null) return null;
            return $"Cartridges: {reloadableComp.RemainingCharges}/{reloadableComp.MaxCharges} | Doses: {dosesApplied}/{Props.maxSafeDoses}";
        }
    }

    public class CompAbilityEffectInjectStimulants : CompAbilityEffect
    {
        public new CompProperties_AbilityInjectStimulants Props => (CompProperties_AbilityInjectStimulants)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent.pawn;
            if (caster == null) return;

            Apparel belt = FindStimulantBelt(caster);
            if (belt == null)
            {
                Messages.Message("StimulantBelt_Required".Translate(), caster, MessageTypeDefOf.RejectInput);
                return;
            }

            var autoComp = belt.GetComp<CompStimulantAutoInjector>();
            if (autoComp == null)
            {
                Messages.Message("StimulantBelt_ComponentMissing".Translate(), caster, MessageTypeDefOf.RejectInput);
                return;
            }

            var reloadableComp = belt.GetComp<CompApparelReloadable>();
            if (reloadableComp == null || reloadableComp.RemainingCharges < Props.ammoCost)
            {
                Messages.Message("StimulantBelt_NoAmmo".Translate(), caster, MessageTypeDefOf.RejectInput);
                return;
            }

            autoComp.InjectStimulants(caster, false);
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn caster = parent.pawn;
            if (caster == null) return false;

            Apparel belt = FindStimulantBelt(caster);
            if (belt == null)
            {
                if (throwMessages) Messages.Message("StimulantBelt_Required".Translate(), caster, MessageTypeDefOf.RejectInput);
                return false;
            }

            var reloadableComp = belt.GetComp<CompApparelReloadable>();
            if (reloadableComp == null || reloadableComp.RemainingCharges < Props.ammoCost)
            {
                if (throwMessages) Messages.Message("StimulantBelt_NeedCartridge".Translate(Props.ammoCost), caster, MessageTypeDefOf.RejectInput);
                return false;
            }

            return base.Valid(target, throwMessages);
        }

        private Apparel FindStimulantBelt(Pawn pawn)
        {
            return pawn.apparel?.WornApparel.FirstOrDefault(a =>
                a.def.defName == "Watcher_StimulantBelt" ||
                a.GetComp<CompStimulantAutoInjector>() != null);
        }
    }

    [StaticConstructorOnStartup]
    public static class PatchLoader
    {
        static PatchLoader()
        {
            var harmony = new Harmony("Watcher.StimulantBelt");
            // Убрать: harmony.PatchAll();

            // Если нужны патчи для StimulantBelt - добавлять вручную:
            // harmony.Patch(...);
        }
    }
}