using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Watcher.Comps
{
    public class CompProperties_UseEffectInstallImplantAnesthetic : CompProperties_UseEffect
    {
        public HediffDef hediffDef;
        public BodyPartDef bodyPart;
        public bool canBeUsedByNonColonists = false;
        public float anestheticSeverity = 1.0f;

        public CompProperties_UseEffectInstallImplantAnesthetic()
        {
            compClass = typeof(CompUseEffectInstallImplantAnesthetic);
        }
    }

    public class CompUseEffectInstallImplantAnesthetic : CompUseEffect
    {
        public CompProperties_UseEffectInstallImplantAnesthetic Props => (CompProperties_UseEffectInstallImplantAnesthetic)props;

        public override void DoEffect(Pawn usedBy)
        {
            BodyPartRecord targetPart = FindTargetBodyPart(usedBy);
            if (targetPart == null)
            {
                Messages.Message("Cannot install: no suitable body part found.", usedBy, MessageTypeDefOf.RejectInput);
                return;
            }

            // Удаляем MissingPart если нога отсутствует
            Hediff_MissingPart missingPart = null;
            foreach (Hediff hediff in usedBy.health.hediffSet.hediffs.ToList())
            {
                if (hediff is Hediff_MissingPart mp && hediff.Part == targetPart)
                {
                    missingPart = mp;
                    break;
                }
            }

            if (missingPart != null)
            {
                usedBy.health.RemoveHediff(missingPart);
            }

            // Удаляем другие хедифы с этой части
            foreach (Hediff hediff in usedBy.health.hediffSet.hediffs.ToList())
            {
                if (hediff.Part == targetPart && hediff.def != Props.hediffDef)
                {
                    usedBy.health.RemoveHediff(hediff);
                }
            }

            // Добавляем имплант
            Hediff implant = HediffMaker.MakeHediff(Props.hediffDef, usedBy, targetPart);
            usedBy.health.AddHediff(implant, targetPart);

            // Анестезия
            Hediff anesthetic = HediffMaker.MakeHediff(HediffDefOf.Anesthetic, usedBy);
            anesthetic.Severity = Props.anestheticSeverity;
            usedBy.health.AddHediff(anesthetic);

            // Уничтожаем предмет
            parent.SplitOff(1).Destroy();

            Messages.Message($"{usedBy.LabelShort} has installed {Props.hediffDef.label} and fallen unconscious.", usedBy, MessageTypeDefOf.NeutralEvent);
        }

        private BodyPartRecord FindTargetBodyPart(Pawn pawn)
        {
            if (pawn?.RaceProps?.body == null) return null;

            foreach (BodyPartRecord part in pawn.RaceProps.body.GetPartsWithDef(Props.bodyPart))
            {
                // Проверяем, нет ли уже такого импланта
                bool hasImplant = false;
                foreach (Hediff h in pawn.health.hediffSet.hediffs)
                {
                    if (h.def == Props.hediffDef && h.Part == part)
                    {
                        hasImplant = true;
                        break;
                    }
                }

                if (!hasImplant)
                {
                    // Подходит если часть здорова ИЛИ отсутствует (MissingPart)
                    bool isHealthy = pawn.health.hediffSet.GetPartHealth(part) > 0;
                    bool isMissing = false;

                    foreach (Hediff h in pawn.health.hediffSet.hediffs)
                    {
                        if (h is Hediff_MissingPart && h.Part == part)
                        {
                            isMissing = true;
                            break;
                        }
                    }

                    if (isHealthy || isMissing)
                        return part;
                }
            }

            return null;
        }

        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            if (!Props.canBeUsedByNonColonists && p.Faction != Faction.OfPlayer)
            {
                return "CannotUseReason".Translate("Must be a colonist");
            }

            BodyPartRecord part = FindTargetBodyPart(p);
            if (part == null)
            {
                return "CannotUseReason".Translate($"No available {Props.bodyPart.label} for replacement");
            }

            return true;
        }
    }
}