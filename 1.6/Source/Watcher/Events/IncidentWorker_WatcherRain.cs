using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using UnityEngine;
using Verse;

//Этот код добавляет в игру атмосферное событие - прометивый дождь, который длится ~10 часов игрового времени,
//оставляя после себя токсичную грязь и изменяя визуальное восприятие мира.

namespace Watcher.Events
{
    // Класс инициализации мода с Harmony
    [StaticConstructorOnStartup]
    public static class WatcherMod
    {
        static WatcherMod()
        {
            try
            {
                var harmony = new Harmony("watcher.events");

                // Патчим вручную
                PatchWeatherManager(harmony);
                PatchSkyManager(harmony);

                //Log.Message("[Watcher.Events] Mod loaded with Harmony patches!");
            }
            catch (Exception ex)
            {
                //Log.Error($"[Watcher.Events] Failed to load Harmony: {ex}");
            }
        }

        private static void PatchWeatherManager(Harmony harmony)
        {
            try
            {
                Type weatherManagerType = typeof(WeatherManager);

                // Патчим свойство CurWeatherLabel
                var curWeatherLabelGetter = weatherManagerType.GetProperty("CurWeatherLabel", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
                if (curWeatherLabelGetter != null)
                {
                    var postfix = typeof(WeatherManager_CurWeatherLabel_Patch).GetMethod("Postfix");
                    harmony.Patch(curWeatherLabelGetter, postfix: new HarmonyMethod(postfix));
                }

                // Патчим свойство CurWeatherDescription
                var curWeatherDescriptionGetter = weatherManagerType.GetProperty("CurWeatherDescription", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
                if (curWeatherDescriptionGetter != null)
                {
                    var postfix = typeof(WeatherManager_CurWeatherDescription_Patch).GetMethod("Postfix");
                    harmony.Patch(curWeatherDescriptionGetter, postfix: new HarmonyMethod(postfix));
                }

                // Патчим метод TransitionTo
                var transitionTo = weatherManagerType.GetMethod("TransitionTo", BindingFlags.Public | BindingFlags.Instance);
                if (transitionTo != null)
                {
                    var prefix = typeof(WeatherManager_TransitionTo_Patch).GetMethod("Prefix");
                    harmony.Patch(transitionTo, prefix: new HarmonyMethod(prefix));
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"[Watcher.Events] Failed to patch WeatherManager: {ex}");
            }
        }

        private static void PatchSkyManager(Harmony harmony)
        {
            try
            {
                Type skyManagerType = typeof(SkyManager);

                // Патчим метод GetSkyColor
                var getSkyColor = skyManagerType.GetMethod("GetSkyColor", BindingFlags.Public | BindingFlags.Instance);
                if (getSkyColor != null)
                {
                    var postfix = typeof(SkyManager_GetSkyColor_Patch).GetMethod("Postfix");
                    harmony.Patch(getSkyColor, postfix: new HarmonyMethod(postfix));
                }

                // Патчим метод GetOverlayColor
                var getOverlayColor = skyManagerType.GetMethod("GetOverlayColor", BindingFlags.Public | BindingFlags.Instance);
                if (getOverlayColor != null)
                {
                    var postfix = typeof(SkyManager_GetOverlayColor_Patch).GetMethod("Postfix");
                    harmony.Patch(getOverlayColor, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                //Log.Error($"[Watcher.Events] Failed to patch SkyManager: {ex}");
            }
        }
    }

    // Класс события
    public class IncidentWorker_WatcherRain : IncidentWorker
    {
        private const int RainDuration = 25000; // 10 часов 

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = (Map)parms.target;

            // Проверяем, не активен ли уже дождь
            WatcherRainComponent comp = map.GetComponent<WatcherRainComponent>();
            if (comp != null && comp.IsActive)
                return false;

            return true;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;

            // Получаем погоду Rain по имени
            WeatherDef rainDef = DefDatabase<WeatherDef>.GetNamed("Rain", false);
            if (rainDef == null)
            {
                //Log.Error("[Watcher.Events] Could not find Rain weather def!");
                return false;
            }

            // Устанавливаем дождь
            map.weatherManager.TransitionTo(rainDef);

            // Создаём компонент для отслеживания
            WatcherRainComponent component = map.GetComponent<WatcherRainComponent>();
            if (component == null)
            {
                component = new WatcherRainComponent(map);
                map.components.Add(component);
            }

            // Активируем событие
            component.StartRainEvent(RainDuration);

            // Выбираем случайный текст письма
            string[] letterTexts = new string[]
            {
                "watcher_rain_letter_text_1".Translate(),
                "watcher_rain_letter_text_2".Translate(),
                "watcher_rain_letter_text_3".Translate(),
                "watcher_rain_letter_text_4".Translate()
            };
            string selectedText = letterTexts.RandomElement();

            // Отправляем письмо (негативный ивент)
            Find.LetterStack.ReceiveLetter(
                "watcher_rain_letter_label".Translate(),
                selectedText,
                LetterDefOf.NegativeEvent,
                new TargetInfo(map.Center, map)
            );

            return true;
        }
    }

    // Компонент карты для отслеживания события
    public class WatcherRainComponent : MapComponent
    {
        private int remainingTicks;
        private bool active;
        private int filthSpawned;
        private WeatherDef rainDef;
        private bool endMessageShown;

        public bool IsActive => active;

        public WatcherRainComponent(Map map) : base(map)
        {
            rainDef = DefDatabase<WeatherDef>.GetNamed("Rain", false);
        }

        public void StartRainEvent(int duration)
        {
            remainingTicks = duration;
            active = true;
            filthSpawned = 0;
            endMessageShown = false;
            //Log.Message($"[Watcher.Events] Event started for {duration} ticks");
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            if (!active) return;

            remainingTicks--;

            if (remainingTicks <= 0 && !endMessageShown)
            {
                EndRainEvent();
                return;
            }

            // Проверяем, что дождь всё ещё идёт
            if (rainDef != null && map.weatherManager.curWeather != rainDef)
            {
                // Возвращаем дождь, если погода изменилась
                map.weatherManager.TransitionTo(rainDef);
            }

            // Спавним грязь каждые 30 тиков (0.5 секунды)
            if (Find.TickManager.TicksGame % 30 == 0)
            {
                SpawnFilth();
            }
        }

        private void SpawnFilth()
        {
            ThingDef filthDef = DefDatabase<ThingDef>.GetNamed("FilthPrometheum", false);
            if (filthDef == null) return;

            for (int i = 0; i < 8; i++)
            {
                IntVec3 cell = CellFinder.RandomCell(map);
                if (cell.IsValid && cell.Walkable(map) && !cell.GetTerrain(map).IsWater)
                {
                    // Проверяем, что клетка не под крышей
                    if (map.roofGrid.Roofed(cell))
                        continue;

                    if (Rand.Chance(0.25f))
                    {
                        FilthMaker.TryMakeFilth(cell, map, filthDef, 1);
                        filthSpawned++;
                    }
                }
            }
        }

        private void EndRainEvent()
        {
            active = false;
            endMessageShown = true;

            // Показываем сообщение о конце ивента
            Messages.Message(
                "watcher_rain_ended".Translate(filthSpawned),
                new TargetInfo(map.Center, map),
                MessageTypeDefOf.NeutralEvent
            );

            //Log.Message($"[Watcher.Events] Event ended. Total filth: {filthSpawned}");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref remainingTicks, "remainingTicks", 0);
            Scribe_Values.Look(ref active, "active", false);
            Scribe_Values.Look(ref filthSpawned, "filthSpawned", 0);
            Scribe_Values.Look(ref endMessageShown, "endMessageShown", false);
        }
    }

    // HARMONY PATCH CLASSES

    [HarmonyPatch]
    public static class WeatherManager_CurWeatherLabel_Patch
    {
        public static bool Prepare() => true;

        public static void Postfix(WeatherManager __instance, ref string __result)
        {
            if (__instance?.map == null) return;

            WatcherRainComponent comp = __instance.map.GetComponent<WatcherRainComponent>();
            if (comp != null && comp.IsActive)
            {
                // Определяем интенсивность по остатку времени
                if (comp.IsActive)
                {
                    float progress = (float)comp.GetRemainingTicks() / 60000f;
                    if (progress > 0.66f)
                        __result = "watcher_rain_heavy".Translate();
                    else if (progress > 0.33f)
                        __result = "watcher_rain_medium".Translate();
                    else
                        __result = "watcher_rain_light".Translate();
                }
            }
        }
    }

    [HarmonyPatch]
    public static class WeatherManager_CurWeatherDescription_Patch
    {
        public static bool Prepare() => true;

        public static void Postfix(WeatherManager __instance, ref string __result)
        {
            if (__instance?.map == null) return;

            WatcherRainComponent comp = __instance.map.GetComponent<WatcherRainComponent>();
            if (comp != null && comp.IsActive)
            {
                __result = "watcher_rain_description".Translate();
            }
        }
    }

    [HarmonyPatch]
    public static class WeatherManager_TransitionTo_Patch
    {
        public static bool Prepare() => true;

        public static bool Prefix(WeatherManager __instance, WeatherDef newWeather)
        {
            if (__instance?.map == null) return true;

            WatcherRainComponent comp = __instance.map.GetComponent<WatcherRainComponent>();
            if (comp != null && comp.IsActive)
            {
                WeatherDef rainDef = DefDatabase<WeatherDef>.GetNamed("Rain", false);
                if (newWeather != rainDef)
                {
                    return false; // Блокируем смену погоды без сообщения
                }
            }
            return true;
        }
    }

    [HarmonyPatch]
    public static class SkyManager_GetSkyColor_Patch
    {
        public static bool Prepare() => true;

        public static void Postfix(Map map, ref Color __result)
        {
            if (map == null) return;

            WatcherRainComponent comp = map.GetComponent<WatcherRainComponent>();
            if (comp != null && comp.IsActive)
            {
                __result = new Color(
                    __result.r * 0.5f,
                    __result.g * 0.5f,
                    __result.b * 0.6f,
                    __result.a
                );
            }
        }
    }

    [HarmonyPatch]
    public static class SkyManager_GetOverlayColor_Patch
    {
        public static bool Prepare() => true;

        public static void Postfix(Map map, ref Color __result)
        {
            if (map == null) return;

            WatcherRainComponent comp = map.GetComponent<WatcherRainComponent>();
            if (comp != null && comp.IsActive)
            {
                __result = new Color(0.1f, 0.08f, 0.12f, 0.3f);
            }
        }
    }

    // Extension methods для доступа к полям компонента
    public static class WatcherRainComponentExtensions
    {
        public static int GetRemainingTicks(this WatcherRainComponent comp)
        {
            var field = typeof(WatcherRainComponent).GetField("remainingTicks", BindingFlags.NonPublic | BindingFlags.Instance);
            return (int)field.GetValue(comp);
        }
    }
}