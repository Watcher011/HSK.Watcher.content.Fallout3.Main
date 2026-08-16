using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Watcher.Events
{
    public class IncidentWorker_HaroldInfection : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            return GetValidTargets(parms.target as Map).Any();
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = parms.target as Map;
            if (map == null) return false;

            List<Pawn> validTargets = GetValidTargets(map).ToList();

            if (validTargets.Count == 0)
                return false;

            Pawn targetPawn = validTargets.RandomElement();

            HediffDef haroldDef = HediffDef.Named("Harold");
            if (haroldDef == null)
            {
                Log.Error("[Watcher] Could not find HediffDef 'Harold'");
                return false;
            }

            Hediff hediff = HediffMaker.MakeHediff(haroldDef, targetPawn);
            hediff.Severity = 0.01f;
            targetPawn.health.AddHediff(hediff);

            if (targetPawn.Faction == Faction.OfPlayer)
            {
                Find.LetterStack.ReceiveLetter(
                    "HaroldInfectionLabel".Translate(targetPawn.Name.ToStringShort),
                    "HaroldInfectionText".Translate(targetPawn.Name.ToStringShort),
                    LetterDefOf.NegativeEvent,
                    new LookTargets(targetPawn));
            }

            return true;
        }

        private IEnumerable<Pawn> GetValidTargets(Map map)
        {
            if (map == null) yield break;

            foreach (Pawn pawn in map.mapPawns.FreeColonists)
            {
                if (pawn == null || pawn.Dead) continue;

                if (!HasActiveGene(pawn, "RADImmunity")) continue;

                if (pawn.health.hediffSet.HasHediff(HediffDef.Named("Harold"))) continue;

                yield return pawn;
            }
        }

        private bool HasActiveGene(Pawn pawn, string geneDefName)
        {
            if (pawn.genes == null) return false;

            GeneDef geneDef = DefDatabase<GeneDef>.GetNamedSilentFail(geneDefName);
            if (geneDef == null) return false;

            return pawn.genes.HasActiveGene(geneDef);
        }
    }
}