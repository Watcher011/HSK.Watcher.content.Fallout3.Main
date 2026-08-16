using RimWorld;
using System.Collections.Generic;
using Verse;

//Этот код добавляет в игру механику лечения зависимостей через употребление определённых веществ/предметов.


namespace Watcher.Hediffs
{
    public class IngestionOutcomeDoer_RemoveAddictions : IngestionOutcomeDoer
    {
        public List<HediffDef> addictionsToRemove;

        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            if (pawn?.health?.hediffSet == null || addictionsToRemove == null)
                return;

            foreach (HediffDef addictionDef in addictionsToRemove)
            {
                if (addictionDef != null)
                {
                    Hediff addiction = pawn.health.hediffSet.GetFirstHediffOfDef(addictionDef);
                    if (addiction != null)
                    {
                        pawn.health.RemoveHediff(addiction);
                    }
                }
            }

            // Также удаляем DrugDesire
            HediffDef drugDesireDef = DefDatabase<HediffDef>.GetNamedSilentFail("DrugDesire");
            if (drugDesireDef != null)
            {
                Hediff drugDesire = pawn.health.hediffSet.GetFirstHediffOfDef(drugDesireDef);
                if (drugDesire != null)
                {
                    pawn.health.RemoveHediff(drugDesire);
                }
            }
        }
    }
}