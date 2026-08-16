using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

//Этот код добавляет в игру механику паразитического заражения - при смерти заражённого колониста из его трупа вылупляются существа "Дутень".

namespace Watcher
{
    // ============ НАСТРОЙКИ МОДА ============
    public class WatcherModSettings : ModSettings
    {
        public bool enableDutenInfection = true;
        public bool enableDebugMode = false; // Отладка отключена по умолчанию
        public bool removeHediffOnDeath = true; // Удалять хеддиф после смерти
        public string infectionDefName = "WoundInfection";
        public string pawnKindToSpawn = "Duten";
        public string factionDefName = "Insect";
        public int spawnCountMin = 1;
        public int spawnCountMax = 2;
        public float baseLayer = 60f;
        public int texSeed = 1;

        public List<string> texturePaths = new List<string>();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enableDutenInfection, "enableDutenInfection", true);
            Scribe_Values.Look(ref enableDebugMode, "enableDebugMode", false);
            Scribe_Values.Look(ref removeHediffOnDeath, "removeHediffOnDeath", true);
            Scribe_Values.Look(ref infectionDefName, "infectionDefName", "WoundInfection");
            Scribe_Values.Look(ref pawnKindToSpawn, "pawnKindToSpawn", "Duten");
            Scribe_Values.Look(ref factionDefName, "factionDefName", "Insect");
            Scribe_Values.Look(ref spawnCountMin, "spawnCountMin", 1);
            Scribe_Values.Look(ref spawnCountMax, "spawnCountMax", 2);
            Scribe_Values.Look(ref baseLayer, "baseLayer", 60f);
            Scribe_Values.Look(ref texSeed, "texSeed", 1);
            Scribe_Collections.Look(ref texturePaths, "texturePaths");

            if (texturePaths == null || texturePaths.Count == 0 || texturePaths.Any(p => p.Contains("Ghoul")))
            {
                texturePaths = new List<string>
                {
                    "Things/Pawn/Attachments/growths_A"

                };
            }

            base.ExposeData();
        }
    }

    // ============ КЛАСС МОДА ============
    public class WatcherMod : Mod
    {
        public static WatcherModSettings settings;
        private string pawnKindBuffer;
        private string factionBuffer;

        public WatcherMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<WatcherModSettings>();
            pawnKindBuffer = settings.pawnKindToSpawn;
            factionBuffer = settings.factionDefName;

            if (settings.texturePaths.Any(p => p.Contains("Ghoul")))
            {
                settings.texturePaths = new List<string>
                {
                    "Things/Pawn/Attachments/growths_A"

                };
            }

            ApplySettings();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("Включить механику заражения Дутен".Translate(), ref settings.enableDutenInfection);

            if (settings.enableDutenInfection)
            {
                listing.Gap(10);

                // Новые настройки отладки и удаления хеддифа
                listing.Label("Дополнительные настройки:");
                listing.CheckboxLabeled("Включить режим отладки".Translate(), ref settings.enableDebugMode);
                listing.CheckboxLabeled("Удалять заражение после смерти".Translate(), ref settings.removeHediffOnDeath);

                listing.Gap(10);
                listing.Label("Настройки спавна:");

                listing.Label("PawnKind для спавна:");
                pawnKindBuffer = listing.TextEntry(pawnKindBuffer);
                settings.pawnKindToSpawn = pawnKindBuffer;

                listing.Gap(5);

                listing.Label("Фракция:");
                factionBuffer = listing.TextEntry(factionBuffer);
                settings.factionDefName = factionBuffer;

                listing.Gap(5);

                listing.Label($"Количество: {settings.spawnCountMin} - {settings.spawnCountMax}");
                listing.IntAdjuster(ref settings.spawnCountMin, 1, 1);
                listing.IntAdjuster(ref settings.spawnCountMax, 1, 1);

                if (settings.spawnCountMin > settings.spawnCountMax)
                    settings.spawnCountMax = settings.spawnCountMin;

                listing.Gap(10);

                if (listing.ButtonText("Сбросить пути текстур"))
                {
                    settings.texturePaths = new List<string>
                    {
                        "Things/Pawn/Attachments/growths_A",
                    };
                }
            }

            listing.End();
            ApplySettings();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory() => "Watcher Duten";

        public static void ApplySettings()
        {
            DutenSettings.EnableDutenInfection = settings.enableDutenInfection;
            DutenSettings.EnableDebugMode = settings.enableDebugMode;
            DutenSettings.RemoveHediffOnDeath = settings.removeHediffOnDeath;
            DutenSettings.InfectionDefName = settings.infectionDefName;
            DutenSettings.PawnKindToSpawn = settings.pawnKindToSpawn;
            DutenSettings.FactionDefName = settings.factionDefName;
            DutenSettings.SpawnCountMin = settings.spawnCountMin;
            DutenSettings.SpawnCountMax = settings.spawnCountMax;
            DutenSettings.BaseLayer = settings.baseLayer;
            DutenSettings.TexSeed = settings.texSeed;
            DutenSettings.TexturePaths = settings.texturePaths ?? new List<string>();
        }
    }

    // ============ СТАТИЧЕСКИЕ НАСТРОЙКИ ============
    public static class DutenSettings
    {
        public static bool EnableDutenInfection = true;
        public static bool EnableDebugMode = false; // Отладка отключена по умолчанию
        public static bool RemoveHediffOnDeath = true; // Удалять хеддиф после смерти
        public static string InfectionDefName = "WoundInfection";
        public static List<string> TexturePaths = new List<string>();
        public static string PawnKindToSpawn = "Duten";
        public static string FactionDefName = "Insect";
        public static int SpawnCountMin = 1;
        public static int SpawnCountMax = 2;
        public static float BaseLayer = 60f;
        public static int TexSeed = 1;
    }

    // ============ КОМПОНЕНТ СПАВНА ============
    public class HediffComp_DutenSpawn : HediffComp
    {
        private bool spawned = false;

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);

            if (!DutenSettings.EnableDutenInfection) return;
            if (spawned) return;
            if (parent.def.defName != DutenSettings.InfectionDefName) return;
            if (culprit != null && culprit != parent) return;

            spawned = true;
            SpawnDutenCreature();

            // Удаляем хеддиф после смерти, если включена соответствующая настройка
            if (DutenSettings.RemoveHediffOnDeath && parent.pawn?.health?.hediffSet != null)
            {
                parent.pawn.health.hediffSet.hediffs.Remove(parent);
            }
        }

        private void SpawnDutenCreature()
        {
            Pawn victim = parent.pawn;
            if (victim?.Corpse == null) return;

            Map map = victim.Corpse.Map;
            if (map == null) return;

            IntVec3 spawnCenter = victim.Corpse.Position;

            Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named(DutenSettings.FactionDefName));
            if (faction == null)
            {
                faction = FactionUtility.DefaultFactionFrom(FactionDefOf.Insect);
            }

            PawnKindDef kindDef = DefDatabase<PawnKindDef>.GetNamed(DutenSettings.PawnKindToSpawn, false);
            if (kindDef == null)
            {
                if (DutenSettings.EnableDebugMode)
                {
                    //Log.Error($"[Watcher] PawnKind {DutenSettings.PawnKindToSpawn} not found! Using Megaspider.");
                }
                kindDef = PawnKindDefOf.Megaspider;
                if (kindDef == null) return;
            }

            int count = new IntRange(DutenSettings.SpawnCountMin, DutenSettings.SpawnCountMax).RandomInRange;

            for (int i = 0; i < count; i++)
            {
                IntVec3 spawnCell = CellFinder.RandomClosewalkCellNear(spawnCenter, map, 2, (IntVec3 c) => c.Walkable(map));

                Pawn duten = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kindDef,
                    faction,
                    PawnGenerationContext.NonPlayer,
                    -1,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    mustBeCapableOfViolence: true
                ));

                GenSpawn.Spawn(duten, spawnCell, map, Rot4.Random);
                FleckMaker.Static(spawnCell, map, FleckDefOf.PsycastAreaEffect, 1.5f);
            }

            // Используем переводы из XML
            string messageKey = count > 1 ? "Watcher_Duten_SpawnMultiple" : "Watcher_Duten_SpawnSingle";
            string message = count > 1
                ? string.Format(messageKey.Translate(), victim.LabelShort, count)
                : string.Format(messageKey.Translate(), victim.LabelShort);

            Find.LetterStack.ReceiveLetter(
                "Watcher_Duten_LetterTitle".Translate(),
                string.Format("Watcher_Duten_LetterText".Translate(), victim.LabelShort),
                LetterDefOf.ThreatBig,
                new TargetInfo(spawnCenter, map)
            );
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref spawned, "spawned", false);
        }
    }

    // ============ КОМПОНЕНТ ВИЗУАЛА ============
    public class HediffComp_DutenVisual : HediffComp
    {
        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            if (!DutenSettings.EnableDutenInfection) return;

            if (parent.pawn.IsHashIntervalTick(60))
            {
                parent.pawn.Drawer.renderer.renderTree?.SetDirty();
            }
        }
    }

    // ============ HARMONY ПАТЧИ ============
    [StaticConstructorOnStartup]
    public static class WatcherHarmonyPatches
    {
        static WatcherHarmonyPatches()
        {
            Harmony harmony = new Harmony("Watcher.DutenInfection");

            PatchHediffSetAddDirect(harmony);
            PatchPawnRendererRenderPawnAt(harmony);
            PatchHediffSetRemoveHediff(harmony);
            PatchHediffDescription(harmony);
        }

        private static void PatchHediffSetAddDirect(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(HediffSet), "AddDirect", new[] { typeof(Hediff), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) });
            if (target == null)
            {
                target = AccessTools.Method(typeof(HediffSet), "AddDirect");
            }

            if (target != null)
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(Patch_HediffSet_AddDirect), "Postfix"));
                if (DutenSettings.EnableDebugMode)
                {
                    //Log.Message("[Watcher] Patched HediffSet.AddDirect");
                }
            }
            else
            {
                if (DutenSettings.EnableDebugMode)
                {
                    //Log.Warning("[Watcher] Could not find HediffSet.AddDirect");
                }
            }
        }

        private static void PatchPawnRendererRenderPawnAt(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(PawnRenderer), "RenderPawnAt", new[] { typeof(Vector3), typeof(Rot4?), typeof(bool) });
            if (target == null)
            {
                target = AccessTools.Method(typeof(PawnRenderer), "RenderPawnAt");
            }

            if (target != null)
            {
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(Patch_PawnRenderer_RenderPawnAt), "Prefix"));
                if (DutenSettings.EnableDebugMode)
                {
                    //Log.Message("[Watcher] Patched PawnRenderer.RenderPawnAt");
                }
            }
            else
            {
                if (DutenSettings.EnableDebugMode)
                {
                    //Log.Warning("[Watcher] Could not find PawnRenderer.RenderPawnAt");
                }
            }
        }

        private static void PatchHediffSetRemoveHediff(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(HediffSet), "RemoveHediff", new[] { typeof(Hediff) });

            if (target == null)
            {
                target = AccessTools.Method(typeof(HediffSet), "RemoveHediff");
            }

            if (target != null)
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(Patch_HediffSet_RemoveHediff), "Postfix"));
                if (DutenSettings.EnableDebugMode)
                {
                    //Log.Message("[Watcher] Patched HediffSet.RemoveHediff");
                }
            }
            else
            {
                if (DutenSettings.EnableDebugMode)
                {
                    //Log.Warning("[Watcher] Could not find HediffSet.RemoveHediff");
                }
            }
        }

        // Патч для замены описания заражения на переводимый текст
        private static void PatchHediffDescription(Harmony harmony)
        {
            MethodInfo target = AccessTools.PropertyGetter(typeof(Hediff), "Description");
            if (target != null)
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(Patch_Hediff_Description), "Postfix"));
                if (DutenSettings.EnableDebugMode)
                {
                    //Log.Message("[Watcher] Patched Hediff.Description");
                }
            }

            MethodInfo labelTarget = AccessTools.PropertyGetter(typeof(Hediff), "Label");
            if (labelTarget != null)
            {
                harmony.Patch(labelTarget, postfix: new HarmonyMethod(typeof(Patch_Hediff_Label), "Postfix"));
                if (DutenSettings.EnableDebugMode)
                {
                    //Log.Message("[Watcher] Patched Hediff.Label");
                }
            }
        }
    }

    // Патч: Замена описания на Fallout-стиль с переводом
    public static class Patch_Hediff_Description
    {
        public static void Postfix(Hediff __instance, ref string __result)
        {
            if (!DutenSettings.EnableDutenInfection) return;
            if (__instance?.def?.defName != DutenSettings.InfectionDefName) return;

            __result = "Watcher_DutenInfection_Description".Translate();
        }
    }

    // Патч: Замена названия с указанием стадии
    public static class Patch_Hediff_Label
    {
        public static void Postfix(Hediff __instance, ref string __result)
        {
            if (!DutenSettings.EnableDutenInfection) return;
            if (__instance?.def?.defName != DutenSettings.InfectionDefName) return;

            string stageKey = "";
            if (__instance.Severity < 0.3f)
                stageKey = "Watcher_DutenInfection_Stage_Latent";
            else if (__instance.Severity < 0.7f)
                stageKey = "Watcher_DutenInfection_Stage_Developing";
            else
                stageKey = "Watcher_DutenInfection_Stage_Critical";

            __result = "Watcher_DutenInfection_Label".Translate() + stageKey.Translate();
        }
    }

    // Патч: Добавляем компоненты к заражению
    public static class Patch_HediffSet_AddDirect
    {
        public static void Postfix(Hediff hediff, HediffSet __instance)
        {
            if (!DutenSettings.EnableDutenInfection) return;
            if (hediff?.def?.defName != DutenSettings.InfectionDefName) return;
            if (!(hediff is HediffWithComps hediffWithComps)) return;

            bool hasSpawn = false;
            bool hasVisual = false;

            foreach (var comp in hediffWithComps.comps)
            {
                if (comp is HediffComp_DutenSpawn) hasSpawn = true;
                if (comp is HediffComp_DutenVisual) hasVisual = true;
            }

            if (!hasSpawn)
            {
                HediffComp_DutenSpawn spawnComp = new HediffComp_DutenSpawn();
                spawnComp.parent = hediffWithComps;
                hediffWithComps.comps.Add(spawnComp);
            }

            if (!hasVisual)
            {
                HediffComp_DutenVisual visualComp = new HediffComp_DutenVisual();
                visualComp.parent = hediffWithComps;
                hediffWithComps.comps.Add(visualComp);
            }
        }
    }

    // Патч: Визуальная часть
    public static class Patch_PawnRenderer_RenderPawnAt
    {
        private static Pawn GetPawnFromRenderer(PawnRenderer renderer)
        {
            FieldInfo pawnField = AccessTools.Field(typeof(PawnRenderer), "pawn");
            if (pawnField != null)
            {
                return (Pawn)pawnField.GetValue(renderer);
            }

            PropertyInfo pawnProp = AccessTools.Property(typeof(PawnRenderer), "Pawn");
            if (pawnProp != null)
            {
                return (Pawn)pawnProp.GetValue(renderer);
            }

            pawnField = AccessTools.Field(typeof(PawnRenderer), "rendererPawn");
            if (pawnField != null)
            {
                return (Pawn)pawnField.GetValue(renderer);
            }

            return null;
        }

        public static void Prefix(PawnRenderer __instance, Vector3 drawLoc, Rot4? rotOverride = null, bool neverAimWeapon = false)
        {
            if (!DutenSettings.EnableDutenInfection) return;

            Pawn pawn = GetPawnFromRenderer(__instance);
            if (pawn?.health?.hediffSet == null) return;

            Hediff infection = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDef.Named(DutenSettings.InfectionDefName));
            if (infection == null) return;
            if (DutenSettings.TexturePaths.NullOrEmpty()) return;

            // Используем настройку отладки вместо Prefs.DevMode
            if (DutenSettings.EnableDebugMode && Find.TickManager.TicksGame % 250 == 0)
            {
                //Log.Message($"[Watcher] Current texture paths: {string.Join(", ", DutenSettings.TexturePaths)}");
            }

            int index = Mathf.Abs(pawn.thingIDNumber + DutenSettings.TexSeed) % DutenSettings.TexturePaths.Count;
            string texPath = DutenSettings.TexturePaths[index];

            if (!ContentFinder<Texture2D>.Get(texPath + "_north", false) &&
                !ContentFinder<Texture2D>.Get(texPath + "_south", false))
            {
                if (DutenSettings.EnableDebugMode)
                {
                    //Log.Warning($"[Watcher] Texture not found: {texPath}");
                }
                return;
            }

            Graphic graphic = GraphicDatabase.Get<Graphic_Multi>(texPath, ShaderDatabase.Cutout, Vector2.one, Color.white);
            if (graphic == null) return;

            Rot4 rot = rotOverride ?? pawn.Rotation;
            Vector3 pos = drawLoc;
            pos.y = AltitudeLayer.Pawn.AltitudeFor() + 0.01f;

            graphic.Draw(pos, rot, pawn);
        }
    }

    // Патч: Очистка при удалении заражения
    public static class Patch_HediffSet_RemoveHediff
    {
        public static void Postfix(Hediff hediff, HediffSet __instance)
        {
            if (hediff?.def?.defName != DutenSettings.InfectionDefName) return;

            Pawn pawn = __instance.pawn;
            if (pawn != null)
            {
                pawn.Drawer.renderer.renderTree?.SetDirty();
            }
        }
    }
}