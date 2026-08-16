using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

//Этот код добавляет в игру абсурдное катастрофическое событие -
//    массовое загрязнение карты фекалиями с одновременным нашествием фекальных существ и изменением погоды.

namespace Watcher.Events
{
    [StaticConstructorOnStartup]
    public static class PoopocalypseModInitializer
    {
        static PoopocalypseModInitializer()
        {
            try
            {
                var harmony = new Harmony("watcher.poopocalypse");
                PatchWeatherManager(harmony);
                PatchSkyManager(harmony);
                //Log.Message("[Poopocalypse] Mod loaded!");
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Failed to load Harmony: {ex}");
            }
        }

        private static void PatchWeatherManager(Harmony harmony)
        {
            try
            {
                Type weatherManagerType = typeof(WeatherManager);

                // Patch CurWeatherLabel getter
                var curWeatherLabelGetter = weatherManagerType.GetProperty("CurWeatherLabel", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
                if (curWeatherLabelGetter != null)
                {
                    harmony.Patch(curWeatherLabelGetter,
                        postfix: new HarmonyMethod(typeof(PoopocalypsePatches), nameof(PoopocalypsePatches.CurWeatherLabel_Postfix)));
                    //Log.Message("[Poopocalypse] Patched CurWeatherLabel");
                }

                // Patch CurWeatherDescription getter
                var curWeatherDescriptionGetter = weatherManagerType.GetProperty("CurWeatherDescription", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
                if (curWeatherDescriptionGetter != null)
                {
                    harmony.Patch(curWeatherDescriptionGetter,
                        postfix: new HarmonyMethod(typeof(PoopocalypsePatches), nameof(PoopocalypsePatches.CurWeatherDescription_Postfix)));
                    //Log.Message("[Poopocalypse] Patched CurWeatherDescription");
                }

                // Patch TransitionTo
                var transitionTo = weatherManagerType.GetMethod("TransitionTo", BindingFlags.Public | BindingFlags.Instance);
                if (transitionTo != null)
                {
                    harmony.Patch(transitionTo,
                        prefix: new HarmonyMethod(typeof(PoopocalypsePatches), nameof(PoopocalypsePatches.TransitionTo_Prefix)));
                    //Log.Message("[Poopocalypse] Patched TransitionTo");
                }
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Failed to patch WeatherManager: {ex}");
            }
        }

        private static void PatchSkyManager(Harmony harmony)
        {
            try
            {
                Type skyManagerType = typeof(SkyManager);

                // Patch GetSkyColor
                var getSkyColor = skyManagerType.GetMethod("GetSkyColor", BindingFlags.Public | BindingFlags.Instance);
                if (getSkyColor != null)
                {
                    harmony.Patch(getSkyColor,
                        postfix: new HarmonyMethod(typeof(PoopocalypsePatches), nameof(PoopocalypsePatches.GetSkyColor_Postfix)));
                    //Log.Message("[Poopocalypse] Patched GetSkyColor");
                }

                // Patch GetOverlayColor
                var getOverlayColor = skyManagerType.GetMethod("GetOverlayColor", BindingFlags.Public | BindingFlags.Instance);
                if (getOverlayColor != null)
                {
                    harmony.Patch(getOverlayColor,
                        postfix: new HarmonyMethod(typeof(PoopocalypsePatches), nameof(PoopocalypsePatches.GetOverlayColor_Postfix)));
                    //Log.Message("[Poopocalypse] Patched GetOverlayColor");
                }
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Failed to patch SkyManager: {ex}");
            }
        }
    }

    // All patches in one static class - NO HarmonyPatch attributes!
    public static class PoopocalypsePatches
    {
        public static void CurWeatherLabel_Postfix(WeatherManager __instance, ref string __result)
        {
            try
            {
                if (__instance?.map == null) return;
                PoopocalypseComponent comp = __instance.map.GetComponent<PoopocalypseComponent>();
                if (comp != null && comp.IsActive)
                {
                    int remainingTicks = comp.RemainingTicks;
                    float progress = (float)remainingTicks / 60000f;
                    if (progress > 0.66f)
                        __result = "poopocalypse_heavy".Translate();
                    else if (progress > 0.33f)
                        __result = "poopocalypse_medium".Translate();
                    else
                        __result = "poopocalypse_light".Translate();
                }
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Error in CurWeatherLabel patch: {ex}");
            }
        }

        public static void CurWeatherDescription_Postfix(WeatherManager __instance, ref string __result)
        {
            try
            {
                if (__instance?.map == null) return;
                PoopocalypseComponent comp = __instance.map.GetComponent<PoopocalypseComponent>();
                if (comp != null && comp.IsActive)
                {
                    __result = "poopocalypse_description".Translate();
                }
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Error in CurWeatherDescription patch: {ex}");
            }
        }

        public static bool TransitionTo_Prefix(WeatherManager __instance, WeatherDef newWeather)
        {
            try
            {
                if (__instance?.map == null) return true;
                PoopocalypseComponent comp = __instance.map.GetComponent<PoopocalypseComponent>();
                if (comp != null && comp.IsActive)
                {
                    WeatherDef blindFogDef = DefDatabase<WeatherDef>.GetNamed("BlindFog", false);
                    if (blindFogDef != null && newWeather != blindFogDef)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Error in TransitionTo patch: {ex}");
                return true;
            }
        }

        public static void GetSkyColor_Postfix(Map map, ref Color __result)
        {
            try
            {
                if (map == null) return;
                PoopocalypseComponent comp = map.GetComponent<PoopocalypseComponent>();
                if (comp != null && comp.IsActive)
                {
                    __result = new Color(__result.r * 0.7f, __result.g * 0.5f, __result.b * 0.3f, __result.a);
                }
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Error in GetSkyColor patch: {ex}");
            }
        }

        public static void GetOverlayColor_Postfix(Map map, ref Color __result)
        {
            try
            {
                if (map == null) return;
                PoopocalypseComponent comp = map.GetComponent<PoopocalypseComponent>();
                if (comp != null && comp.IsActive)
                {
                    __result = new Color(0.45f, 0.25f, 0.1f, 0.4f);
                }
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Error in GetOverlayColor patch: {ex}");
            }
        }
    }

    public class IncidentWorker_Poopocalypse : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (parms?.target == null) return false;
            Map map = (Map)parms.target;
            if (map == null) return false;
            PoopocalypseComponent comp = map.GetComponent<PoopocalypseComponent>();
            if (comp != null && comp.IsActive)
                return false;
            return true;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (parms?.target == null) return false;
            Map map = (Map)parms.target;
            if (map == null || map.weatherManager == null) return false;

            WeatherDef blindFogDef = DefDatabase<WeatherDef>.GetNamed("BlindFog", false);
            if (blindFogDef == null)
            {
                //Log.Error("[Poopocalypse] Could not find BlindFog weather def!");
                return false;
            }

            map.weatherManager.TransitionTo(blindFogDef);

            PoopocalypseComponent component = map.GetComponent<PoopocalypseComponent>();
            if (component == null)
            {
                component = new PoopocalypseComponent(map);
                map.components.Add(component);
            }

            component.StartPoopocalypse();

            string[] letterTexts = new string[]
            {
                "poopocalypse_letter_text_1".Translate(),
                "poopocalypse_letter_text_2".Translate(),
                "poopocalypse_letter_text_3".Translate(),
                "poopocalypse_letter_text_4".Translate(),
                "poopocalypse_letter_text_5".Translate()
            };
            string selectedText = letterTexts.RandomElement();

            Find.LetterStack?.ReceiveLetter(
                "poopocalypse_letter_label".Translate(),
                selectedText,
                LetterDefOf.ThreatBig,
                new TargetInfo(map.Center, map)
            );

            return true;
        }
    }

    public class PoopocalypseComponent : MapComponent
    {
        private const int EventDuration = 60000;
        private const int CreatureSpawnInterval = 1500;
        private const int MaxCreaturesPerWave = 4;
        private const int FilthSpawnInterval = 30;
        private const int FilthPerSpawn = 25;
        private const int MaxTotalCreatures = 20;

        private int remainingTicks;
        private bool active;
        private int filthSpawned;
        private int creaturesSpawned;
        private int nextSpawnTick;
        private WeatherDef blindFogDef;
        private ThingDef filthDef;
        private ThingDef creatureDef;
        private PawnKindDef creatureKindDef;
        private List<Pawn> spawnedCreatures;
        private bool endMessageShown;

        public bool IsActive => active;
        public int RemainingTicks => remainingTicks;

        public PoopocalypseComponent(Map map) : base(map)
        {
            try
            {
                blindFogDef = DefDatabase<WeatherDef>.GetNamed("BlindFog", false);
                filthDef = DefDatabase<ThingDef>.GetNamed("FilthFaeces", false);

                try
                {
                    creatureDef = DefDatabase<ThingDef>.GetNamed("SewageCreature", false);
                }
                catch
                {
                    creatureDef = null;
                }

                try
                {
                    creatureKindDef = DefDatabase<PawnKindDef>.GetNamed("SewageCreature", false);
                }
                catch
                {
                    creatureKindDef = null;
                }

                spawnedCreatures = new List<Pawn>();
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Error in constructor: {ex}");
            }
        }

        public void StartPoopocalypse()
        {
            try
            {
                remainingTicks = EventDuration;
                active = true;
                filthSpawned = 0;
                creaturesSpawned = 0;
                endMessageShown = false;
                nextSpawnTick = Find.TickManager?.TicksGame + 300 ?? 0;
                //Log.Message("[Poopocalypse] Poopocalypse started!");
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Error in StartPoopocalypse: {ex}");
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            try
            {
                if (!active) return;
                if (map == null || map.weatherManager == null) return;
                if (Find.TickManager == null) return;

                remainingTicks--;

                if (remainingTicks <= 0 && !endMessageShown)
                {
                    EndPoopocalypse();
                    return;
                }

                if (blindFogDef != null && map.weatherManager.curWeather != blindFogDef)
                {
                    map.weatherManager.TransitionTo(blindFogDef);
                }

                if (Find.TickManager.TicksGame % FilthSpawnInterval == 0)
                {
                    SpawnFilth();
                }

                if (Find.TickManager.TicksGame >= nextSpawnTick && remainingTicks > 3000)
                {
                    if (creatureDef != null || creatureKindDef != null)
                    {
                        SpawnCreatures();
                        nextSpawnTick = Find.TickManager.TicksGame + CreatureSpawnInterval;
                    }
                }
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Error in MapComponentTick: {ex}");
            }
        }

        private void SpawnFilth()
        {
            try
            {
                if (filthDef == null || map == null) return;

                for (int i = 0; i < FilthPerSpawn; i++)
                {
                    IntVec3 cell = CellFinder.RandomCell(map);
                    if (cell.IsValid && cell.Walkable(map) && !cell.GetTerrain(map).IsWater)
                    {
                        if (Rand.Chance(0.65f))
                        {
                            if (FilthMaker.TryMakeFilth(cell, map, filthDef, 1))
                            {
                                filthSpawned++;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Error in SpawnFilth: {ex}");
            }
        }

        private void SpawnCreatures()
        {
            try
            {
                if (map == null) return;

                int countToSpawn = Rand.RangeInclusive(2, MaxCreaturesPerWave);

                for (int i = 0; i < countToSpawn; i++)
                {
                    if (creaturesSpawned >= MaxTotalCreatures) break;

                    IntVec3 cell = FindValidSpawnCell();
                    if (!cell.IsValid) continue;

                    try
                    {
                        Pawn creature = null;

                        if (creatureKindDef != null)
                        {
                            creature = PawnGenerator.GeneratePawn(creatureKindDef, Faction.OfEntities);
                            if (creature != null)
                            {
                                GenSpawn.Spawn(creature, cell, map, Rot4.Random);
                            }
                        }
                        else if (creatureDef != null)
                        {
                            Thing thing = GenSpawn.Spawn(creatureDef, cell, map, Rot4.Random);
                            creature = thing as Pawn;
                        }

                        if (creature == null) continue;

                        spawnedCreatures.Add(creature);
                        creaturesSpawned++;

                        try
                        {
                            if (creature.mindState != null)
                            {
                                creature.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter);
                            }
                        }
                        catch { }
                    }
                    catch { }
                }
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Error in SpawnCreatures: {ex}");
            }
        }

        private IntVec3 FindValidSpawnCell()
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                IntVec3 cell = CellFinder.RandomCell(map);
                if (cell.IsValid && cell.Walkable(map) && !cell.Fogged(map) && cell.GetFirstPawn(map) == null)
                {
                    if (cell.x > 5 && cell.x < map.Size.x - 5 && cell.z > 5 && cell.z < map.Size.z - 5)
                    {
                        return cell;
                    }
                }
            }
            return IntVec3.Invalid;
        }

        private void EndPoopocalypse()
        {
            try
            {
                active = false;
                endMessageShown = true;

                if (spawnedCreatures != null)
                {
                    foreach (var creature in spawnedCreatures)
                    {
                        try
                        {
                            if (creature != null && !creature.Destroyed)
                            {
                                creature.Destroy();
                            }
                        }
                        catch { }
                    }
                    spawnedCreatures.Clear();
                }

                if (map != null)
                {
                    Messages.Message(
                        "poopocalypse_ended".Translate(filthSpawned, creaturesSpawned),
                        new TargetInfo(map.Center, map),
                        MessageTypeDefOf.NeutralEvent
                    );
                }

                //Log.Message($"[Poopocalypse] Poopocalypse ended. Filth: {filthSpawned}, Creatures: {creaturesSpawned}");
            }
            catch (Exception)
            {
                //Log.Error($"[Poopocalypse] Error in EndPoopocalypse: {ex}");
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref remainingTicks, "remainingTicks", 0);
            Scribe_Values.Look(ref active, "active", false);
            Scribe_Values.Look(ref filthSpawned, "filthSpawned", 0);
            Scribe_Values.Look(ref creaturesSpawned, "creaturesSpawned", 0);
            Scribe_Values.Look(ref nextSpawnTick, "nextSpawnTick", 0);
            Scribe_Values.Look(ref endMessageShown, "endMessageShown", false);
            Scribe_Collections.Look(ref spawnedCreatures, "spawnedCreatures", LookMode.Reference);
        }
    }
}