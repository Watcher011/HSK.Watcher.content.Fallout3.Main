//using System;
//using System.Collections;
//using System.Collections.Generic;
//using System.Linq;
//using System.Reflection;
//using System.Text;
//using HarmonyLib;
//using RimWorld;
//using UnityEngine;
//using Verse;

//namespace Watcher.Comps
//{
//    public class CompProperties_CompAnalyzableUnlockResearch : CompProperties_Analyzable
//    {
//        // Сам анализ засчитывается по analysisID (задаётся в XML, уникален для каждого голодиска).
//        public int analysisID;
//        public List<string> researchDefNames;
//        public bool requiresMechanitor;
//        public string inspectStringKey = "Watcher_AnalyzeInspect";
//        public string requiresMechanitorKey = "Watcher_RequiresMechanitor";

//        public CompProperties_CompAnalyzableUnlockResearch()
//        {
//            compClass = typeof(CompAnalyzableUnlockResearch);
//        }

//        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
//        {
//            foreach (string error in base.ConfigErrors(parentDef))
//                yield return error;

//            if (analysisID == 0)
//                yield return $"[Watcher.Comps] {parentDef.defName}: analysisID не задан (0). " +
//                             "Задайте уникальный analysisID в XML.";
//        }
//    }

//    /// <summary>
//    /// Ванильные ResearchProjectDef.AnalyzedThingsCompleted / AnalyzedThingsRequirementsMet
//    /// не видят анализ, записанный нашим компом (в логе: AnalysisManager Satisfied=True,
//    /// а AnalyzedThingsCompleted=0). Поправляем результат этих геттеров.
//    /// </summary>
//    [StaticConstructorOnStartup]
//    public static class AnalyzedRequirementsFix
//    {
//        static AnalyzedRequirementsFix()
//        {
//            try
//            {
//                var harmony = new Harmony("hsk.watcher.comps.analyzedfix");
//                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

//                MethodBase completed = typeof(ResearchProjectDef).GetProperty("AnalyzedThingsCompleted", flags)?.GetGetMethod(true);
//                MethodBase met = typeof(ResearchProjectDef).GetProperty("AnalyzedThingsRequirementsMet", flags)?.GetGetMethod(true);

//                if (completed != null)
//                    harmony.Patch(completed, postfix: new HarmonyMethod(typeof(AnalyzedRequirementsFix), nameof(CompletedPostfix)));
//                else
//                    Log.Warning("[Watcher.Comps] ResearchProjectDef.AnalyzedThingsCompleted не найден, патч не применён");

//                if (met != null)
//                    harmony.Patch(met, postfix: new HarmonyMethod(typeof(AnalyzedRequirementsFix), nameof(MetPostfix)));
//                else
//                    Log.Warning("[Watcher.Comps] ResearchProjectDef.AnalyzedThingsRequirementsMet не найден, патч не применён");

//                // Отладка: полный стек NRE при завершении проекта (в обход дедупликации "Duplicate stacktrace").
//                MethodBase finish = typeof(ResearchManager).GetMethod("FinishProject", flags);
//                if (finish != null)
//                    harmony.Patch(finish,
//                        prefix: new HarmonyMethod(typeof(AnalyzedRequirementsFix), nameof(FinishProjectPrefix)),
//                        finalizer: new HarmonyMethod(typeof(AnalyzedRequirementsFix), nameof(FinishProjectFinalizer)));
//                else
//                    Log.Warning("[Watcher.Comps] ResearchManager.FinishProject не найден, отладочный патч не применён");

//                Log.Message("[Watcher.Comps] AnalyzedRequirementsFix применён");
//            }
//            catch (Exception ex)
//            {
//                Log.Error("[Watcher.Comps] AnalyzedRequirementsFix failed: " + ex);
//            }
//        }

//        private static readonly HashSet<string> dumpedFinish = new HashSet<string>();

//        // Отладка NRE: сравниваем поля завершаемого проекта с эталонным (Electricity).
//        // Поля, которые у нашего проекта null, а у эталона нет, - главные подозреваемые.
//        public static void FinishProjectPrefix(ResearchProjectDef proj, bool doCompletionDialog, Pawn researcher, bool doCompletionLetter)
//        {
//            if (!CompAnalyzableUnlockResearch.DebugEnabled || proj == null || !dumpedFinish.Add(proj.defName))
//                return;

//            try
//            {
//                ResearchProjectDef reference = DefDatabase<ResearchProjectDef>.GetNamedSilentFail("Electricity")
//                    ?? DefDatabase<ResearchProjectDef>.AllDefs.FirstOrDefault(d => d != proj && d.IsFinished);

//                var sb = new StringBuilder();
//                sb.AppendLine($"[Watcher.Comps][DEBUG] FinishProject({proj.defName}) args: doCompletionDialog={doCompletionDialog}, " +
//                              $"researcher={(researcher == null ? "null" : researcher.LabelShort)}, doCompletionLetter={doCompletionLetter}");
//                sb.AppendLine($"  Find.Storyteller={(Find.Storyteller == null ? "null" : "ok")}, Find.Storyteller.def={(Find.Storyteller?.def == null ? "null" : "ok")}, " +
//                              $"Find.ResearchManager={(Find.ResearchManager == null ? "null" : "ok")}");
//                sb.AppendLine($"  reference project: {reference?.defName ?? "none"}");

//                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
//                var suspects = new List<string>();
//                var allNull = new List<string>();

//                for (Type t = typeof(ResearchProjectDef); t != null && t != typeof(object); t = t.BaseType)
//                {
//                    foreach (FieldInfo f in t.GetFields(flags))
//                    {
//                        object mine = f.GetValue(proj);
//                        if (mine != null)
//                            continue;

//                        allNull.Add(f.Name);
//                        object theirs = reference != null ? f.GetValue(reference) : null;
//                        if (theirs != null)
//                            suspects.Add($"{f.Name} (у эталона: {(theirs is Def d ? d.defName : theirs.GetType().Name)})");
//                    }
//                }

//                sb.AppendLine($"  поля null у проекта, но НЕ null у эталона: {(suspects.Count == 0 ? "(нет)" : string.Join("; ", suspects))}");
//                sb.AppendLine($"  все null-поля проекта: {string.Join(", ", allNull)}");
//                Log.Warning(sb.ToString());
//            }
//            catch (Exception ex)
//            {
//                Log.Warning("[Watcher.Comps][DEBUG] FinishProjectPrefix failed: " + ex);
//            }
//        }

//        private static int finishErrorsLogged;

//        // Возвращаем исключение без изменений (поведение игры не меняется), только логируем полный текст.
//        public static Exception FinishProjectFinalizer(ResearchProjectDef proj, Exception __exception)
//        {
//            if (__exception != null && CompAnalyzableUnlockResearch.DebugEnabled && finishErrorsLogged < 3)
//            {
//                finishErrorsLogged++;
//                Log.Warning($"[Watcher.Comps][DEBUG] FinishProject({proj?.defName}) threw:\n{__exception}");
//            }
//            return __exception;
//        }

//        private static CompProperties_CompAnalyzableUnlockResearch GetOurProps(ThingDef def) =>
//            def?.comps?.OfType<CompProperties_CompAnalyzableUnlockResearch>().FirstOrDefault();

//        private static bool IsSatisfied(int analysisID) =>
//            Find.AnalysisManager != null
//            && Find.AnalysisManager.TryGetAnalysisProgress(analysisID, out var details)
//            && details.Satisfied;

//        public static void CompletedPostfix(ResearchProjectDef __instance, ref int __result)
//        {
//            if (Current.ProgramState != ProgramState.Playing || __instance.requiredAnalyzed == null)
//                return;

//            int ours = 0;
//            foreach (ThingDef t in __instance.requiredAnalyzed)
//            {
//                var cp = GetOurProps(t);
//                if (cp != null && IsSatisfied(cp.analysisID))
//                    ours++;
//            }

//            if (ours > 0)
//                __result = Math.Min(__instance.requiredAnalyzed.Count, __result + ours);
//        }

//        public static void MetPostfix(ResearchProjectDef __instance, ref bool __result)
//        {
//            if (Current.ProgramState != ProgramState.Playing || __instance.requiredAnalyzed.NullOrEmpty())
//                return;

//            bool anyOurs = false;
//            bool allOurs = true;
//            foreach (ThingDef t in __instance.requiredAnalyzed)
//            {
//                var cp = GetOurProps(t);
//                if (cp == null)
//                {
//                    allOurs = false;
//                    continue;
//                }

//                anyOurs = true;
//                if (!IsSatisfied(cp.analysisID))
//                {
//                    __result = false;
//                    return;
//                }
//            }

//            // Все наши голодиски изучены; если остальных требований нет - условие выполнено.
//            if (anyOurs && allOurs)
//                __result = true;
//        }
//    }

//    public class CompAnalyzableUnlockResearch : CompAnalyzable
//    {
//        /// <summary>Выключите (false), когда отладка больше не нужна.</summary>
//        public static bool DebugEnabled = true;

//        private const string Tag = "[Watcher.Comps][DEBUG] ";
//        private static readonly HashSet<string> loggedSpawn = new HashSet<string>();

//        private List<ResearchProjectDef> researchUnlocked;

//        public new CompProperties_CompAnalyzableUnlockResearch Props =>
//            (CompProperties_CompAnalyzableUnlockResearch)props;

//        public List<ResearchProjectDef> ResearchUnlocked
//        {
//            get
//            {
//                if (researchUnlocked == null)
//                {
//                    researchUnlocked = new List<ResearchProjectDef>();

//                    if (Props.researchDefNames.NullOrEmpty())
//                        return researchUnlocked;

//                    foreach (string defName in Props.researchDefNames)
//                    {
//                        ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);

//                        if (research != null)
//                            researchUnlocked.Add(research);
//                        else
//                            Log.Warning($"[Watcher.Comps] Research '{defName}' not found");
//                    }
//                }
//                return researchUnlocked;
//            }
//        }

//        // Стабильный ID из XML (раньше был хэш от researchDefNames).
//        public override int AnalysisID => Props.analysisID;

//        public override NamedArgument? ExtraNamedArg =>
//            ResearchUnlocked.Select(r => r.label).ToCommaList(useAnd: true).Named("RESEARCH");

//        private bool IsAlreadyAnalyzed =>
//            Find.AnalysisManager.TryGetAnalysisProgress(AnalysisID, out var details) && details.Satisfied;

//        // ------------------------------------------------------------------
//        // Жизненный цикл
//        // ------------------------------------------------------------------

//        public override void PostSpawnSetup(bool respawningAfterLoad)
//        {
//            base.PostSpawnSetup(respawningAfterLoad);

//            if (!DebugEnabled)
//                return;

//            // Один раз на каждый def, чтобы не засорять лог.
//            if (loggedSpawn.Add(parent.def.defName))
//                Log.Message($"{Tag}spawn: thing={parent.def.defName}, analysisID={AnalysisID}, " +
//                            $"researchDefNames=[{string.Join(", ", Props.researchDefNames ?? new List<string>())}]");
//        }

//        public override IEnumerable<Gizmo> CompGetGizmosExtra()
//        {
//            foreach (Gizmo g in base.CompGetGizmosExtra())
//                yield return g;

//            if (DebugEnabled && Prefs.DevMode)
//            {
//                yield return new Command_Action
//                {
//                    defaultLabel = "DEV: Watcher dump",
//                    defaultDesc = "Выводит в лог состояние анализа и связанных исследований.",
//                    action = () => DebugDump("gizmo")
//                };
//            }
//        }

//        public override AcceptanceReport CanInteract(Pawn activateBy = null, bool checkOptionalItems = true)
//        {
//            AcceptanceReport result = base.CanInteract(activateBy, checkOptionalItems);
//            if (!result.Accepted)
//                return result;

//            if (IsAlreadyAnalyzed)
//                return "Watcher_AlreadyAnalyzed".Translate();

//            if (activateBy != null && Props.requiresMechanitor && !MechanitorUtility.IsMechanitor(activateBy))
//            {
//                if (!string.IsNullOrEmpty(Props.requiresMechanitorKey))
//                    return Props.requiresMechanitorKey.Translate();
//                else
//                    return "RequiresMechanitor".Translate();
//            }

//            return true;
//        }

//        public override void OnAnalyzed(Pawn pawn)
//        {
//            if (DebugEnabled)
//                DebugDump("OnAnalyzed BEFORE base");

//            base.OnAnalyzed(pawn);

//            if (DebugEnabled)
//                DebugDump("OnAnalyzed AFTER base");

//            // requiredAnalyzed не трогаем: проверка должна видеть завершённый анализ по ID.
//            if (IsAlreadyAnalyzed)
//                SendCompletionLetter(pawn);
//        }

//        // ------------------------------------------------------------------
//        // Отладка
//        // ------------------------------------------------------------------

//        private void DebugDump(string reason)
//        {
//            try
//            {
//                var sb = new StringBuilder();
//                sb.AppendLine($"{Tag}===== {reason} =====");
//                sb.AppendLine($"thing: {parent.def.defName} (id {parent.ThingID})");
//                sb.AppendLine($"AnalysisID (comp): {AnalysisID}");
//                sb.AppendLine($"Props.analysisID (xml): {Props.analysisID}");
//                sb.AppendLine($"researchDefNames: [{string.Join(", ", Props.researchDefNames ?? new List<string>())}]");

//                // Прогресс в AnalysisManager
//                bool has = Find.AnalysisManager.TryGetAnalysisProgress(AnalysisID, out var details);
//                sb.AppendLine($"AnalysisManager.TryGetAnalysisProgress({AnalysisID}) = {has}");
//                if (has)
//                    sb.AppendLine($"  details: {Dump(details)}");
//                sb.AppendLine($"  IsAlreadyAnalyzed (Satisfied): {IsAlreadyAnalyzed}");
//                sb.AppendLine($"AnalysisManager dump: {Dump(Find.AnalysisManager)}");

//                // Исследования, которые должен открывать этот комп
//                foreach (string defName in Props.researchDefNames ?? new List<string>())
//                {
//                    ResearchProjectDef r = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);
//                    if (r == null)
//                    {
//                        sb.AppendLine($"research '{defName}': NOT FOUND in DefDatabase");
//                        continue;
//                    }

//                    sb.AppendLine($"research '{r.defName}' ({r.label}): IsFinished={r.IsFinished}, " +
//                                  $"CanStartNow={r.CanStartNow}, ProgressReal={r.ProgressReal}");

//                    var req = r.requiredAnalyzed;
//                    sb.AppendLine($"  requiredAnalyzed: {(req == null ? "null" : "[" + string.Join(", ", req.Select(t => t?.defName ?? "null")) + "]")}");

//                    if (req != null)
//                    {
//                        bool listed = req.Any(t => t == parent.def);
//                        sb.AppendLine($"  requiredAnalyzed содержит {parent.def.defName}: {listed}");
//                        if (!listed)
//                            sb.AppendLine($"  !!! {parent.def.defName} НЕ указан в requiredAnalyzed этого исследования");

//                        foreach (ThingDef t in req.Where(t => t != null))
//                        {
//                            var cp = t.comps?.OfType<CompProperties_CompAnalyzableUnlockResearch>().FirstOrDefault();
//                            sb.AppendLine($"  - {t.defName}: comp={(cp == null ? "НЕТ нашего компа" : "analysisID=" + cp.analysisID)}");
//                            if (cp != null && cp.analysisID != AnalysisID)
//                                sb.AppendLine($"    (отличается от ID этого предмета: {AnalysisID})");
//                        }
//                    }

//                    // Все параметрless-члены ResearchProjectDef с "Analyz" в имени:
//                    // так видно, что именно считает ванильная проверка.
//                    sb.AppendLine($"  vanilla analyzed-members: {DumpAnalyzedMembers(r)}");
//                }

//                // Коллизии analysisID среди всех дефов с нашим компом
//                var withComp = DefDatabase<ThingDef>.AllDefs
//                    .Select(d => new { d, cp = d.comps?.OfType<CompProperties_CompAnalyzableUnlockResearch>().FirstOrDefault() })
//                    .Where(x => x.cp != null)
//                    .ToList();
//                sb.AppendLine($"Всего дефов с нашим компом: {withComp.Count}");
//                foreach (var grp in withComp.GroupBy(x => x.cp.analysisID))
//                {
//                    string names = string.Join(", ", grp.Select(x => x.d.defName));
//                    sb.AppendLine($"  analysisID {grp.Key}: {names}" + (grp.Count() > 1 ? "   !!! ДУБЛИКАТ" : ""));
//                }

//                Log.Message(sb.ToString());
//            }
//            catch (Exception ex)
//            {
//                Log.Error($"{Tag}DebugDump failed: {ex}");
//            }
//        }

//        private static string DumpAnalyzedMembers(ResearchProjectDef r)
//        {
//            var sb = new StringBuilder();
//            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

//            foreach (PropertyInfo p in r.GetType().GetProperties(flags))
//            {
//                if (p.Name.IndexOf("Analyz", StringComparison.OrdinalIgnoreCase) < 0 || p.GetIndexParameters().Length > 0)
//                    continue;
//                try { sb.Append($"{p.Name}={Fmt(p.GetValue(r))}; "); }
//                catch (Exception e) { sb.Append($"{p.Name}=<ex {e.GetType().Name}>; "); }
//            }

//            foreach (MethodInfo m in r.GetType().GetMethods(flags))
//            {
//                if (m.Name.IndexOf("Analyz", StringComparison.OrdinalIgnoreCase) < 0
//                    || m.GetParameters().Length > 0 || m.ReturnType == typeof(void)
//                    || m.IsSpecialName || m.IsGenericMethod)
//                    continue;
//                try { sb.Append($"{m.Name}()={Fmt(m.Invoke(r, null))}; "); }
//                catch (Exception e) { sb.Append($"{m.Name}()=<ex {e.GetType().Name}>; "); }
//            }

//            foreach (FieldInfo f in r.GetType().GetFields(flags))
//            {
//                if (f.Name.IndexOf("Analyz", StringComparison.OrdinalIgnoreCase) < 0)
//                    continue;
//                try { sb.Append($"field {f.Name}={Fmt(f.GetValue(r))}; "); }
//                catch (Exception e) { sb.Append($"field {f.Name}=<ex {e.GetType().Name}>; "); }
//            }

//            return sb.Length == 0 ? "(ничего не найдено)" : sb.ToString();
//        }

//        private static string Dump(object obj)
//        {
//            if (obj == null)
//                return "null";

//            var sb = new StringBuilder();
//            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
//            foreach (FieldInfo f in obj.GetType().GetFields(flags))
//            {
//                try { sb.Append($"{f.Name}={Fmt(f.GetValue(obj))}; "); }
//                catch (Exception e) { sb.Append($"{f.Name}=<ex {e.GetType().Name}>; "); }
//            }
//            return sb.ToString();
//        }

//        private static string Fmt(object value)
//        {
//            if (value == null)
//                return "null";

//            if (value is string s)
//                return s;

//            if (value is IDictionary dict)
//            {
//                var parts = new List<string>();
//                foreach (DictionaryEntry e in dict)
//                    parts.Add($"{e.Key}:{FmtShallow(e.Value)}");
//                return "{" + string.Join(", ", parts) + "}";
//            }

//            if (value is IEnumerable en)
//            {
//                var parts = new List<string>();
//                foreach (object o in en)
//                    parts.Add(FmtShallow(o));
//                return "[" + string.Join(", ", parts) + "]";
//            }

//            return FmtShallow(value);
//        }

//        private static string FmtShallow(object value)
//        {
//            if (value == null)
//                return "null";
//            if (value is Def def)
//                return def.defName;
//            if (value.GetType().IsPrimitive || value is string || value is Enum)
//                return value.ToString();

//            // Небольшие объекты (например, AnalysisDetails) раскрываем по полям.
//            var sb = new StringBuilder("(");
//            foreach (FieldInfo f in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
//            {
//                try
//                {
//                    object v = f.GetValue(value);
//                    sb.Append($"{f.Name}={(v is Def d ? d.defName : v?.ToString() ?? "null")} ");
//                }
//                catch { }
//            }
//            sb.Append(")");
//            return sb.ToString();
//        }

//        // ------------------------------------------------------------------
//        // Письмо и текст
//        // ------------------------------------------------------------------

//        private void SendCompletionLetter(Pawn pawn)
//        {
//            if (ResearchUnlocked.NullOrEmpty())
//                return;

//            string researchNames = ResearchUnlocked
//                .Where(r => r != null)
//                .Select(r => r.label)
//                .ToCommaList(useAnd: true);

//            string label = Props.completedLetterLabel;
//            string text = Props.completedLetter;

//            if (!string.IsNullOrEmpty(label) && label.StartsWith("Watcher_"))
//                label = label.Translate();
//            else if (string.IsNullOrEmpty(label))
//                label = "Watcher_AnalyzeComplete".Translate();

//            if (!string.IsNullOrEmpty(text) && text.StartsWith("Watcher_"))
//                text = text.Translate();
//            else if (string.IsNullOrEmpty(text))
//                text = "Watcher_AnalyzeCompleteDesc".Translate();

//            label = label.Replace("{RESEARCH}", researchNames);
//            text = text.Replace("{RESEARCH}", researchNames);
//            text = text.Replace("{PAWN_labelShort}", pawn?.LabelShort ?? "Someone");

//            Letter letter = LetterMaker.MakeLetter(
//                label,
//                text,
//                Props.completedLetterDef ?? LetterDefOf.PositiveEvent
//            );
//            Find.LetterStack.ReceiveLetter(letter);
//        }

//        public override string CompInspectStringExtra()
//        {
//            string baseText = base.CompInspectStringExtra();

//            if (IsAlreadyAnalyzed)
//            {
//                string done = "Watcher_AlreadyAnalyzed".Translate();
//                return string.IsNullOrEmpty(baseText) ? done : (baseText + "\n" + done);
//            }

//            if (!ResearchUnlocked.NullOrEmpty())
//            {
//                string researchNames = ResearchUnlocked
//                    .Where(r => r != null)
//                    .Select(r => r.label)
//                    .ToCommaList(useAnd: true);

//                string result;
//                if (!string.IsNullOrEmpty(Props.inspectStringKey))
//                    result = Props.inspectStringKey.Translate(researchNames);
//                else
//                    result = "Can be analyzed by a colonist. Unlocks: " + researchNames;

//                return string.IsNullOrEmpty(baseText) ? result : (baseText + "\n" + result);
//            }

//            return baseText;
//        }
//    }
//}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace HSK.Watcher.Content
{
    /// <summary>
    /// Подменяет текстуру у чертежей (техпринтов).
    /// Вариант выбирается по хэшу defName: один и тот же чертёж всегда получает одну текстуру.
    /// Работает в два этапа:
    ///  1) Harmony-постфикс на ThingDefGenerator_Techprints.ImpliedTechprintDefs (если патч применился вовремя);
    ///  2) TechprintTextureFixup при старте: проходит по ВСЕМ ThingDef с CompProperties_Techprint
    ///     (и сгенерированным, и описанным в XML), правит texPath и сбрасывает кэш графики.
    /// </summary>
    [HarmonyPatch(typeof(ThingDefGenerator_Techprints), nameof(ThingDefGenerator_Techprints.ImpliedTechprintDefs))]
    public static class Patch_TechprintTextures
    {
        // Файлы лежат в Textures/Things/Item/Special/ (расширение .png в пути не указывается).
        public static readonly string[] TexPaths =
        {
            "Things/Item/Special/TechprintUltratechA",
            "Things/Item/Special/TechprintUltratechB",
            "Things/Item/Special/TechprintUltratechC",
            "Things/Item/Special/TechprintUltratechD",
            "Things/Item/Special/TechprintUltratechE",
            "Things/Item/Special/TechprintUltratechF"
        };

        public static string PathFor(string defName)
        {
            int index = (GenText.StableStringHash(defName) & 0x7FFFFFFF) % TexPaths.Length;
            return TexPaths[index];
        }

        // ImpliedTechprintDefs - итератор, поэтому оборачиваем результат.
        public static IEnumerable<ThingDef> Postfix(IEnumerable<ThingDef> __result)
        {
            int count = 0;
            foreach (ThingDef def in __result)
            {
                if (def != null && def.graphicData != null && !string.IsNullOrEmpty(def.defName))
                {
                    def.graphicData.texPath = PathFor(def.defName);
                    count++;
                }

                yield return def;
            }

            Log.Message("[Watcher] Techprint textures: Harmony-постфикс обработал чертежей: " + count);
        }
    }

    [StaticConstructorOnStartup]
    public static class TechprintTextureFixup
    {
        static TechprintTextureFixup()
        {
            try
            {
                int total = 0;
                int changed = 0;
                var sample = new List<string>();

                foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    if (def.comps == null || def.graphicData == null)
                        continue;
                    if (!def.comps.Any(c => c is CompProperties_Techprint))
                        continue;

                    total++;
                    string wanted = Patch_TechprintTextures.PathFor(def.defName);

                    if (sample.Count < 5)
                        sample.Add(def.defName + " -> " + def.graphicData.texPath);

                    if (def.graphicData.texPath == wanted)
                        continue;

                    def.graphicData.texPath = wanted;
                    ResetGraphic(def);
                    changed++;
                }

                Log.Message("[Watcher] Techprint textures: найдено чертежей " + total + ", исправлено при старте " + changed +
                            ". Примеры: " + string.Join("; ", sample.ToArray()));
            }
            catch (Exception ex)
            {
                Log.Error("[Watcher] TechprintTextureFixup failed: " + ex);
            }
        }

        // Сбрасывает закэшированную графику, чтобы новая текстура реально подтянулась.
        private static void ResetGraphic(ThingDef def)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (FieldInfo fi in typeof(GraphicData).GetFields(flags))
            {
                if (typeof(Graphic).IsAssignableFrom(fi.FieldType))
                    fi.SetValue(def.graphicData, null);
            }

            Graphic fresh = def.graphicData.Graphic;

            for (Type t = def.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo fi in t.GetFields(flags | BindingFlags.DeclaredOnly))
                {
                    if (fi.FieldType == typeof(Graphic))
                        fi.SetValue(def, fresh);
                }
            }

            MethodInfo resolveIcon = typeof(ThingDef).GetMethod("ResolveIcon", flags);
            if (resolveIcon != null && resolveIcon.GetParameters().Length == 0)
                resolveIcon.Invoke(def, null);
        }
    }
}