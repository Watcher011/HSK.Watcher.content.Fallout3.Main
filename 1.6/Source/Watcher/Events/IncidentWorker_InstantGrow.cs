using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

//Этот код добавляет в игру аномальное погодное явление - зелёный дождь, который мгновенно выращивает урожай и порождает слизистых существ.

namespace Watcher.Events
{
    public class IncidentWorker_GreenRain : IncidentWorker
    {
        public const float Radius = 25f;
        public const int TicksBetweenPulses = 30;
        public const int TotalPulses = 8;
        public const int RainDurationTicks = 6000;
        public const int MucusSpawnCount = 8;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            foreach (Zone zone in map.zoneManager.AllZones)
            {
                if (zone is Zone_Growing growZone)
                {
                    foreach (IntVec3 cell in growZone.Cells)
                    {
                        Plant plant = cell.GetPlant(map);
                        if (plant != null && plant.def.plant != null && !plant.HarvestableNow && !plant.Destroyed)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            List<Plant> affectedPlants = new List<Plant>();
            int totalPlants = 0;
            int grownPlants = 0;
            IntVec3 centerCell = IntVec3.Invalid;

            foreach (Zone zone in map.zoneManager.AllZones)
            {
                if (zone is Zone_Growing growZone)
                {
                    foreach (IntVec3 cell in growZone.Cells)
                    {
                        Plant plant = cell.GetPlant(map);
                        if (plant != null && plant.def.plant != null && !plant.Destroyed)
                        {
                            totalPlants++;

                            if (!centerCell.IsValid)
                            {
                                centerCell = plant.Position;
                            }

                            if (!plant.HarvestableNow)
                            {
                                affectedPlants.Add(plant);
                            }
                        }
                    }
                }
            }

            if (affectedPlants.Count == 0)
                return false;

            if (!centerCell.IsValid)
            {
                centerCell = affectedPlants[0].Position;
            }

            SoundDefOf.PsychicPulseGlobal.PlayOneShot(new TargetInfo(centerCell, map));

            string letterText = "GreenRain_LetterText".Translate();

            if (affectedPlants.Count > 30)
            {
                letterText += "\n\n" + "GreenRain_MassiveHarvest".Translate(affectedPlants.Count);
            }
            else if (affectedPlants.Count > 15)
            {
                letterText += "\n\n" + "GreenRain_LargeHarvest".Translate();
            }
            else if (affectedPlants.Count < 5)
            {
                letterText += "\n\n" + "GreenRain_SmallHarvest".Translate();
            }

            SendStandardLetter("GreenRain_LetterLabel".Translate(), letterText, def.letterDef, parms, new TargetInfo(centerCell, map));

            List<Plant> allPlantsInZones = new List<Plant>();
            foreach (Zone zone in map.zoneManager.AllZones)
            {
                if (zone is Zone_Growing growZone)
                {
                    foreach (IntVec3 cell in growZone.Cells)
                    {
                        Plant plant = cell.GetPlant(map);
                        if (plant != null && !plant.Destroyed)
                        {
                            allPlantsInZones.Add(plant);
                        }
                    }
                }
            }

            map.GetComponent<MapComponent_GreenRain>()?.StartGreenRainEffect(allPlantsInZones, centerCell, map);

            GameConditionManager conditionManager = map.GameConditionManager;
            GameCondition greenRainCondition = GameConditionMaker.MakeCondition(GameConditionDef.Named("GreenRain_WeatherCondition"));
            greenRainCondition.Duration = RainDurationTicks;
            greenRainCondition.Permanent = false;
            conditionManager.RegisterCondition(greenRainCondition);

            foreach (Plant plant in affectedPlants)
            {
                plant.Growth = 1f;

                System.Reflection.FieldInfo yieldField = typeof(Plant).GetField("yieldInt",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (yieldField != null)
                {
                    float baseYield = plant.def.plant.harvestYield;
                    float randomFactor = Rand.Range(0.8f, 1.3f);
                    yieldField.SetValue(plant, baseYield * randomFactor);
                }

                plant.DirtyMapMesh(map);
                grownPlants++;
            }

            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.RaceProps.Humanlike && pawn.Faction == Faction.OfPlayer)
                {
                    pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDef.Named("GreenRain_WeirdFeeling"));
                }
            }

            return true;
        }
    }

    public class MapComponent_GreenRain : MapComponent
    {
        private List<Plant> affectedPlants;
        private IntVec3 centerCell;
        private int ticksUntilNextPulse;
        private int pulsesLeft;
        private bool isActive;
        private List<Pawn> spawnedMucus;
        private bool mucusSpawned;

        public MapComponent_GreenRain(Map map) : base(map)
        {
            spawnedMucus = new List<Pawn>();
        }

        public void StartGreenRainEffect(List<Plant> plants, IntVec3 center, Map currentMap)
        {
            affectedPlants = plants;
            centerCell = center;
            ticksUntilNextPulse = IncidentWorker_GreenRain.TicksBetweenPulses;
            pulsesLeft = IncidentWorker_GreenRain.TotalPulses;
            isActive = true;
            mucusSpawned = false;
            spawnedMucus.Clear();

            // Спавним слизней сразу при старте
            if (!mucusSpawned)
            {
                SpawnMucusCreatures(currentMap);
                mucusSpawned = true;
            }
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            if (!isActive || affectedPlants == null || affectedPlants.Count == 0)
                return;

            ticksUntilNextPulse--;

            if (ticksUntilNextPulse <= 0)
            {
                ticksUntilNextPulse = IncidentWorker_GreenRain.TicksBetweenPulses;
                pulsesLeft--;

                int flashesPerPulse = Mathf.Max(3, affectedPlants.Count / 8);
                for (int i = 0; i < flashesPerPulse; i++)
                {
                    Plant randomPlant = affectedPlants[Rand.Range(0, affectedPlants.Count)];
                    if (randomPlant != null && !randomPlant.Destroyed)
                    {
                        FleckMaker.Static(randomPlant.Position, map, FleckDefOf.FlashHollow, 0.6f);

                        if (Rand.Chance(0.4f))
                        {
                            FleckMaker.ThrowMetaPuff(randomPlant.Position.ToVector3(), map);
                        }
                    }
                }

                if (pulsesLeft % 2 == 0)
                {
                    SoundDefOf.TurretAcquireTarget.PlayOneShot(new TargetInfo(centerCell, map));
                }

                if (pulsesLeft <= 0)
                {
                    isActive = false;
                    affectedPlants.Clear();
                }
            }
        }

        private void SpawnMucusCreatures(Map currentMap)
        {
            PawnKindDef mucusKind = PawnKindDef.Named("Mucus");
            if (mucusKind == null)
            {
                //Log.Warning("[Watcher] PawnKindDef 'Mucus' not found");
                return;
            }

            // Получаем все валидные клетки для спавна на карте
            List<IntVec3> validSpawnCells = new List<IntVec3>();

            // Ищем клетки по всей карте
            foreach (IntVec3 cell in currentMap.AllCells)
            {
                // Проверяем, что клетка подходит для спавна существа
                if (cell.Standable(currentMap) && !cell.Fogged(currentMap) && currentMap.reachability.CanReachColony(cell))
                {
                    validSpawnCells.Add(cell);
                }
            }

            if (validSpawnCells.Count == 0)
            {
                //Log.Warning("[Watcher] No valid spawn cells found for Mucus");
                return;
            }

            int spawnedCount = 0;
            for (int i = 0; i < IncidentWorker_GreenRain.MucusSpawnCount; i++)
            {
                // Выбираем случайную клетку из всех валидных
                IntVec3 spawnCell = validSpawnCells[Rand.Range(0, validSpawnCells.Count)];

                // Проверяем ещё раз, что клетка валидна
                if (!spawnCell.IsValid || !spawnCell.Standable(currentMap))
                    continue;

                Pawn mucus = PawnGenerator.GeneratePawn(mucusKind, null);
                if (mucus != null)
                {
                    GenSpawn.Spawn(mucus, spawnCell, currentMap);
                    spawnedMucus.Add(mucus);
                    spawnedCount++;

                    // Заставляем слизня бродить по карте
                    mucus.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Wander_Psychotic);

                    // Добавляем эффект появления
                    FleckMaker.Static(spawnCell, currentMap, FleckDefOf.PsycastAreaEffect, 0.5f);
                }
            }

            //Log.Message($"[Watcher] Spawned {spawnedCount} Mucus creatures across the map");
        }

        public void DespawnAllMucus()
        {
            foreach (Pawn mucus in spawnedMucus)
            {
                if (mucus != null && !mucus.Destroyed && mucus.Spawned)
                {
                    FleckMaker.Static(mucus.Position, map, FleckDefOf.PsycastAreaEffect, 1f);
                    mucus.Destroy(DestroyMode.Vanish);
                }
            }
            spawnedMucus.Clear();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref isActive, "isActive", false);
            Scribe_Values.Look(ref ticksUntilNextPulse, "ticksUntilNextPulse", 0);
            Scribe_Values.Look(ref pulsesLeft, "pulsesLeft", 0);
            Scribe_Values.Look(ref centerCell, "centerCell");
            Scribe_Collections.Look(ref affectedPlants, "affectedPlants", LookMode.Reference);
            Scribe_Collections.Look(ref spawnedMucus, "spawnedMucus", LookMode.Reference);
            Scribe_Values.Look(ref mucusSpawned, "mucusSpawned", false);
        }
    }

    public class GameCondition_GreenRain : GameCondition
    {
        private SkyColorSet greenSkyColors;
        private const float SkyLerpSpeed = 0.5f;

        private static WeatherDef cachedRainDef;
        private static WeatherDef cachedRainyThunderstormDef;

        public GameCondition_GreenRain()
        {
            greenSkyColors = new SkyColorSet(
                new Color(0.2f, 0.6f, 0.2f),
                new Color(0.3f, 0.8f, 0.3f),
                new Color(0.4f, 0.9f, 0.4f),
                0.8f
            );
        }

        private static WeatherDef RainDef
        {
            get
            {
                if (cachedRainDef == null)
                {
                    cachedRainDef = DefDatabase<WeatherDef>.GetNamed("Rain", false);
                    if (cachedRainDef == null)
                    {
                        //Log.Warning("[Watcher] WeatherDef 'Rain' not found, using fallback");
                        cachedRainDef = WeatherDefOf.Clear;
                    }
                }
                return cachedRainDef;
            }
        }

        private static WeatherDef RainyThunderstormDef
        {
            get
            {
                if (cachedRainyThunderstormDef == null)
                {
                    cachedRainyThunderstormDef = DefDatabase<WeatherDef>.GetNamed("RainyThunderstorm", false);
                }
                return cachedRainyThunderstormDef;
            }
        }

        public override void Init()
        {
            base.Init();
            if (this.SingleMap != null && RainDef != null)
            {
                this.SingleMap.weatherManager.TransitionTo(RainDef);
            }
        }

        public override void GameConditionTick()
        {
            base.GameConditionTick();

            if (this.SingleMap != null && this.TicksLeft > 0 && RainDef != null)
            {
                Map map = this.SingleMap;
                WeatherDef currentWeather = map.weatherManager.curWeather;

                if (currentWeather != RainDef && currentWeather != RainyThunderstormDef)
                {
                    map.weatherManager.TransitionTo(RainDef);
                }
            }
        }

        public override void End()
        {
            base.End();
            MapComponent_GreenRain component = this.SingleMap?.GetComponent<MapComponent_GreenRain>();
            component?.DespawnAllMucus();
        }

        public override SkyTarget? SkyTarget(Map map)
        {
            return new SkyTarget(1f, greenSkyColors, 1f, 1f);
        }

        public override float SkyTargetLerpFactor(Map map)
        {
            return SkyLerpSpeed;
        }
    }
}