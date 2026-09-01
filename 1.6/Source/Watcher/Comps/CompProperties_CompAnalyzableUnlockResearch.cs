using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Watcher.Comps
{
    public class CompProperties_CompAnalyzableUnlockResearch : CompProperties_Analyzable
    {
        public List<string> researchDefNames;
        public bool requiresMechanitor;
        public string inspectStringKey = "Watcher_AnalyzeInspect";
        public string requiresMechanitorKey = "Watcher_RequiresMechanitor";

        public CompProperties_CompAnalyzableUnlockResearch()
        {
            compClass = typeof(CompAnalyzableUnlockResearch);
        }
    }

    public class CompAnalyzableUnlockResearch : CompAnalyzable
    {
        private List<ResearchProjectDef> researchUnlocked;
        private int cachedAnalysisID = -1;

        public new CompProperties_CompAnalyzableUnlockResearch Props =>
            (CompProperties_CompAnalyzableUnlockResearch)props;

        public List<ResearchProjectDef> ResearchUnlocked
        {
            get
            {
                if (researchUnlocked == null)
                {
                    researchUnlocked = new List<ResearchProjectDef>();

                    if (Props.researchDefNames.NullOrEmpty())
                        return researchUnlocked;

                    foreach (string defName in Props.researchDefNames)
                    {
                        ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);

                        if (research != null)
                        {
                            researchUnlocked.Add(research);
                        }
                        else
                        {
                            Log.Warning($"[Watcher.Comps] Research '{defName}' not found");
                        }
                    }
                }
                return researchUnlocked;
            }
        }

        public override int AnalysisID
        {
            get
            {
                if (cachedAnalysisID == -1)
                {
                    if (Props.researchDefNames.NullOrEmpty())
                    {
                        cachedAnalysisID = 0;
                    }
                    else
                    {
                        string combined = string.Join("|", Props.researchDefNames);
                        cachedAnalysisID = GenText.StableStringHash(combined);
                    }
                }
                return cachedAnalysisID;
            }
        }

        public override NamedArgument? ExtraNamedArg =>
            ResearchUnlocked.Select(r => r.label).ToCommaList(useAnd: true).Named("RESEARCH");

        public override AcceptanceReport CanInteract(Pawn activateBy = null, bool checkOptionalItems = true)
        {
            AcceptanceReport result = base.CanInteract(activateBy, checkOptionalItems);
            if (!result.Accepted)
                return result;

            if (Find.AnalysisManager.TryGetAnalysisProgress(AnalysisID, out var details) && details.Satisfied)
                return "Watcher_AlreadyAnalyzed".Translate();

            if (activateBy != null && Props.requiresMechanitor && !MechanitorUtility.IsMechanitor(activateBy))
            {
                if (!string.IsNullOrEmpty(Props.requiresMechanitorKey))
                    return Props.requiresMechanitorKey.Translate();
                else
                    return "RequiresMechanitor".Translate();
            }

            return true;
        }

        public override void OnAnalyzed(Pawn pawn)
        {
            base.OnAnalyzed(pawn);

            if (Find.AnalysisManager.TryGetAnalysisProgress(AnalysisID, out var details) && details.Satisfied)
            {
                foreach (ResearchProjectDef research in ResearchUnlocked)
                {
                    if (research != null && !research.IsFinished)
                    {
                        research.requiredAnalyzed = null;
                    }
                }

                // Отправляем письмо вручную, так как base.OnAnalyzed может не работать с локализацией
                SendCompletionLetter(pawn);
            }
        }

        private void SendCompletionLetter(Pawn pawn)
        {
            if (ResearchUnlocked.NullOrEmpty())
                return;

            string researchNames = ResearchUnlocked
                .Where(r => r != null)
                .Select(r => r.label)
                .ToCommaList(useAnd: true);

            // Получаем текст письма
            string label = Props.completedLetterLabel;
            string text = Props.completedLetter;

            // Если это ключи локализации - переводим
            if (!string.IsNullOrEmpty(label) && label.StartsWith("Watcher_"))
                label = label.Translate();
            else if (string.IsNullOrEmpty(label))
                label = "Watcher_AnalyzeComplete".Translate();

            if (!string.IsNullOrEmpty(text) && text.StartsWith("Watcher_"))
                text = text.Translate();
            else if (string.IsNullOrEmpty(text))
                text = "Watcher_AnalyzeCompleteDesc".Translate();

            // Подставляем значения
            label = label.Replace("{RESEARCH}", researchNames);
            text = text.Replace("{RESEARCH}", researchNames);
            text = text.Replace("{PAWN_labelShort}", pawn?.LabelShort ?? "Someone");

            // Отправляем письмо
            Letter letter = LetterMaker.MakeLetter(
                label,
                text,
                Props.completedLetterDef ?? LetterDefOf.PositiveEvent
            );
            Find.LetterStack.ReceiveLetter(letter);
        }

        public override string CompInspectStringExtra()
        {
            string baseText = base.CompInspectStringExtra();

            if (Find.AnalysisManager.TryGetAnalysisProgress(AnalysisID, out var details) && details.Satisfied)
            {
                string result = "Watcher_AlreadyAnalyzed".Translate();
                return string.IsNullOrEmpty(baseText) ? result : (baseText + "\n" + result);
            }

            if (!ResearchUnlocked.NullOrEmpty())
            {
                string researchNames = ResearchUnlocked
                    .Where(r => r != null)
                    .Select(r => r.label)
                    .ToCommaList(useAnd: true);

                string result;
                if (!string.IsNullOrEmpty(Props.inspectStringKey))
                    result = Props.inspectStringKey.Translate(researchNames);
                else
                    result = "Can be analyzed by a colonist. Unlocks: " + researchNames;

                return string.IsNullOrEmpty(baseText) ? result : (baseText + "\n" + result);
            }

            return baseText;
        }
    }
}