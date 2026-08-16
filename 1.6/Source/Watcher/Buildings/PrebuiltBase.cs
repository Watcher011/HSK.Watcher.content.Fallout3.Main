using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Watcher.Comps
{
    #region Debug Config
    public static class PrebuiltBaseDebug
    {
        public const bool Enabled = false;

        public static void Log(string message)
        {
            if (Enabled)
            {
            }
        }

        public static void Warning(string message)
        {
            if (Enabled)
            {
            }
        }

        public static void Error(string message)
        {
            Verse.Log.Error("[PrebuiltBase] " + message);
        }
    }
    #endregion

    #region Configuration
    public static class PrebuiltBaseConfig
    {
        public const int RoomWidth = 12;
        public const int RoomHeight = 12;

        public static readonly int[,] RoomLayout = new int[12, 12]
        {
            { 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0 },
            { 0,-1,-1, 0,-1,-1, 0,-1,-1,-1,-1, 0 },
            { 0,-1,-1, 0, 2, 2, 0,-1,-1,-1,-1, 0 },
            { 0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, 0 },
            { 0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, 0 },
            { 0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, 0 },
            { 0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, 0 },
            { 0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, 0 },
            { 0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, 0 },
            { 0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, 0 },
            { 0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1, 0 },
            { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
        };

        public const string RoofDef = "BunkerRoof";
        public const string FallbackRoofDef = "RoofRockThick";
        public const string FloorDef = "DarkConcrete";
        public static readonly string[] FallbackFloorDefs = { "Concrete", "TileGranite" };
        public const string WallFloorDef = "TileSterling";
        public static readonly string[] FallbackWallFloorDefs = { "TileGranite", "Concrete" };
        public const string WallDef = "ClutterSilverWall";
        public const string FallbackWallDef = "Wall";
        public const string WallStuffDef = "PlateSteel";
        public const string OuterDoorDef = "ArmoredDoor";
        public const string InnerDoorDef = "QDoor";
        public const string DoorStuffDef = "PlateSteel";
        public const string FallbackDoorDef = "Door";

        public static readonly string[] RoomBuildings = {
            "Bed@1,1", "Bed@2,1", "Bed@1,2", "Bed@2,2",
            "VaultLamp@6,6",
            "SteelShelf@7,2", "SteelShelf@7,3", "SteelShelf@7,4", "SteelShelf@7,5",
            "Table@4,8", "Chair@3,8", "Chair@5,8",
        };

        public static readonly string[] RoomItems = {
            "Chemfuel:100@7,2", "Steel:100@8,2", "ComponentIndustrial:20@9,2",
            "MedicineIndustrial:30@7,4", "RadAway:50@8,4", "Stimpak:20@9,4",
            "MedicineHerbal:10@1,1", "MedicineHerbal:10@1,2",
            "Apparel_Pants@1,0", "Apparel_Shirt@2,0",
            "SimpleMeal:10@4,8", "Beer:5@4,8",
            "ComponentIndustrial:5@3,0", "Steel:50@4,0",
        };

        public const int PowerRoomWidth = 6;
        public const int PowerRoomHeight = 6;
        public const int PowerRoomOffsetX = 13;
        public const int PowerRoomOffsetZ = 3;

        public static readonly string[] PowerRoomBuildings = {
            "VaultTecPortableGenerator@2,2", "VaultBattery@3,2",
            "PowerConduit@0,0", "PowerConduit@1,0", "PowerConduit@2,0", "PowerConduit@3,0", "PowerConduit@4,0", "PowerConduit@5,0",
            "PowerConduit@0,5", "PowerConduit@1,5", "PowerConduit@2,5", "PowerConduit@3,5", "PowerConduit@4,5", "PowerConduit@5,5",
            "PowerConduit@0,1", "PowerConduit@0,2", "PowerConduit@0,3", "PowerConduit@0,4",
            "PowerConduit@5,1", "PowerConduit@5,2", "PowerConduit@5,3", "PowerConduit@5,4"
        };

        public static readonly string[] PowerRoomItems = {
            "Chemfuel:75@2,2", "ComponentIndustrial:5@3,3", "Steel:50@2,3"
        };

        public static readonly string[] PowerRoomDoorPositions = { "2" };
        public const string PowerRoomDoorSide = "west";
        public const string PowerRoomDoorDef = "Door";
        public const string PowerRoomDoorStuffDef = "PlateSteel";

        public const int ClearMargin = 3;
        public const bool ClearPlants = true;
        public const bool ClearFilth = true;
        public const int MaxLocationAttempts = 50;

        // Имя Def сценария (точное совпадение defName)
        public const string RequiredScenarioDefName = "ShelterResidents";

        // Альтернативные имена для проверки (если defName не совпадает)
        public static readonly string[] AllowedScenarioKeywords = { "Shelter", "Убежище", "Protocol", "Протокол" };
    }
    #endregion

    #region Room Builder
    public class RoomBuilder
    {
        private readonly Map map;
        private readonly IntVec3 center;
        private IntVec3 roomStart;
        private IntVec3 powerRoomStart;

        public RoomBuilder(Map map, IntVec3 center)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.center = center;
        }

        public void BuildRoom()
        {
            try
            {
                PrebuiltBaseDebug.Log("===== BUILD START =====");

                if (!IsCorrectScenario())
                {
                    PrebuiltBaseDebug.Log("Scenario mismatch: " + PrebuiltBaseConfig.RequiredScenarioDefName + " - SKIPPING BUILD");
                    return;
                }

                IntVec3 roomCenter = FindRoomLocation();
                if (roomCenter == IntVec3.Invalid)
                {
                    PrebuiltBaseDebug.Error("No valid location found");
                    return;
                }

                roomStart = new IntVec3(
                    roomCenter.x - PrebuiltBaseConfig.RoomWidth / 2,
                    0,
                    roomCenter.z - PrebuiltBaseConfig.RoomHeight / 2
                );

                IntVec3 powerRoomCenter = new IntVec3(
                    roomCenter.x + PrebuiltBaseConfig.PowerRoomOffsetX,
                    0,
                    roomCenter.z + PrebuiltBaseConfig.PowerRoomOffsetZ
                );
                powerRoomStart = new IntVec3(
                    powerRoomCenter.x - PrebuiltBaseConfig.PowerRoomWidth / 2,
                    0,
                    powerRoomCenter.z - PrebuiltBaseConfig.PowerRoomHeight / 2
                );

                if (!AreRoomsWithinMapBounds())
                {
                    PrebuiltBaseDebug.Error("Rooms out of map bounds");
                    return;
                }

                PrebuiltBaseDebug.Log("Main room: " + roomStart + ", Power room: " + powerRoomStart);

                ClearRoomArea();
                ClearPowerRoomArea();
                BuildRoomFromLayout();
                BuildPowerRoom();
                ClearAreaAround();

                PrebuiltBaseDebug.Log("===== BUILD SUCCESS =====");
            }
            catch (Exception ex)
            {
                PrebuiltBaseDebug.Error("Build error: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        private void BuildRoomFromLayout()
        {
            try
            {
                PrebuiltBaseDebug.Log("Building room from layout...");

                ThingDef wallDef = GetDefOrFallback<ThingDef>(PrebuiltBaseConfig.WallDef,
                    PrebuiltBaseConfig.FallbackWallDef, ThingDefOf.Wall);
                ThingDef outerDoorDef = GetDefOrFallback<ThingDef>(PrebuiltBaseConfig.OuterDoorDef,
                    PrebuiltBaseConfig.FallbackDoorDef, ThingDefOf.Door);
                ThingDef innerDoorDef = GetDefOrFallback<ThingDef>(PrebuiltBaseConfig.InnerDoorDef,
                    PrebuiltBaseConfig.FallbackDoorDef, ThingDefOf.Door);
                ThingDef wallStuff = GetStuffDef(PrebuiltBaseConfig.WallStuffDef, GetPlateSteelDef());
                ThingDef doorStuff = GetStuffDef(PrebuiltBaseConfig.DoorStuffDef, GetPlateSteelDef());
                TerrainDef floorDef = GetFloorDef();
                TerrainDef wallFloorDef = GetWallFloorDef();
                RoofDef roofDef = GetDefOrFallback<RoofDef>(PrebuiltBaseConfig.RoofDef,
                    PrebuiltBaseConfig.FallbackRoofDef, RoofDefOf.RoofRockThick);

                PrebuiltBaseDebug.Log("Defs loaded: Wall=" + (wallDef?.defName ?? "null") +
                    ", OuterDoor=" + (outerDoorDef?.defName ?? "null") +
                    ", InnerDoor=" + (innerDoorDef?.defName ?? "null"));

                // Pass 1: Floor
                for (int x = 0; x < PrebuiltBaseConfig.RoomWidth; x++)
                {
                    for (int z = 0; z < PrebuiltBaseConfig.RoomHeight; z++)
                    {
                        IntVec3 pos = new IntVec3(roomStart.x + x, 0, roomStart.z + z);
                        if (!pos.InBounds(map)) continue;

                        int cellType = PrebuiltBaseConfig.RoomLayout[z, x];
                        TerrainDef terrain = (cellType == 0 || cellType == 1 || cellType == 2)
                            ? wallFloorDef
                            : floorDef;

                        if (terrain != null)
                            map.terrainGrid.SetTerrain(pos, terrain);
                    }
                }

                // Pass 2: Walls and doors
                int wallCount = 0;
                int doorCount = 0;

                for (int x = 0; x < PrebuiltBaseConfig.RoomWidth; x++)
                {
                    for (int z = 0; z < PrebuiltBaseConfig.RoomHeight; z++)
                    {
                        IntVec3 pos = new IntVec3(roomStart.x + x, 0, roomStart.z + z);
                        if (!pos.InBounds(map)) continue;

                        int cellType = PrebuiltBaseConfig.RoomLayout[z, x];

                        switch (cellType)
                        {
                            case 0:
                                if (PlaceWallSimple(pos, wallDef, wallStuff))
                                    wallCount++;
                                map.roofGrid.SetRoof(pos, roofDef);
                                break;
                            case 1:
                                if (PlaceDoorSimple(pos, outerDoorDef, doorStuff, true))
                                {
                                    doorCount++;
                                    PrebuiltBaseDebug.Log("NORTH ENTRANCE ArmoredDoor at " + pos);
                                }
                                map.roofGrid.SetRoof(pos, roofDef);
                                break;
                            case 2:
                                if (PlaceDoorSimple(pos, innerDoorDef, doorStuff, false))
                                {
                                    doorCount++;
                                    PrebuiltBaseDebug.Log("INNER AIRLOCK QDoor at " + pos);
                                }
                                map.roofGrid.SetRoof(pos, roofDef);
                                break;
                            case -1:
                                map.roofGrid.SetRoof(pos, roofDef);
                                break;
                        }
                    }
                }

                PrebuiltBaseDebug.Log("Built: " + wallCount + " walls, " + doorCount + " doors");

                PlaceRoomFurniture();
                PlaceRoomItems();
            }
            catch (Exception ex)
            {
                PrebuiltBaseDebug.Error("BuildRoomFromLayout error: " + ex.Message);
            }
        }

        private bool PlaceDoorSimple(IntVec3 pos, ThingDef doorDef, ThingDef doorStuff, bool isOuterDoor)
        {
            try
            {
                if (!pos.InBounds(map)) return false;

                foreach (Thing thing in pos.GetThingList(map))
                {
                    if (thing.def == doorDef)
                        return true;
                }

                ClearCellForBuilding(pos);

                Thing door;
                if (doorDef.MadeFromStuff)
                    door = ThingMaker.MakeThing(doorDef, doorStuff ?? GetPlateSteelDef());
                else
                    door = ThingMaker.MakeThing(doorDef);

                if (door == null) return false;

                door.SetFaction(Faction.OfPlayer);

                if (door is ThingWithComps doorWithComps)
                {
                    if (isOuterDoor && pos.z == roomStart.z)
                        doorWithComps.Rotation = Rot4.South;
                    else if (!isOuterDoor && pos.z == roomStart.z + 2)
                        doorWithComps.Rotation = Rot4.South;
                }

                GenSpawn.Spawn(door, pos, map, WipeMode.Vanish);
                return true;
            }
            catch (Exception ex)
            {
                PrebuiltBaseDebug.Error("Door error: " + ex.Message);
                return false;
            }
        }

        private bool PlaceWallSimple(IntVec3 pos, ThingDef wallDef, ThingDef wallStuff)
        {
            try
            {
                if (!pos.InBounds(map)) return false;

                foreach (Thing thing in pos.GetThingList(map))
                {
                    if (thing.def == wallDef || thing.def == ThingDefOf.Wall)
                        return true;
                }

                ClearCellForBuilding(pos);

                Thing wall;
                if (wallDef.MadeFromStuff)
                    wall = ThingMaker.MakeThing(wallDef, wallStuff ?? GetPlateSteelDef());
                else
                    wall = ThingMaker.MakeThing(wallDef);

                if (wall == null) return false;

                wall.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(wall, pos, map, WipeMode.Vanish);
                return true;
            }
            catch (Exception ex)
            {
                PrebuiltBaseDebug.Error("Wall error: " + ex.Message);
                return false;
            }
        }

        private void ClearCellForBuilding(IntVec3 pos)
        {
            if (!pos.InBounds(map)) return;

            List<Thing> toRemove = new List<Thing>();
            foreach (Thing thing in pos.GetThingList(map))
            {
                if (thing.def.category == ThingCategory.Plant ||
                    thing.def.category == ThingCategory.Filth ||
                    thing.def.category == ThingCategory.Item ||
                    (thing.def.mineable && thing.def.building != null && thing.def.building.isNaturalRock) ||
                    thing.def.category == ThingCategory.Building)
                {
                    toRemove.Add(thing);
                }
            }

            foreach (Thing thing in toRemove)
                thing.Destroy(DestroyMode.Vanish);
        }

        private bool IsCorrectScenario()
        {
            try
            {
                PrebuiltBaseDebug.Log("=== SCENARIO CHECK ===");

                if (Current.Game == null)
                {
                    PrebuiltBaseDebug.Error("Current.Game is NULL");
                    return false;
                }

                if (Current.Game.Scenario == null)
                {
                    PrebuiltBaseDebug.Error("Current.Game.Scenario is NULL");
                    return false;
                }

                string currentName = Current.Game.Scenario.name ?? "NULL";
                PrebuiltBaseDebug.Log("Current scenario name: '" + currentName + "'");

                // Ищем ScenarioDef по имени сценария через все возможные способы
                ScenarioDef currentDef = FindScenarioDefByName(currentName);
                string currentDefName = currentDef?.defName ?? "NULL";
                string currentLabel = currentDef?.label ?? "NULL";

                PrebuiltBaseDebug.Log("Found ScenarioDef: defName='" + currentDefName + "', label='" + currentLabel + "'");

                // Проверка 1: Точное совпадение defName
                if (currentDefName == PrebuiltBaseConfig.RequiredScenarioDefName)
                {
                    PrebuiltBaseDebug.Log("Matched by defName: " + currentDefName);
                    return true;
                }

                // Проверка 2: Проверка по label
                ScenarioDef required = DefDatabase<ScenarioDef>.GetNamedSilentFail(PrebuiltBaseConfig.RequiredScenarioDefName);
                if (required != null)
                {
                    string requiredLabel = required.label ?? required.defName;

                    if (currentLabel == requiredLabel)
                    {
                        PrebuiltBaseDebug.Log("Matched by label: " + currentLabel);
                        return true;
                    }

                    // Проверка label без скобок
                    int idx = currentLabel.IndexOf('(');
                    if (idx > 0)
                    {
                        string cleanCurrentLabel = currentLabel.Substring(0, idx).Trim();
                        if (cleanCurrentLabel == requiredLabel)
                        {
                            PrebuiltBaseDebug.Log("Matched by clean label: " + cleanCurrentLabel);
                            return true;
                        }
                    }
                }

                // Проверка 3: Прямое совпадение имени с RequiredScenarioDefName
                if (currentName == PrebuiltBaseConfig.RequiredScenarioDefName)
                {
                    PrebuiltBaseDebug.Log("Matched by exact scenario name: " + currentName);
                    return true;
                }

                // Проверка 4: Проверка по ключевым словам в defName, label или имени сценария
                foreach (string keyword in PrebuiltBaseConfig.AllowedScenarioKeywords)
                {
                    if (currentDefName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        currentLabel.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        currentName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        PrebuiltBaseDebug.Log("Matched by keyword '" + keyword + "' in: " + currentDefName + " / " + currentLabel + " / " + currentName);
                        return true;
                    }
                }

                PrebuiltBaseDebug.Log("Scenario check FAILED - no match found");
                return false;
            }
            catch (Exception ex)
            {
                PrebuiltBaseDebug.Error("Scenario check error: " + ex.Message);
                return false;
            }
        }

        private ScenarioDef FindScenarioDefByName(string scenarioName)
        {
            if (string.IsNullOrEmpty(scenarioName)) return null;

            // Убираем скобки и пробелы для сравнения
            string cleanName = scenarioName;
            int idx = scenarioName.IndexOf('(');
            if (idx > 0)
                cleanName = scenarioName.Substring(0, idx).Trim();

            PrebuiltBaseDebug.Log("Searching for ScenarioDef, clean name: '" + cleanName + "', original: '" + scenarioName + "'");

            // Сначала ищем по всем ScenarioDef напрямую
            foreach (ScenarioDef def in DefDatabase<ScenarioDef>.AllDefs)
            {
                // Точное совпадение по defName
                if (def.defName == cleanName || def.defName == scenarioName)
                {
                    PrebuiltBaseDebug.Log("Found by exact defName match: " + def.defName);
                    return def;
                }

                // Точное совпадение по label
                string defLabel = def.label ?? def.defName;
                if (defLabel == cleanName || defLabel == scenarioName)
                {
                    PrebuiltBaseDebug.Log("Found by exact label match: " + def.defName + " (label: " + defLabel + ")");
                    return def;
                }
            }

            // Если точное совпадение не найдено, ищем по частичному совпадению
            foreach (ScenarioDef def in DefDatabase<ScenarioDef>.AllDefs)
            {
                string defLabel = def.label ?? def.defName;

                // Частичное совпадение по defName (contains)
                if (def.defName.IndexOf(cleanName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cleanName.IndexOf(def.defName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PrebuiltBaseDebug.Log("Found by partial defName match: " + def.defName);
                    return def;
                }

                // Частичное совпадение по label (contains)
                if (defLabel.IndexOf(cleanName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cleanName.IndexOf(defLabel, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PrebuiltBaseDebug.Log("Found by partial label match: " + def.defName + " (label: " + defLabel + ")");
                    return def;
                }
            }

            PrebuiltBaseDebug.Warning("ScenarioDef not found for: " + scenarioName);
            return null;
        }

        private IntVec3 FindRoomLocation()
        {
            IntVec3 mapCenter = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            int searchRadius = Mathf.Min(map.Size.x / 8, map.Size.z / 8);

            PrebuiltBaseDebug.Log("Finding location, center: " + mapCenter + ", radius: " + searchRadius);

            for (int i = 0; i < PrebuiltBaseConfig.MaxLocationAttempts; i++)
            {
                IntVec3 testCenter = new IntVec3(
                    mapCenter.x + Rand.Range(-searchRadius, searchRadius),
                    0,
                    mapCenter.z + Rand.Range(-searchRadius, searchRadius)
                );

                if (IsGoodLocationForRoom(testCenter, PrebuiltBaseConfig.RoomWidth, PrebuiltBaseConfig.RoomHeight))
                {
                    IntVec3 powerCenter = new IntVec3(
                        testCenter.x + PrebuiltBaseConfig.PowerRoomOffsetX,
                        0,
                        testCenter.z + PrebuiltBaseConfig.PowerRoomOffsetZ
                    );

                    if (IsGoodLocation(powerCenter, PrebuiltBaseConfig.PowerRoomWidth, PrebuiltBaseConfig.PowerRoomHeight))
                    {
                        PrebuiltBaseDebug.Log("Location found on attempt " + i + ": " + testCenter);
                        return testCenter;
                    }
                }
            }

            PrebuiltBaseDebug.Warning("Location not found, using map center");
            return mapCenter;
        }

        private bool IsGoodLocation(IntVec3 center, int width, int height)
        {
            IntVec3 start = new IntVec3(center.x - width / 2, 0, center.z - height / 2);
            return IsGoodLocationForRoom(start, width, height);
        }

        private bool IsGoodLocationForRoom(IntVec3 center, int width, int height)
        {
            IntVec3 start = new IntVec3(center.x - width / 2, 0, center.z - height / 2);

            if (start.x < 0 || start.x + width >= map.Size.x ||
                start.z < 0 || start.z + height >= map.Size.z)
                return false;

            int passable = 0;
            int total = width * height;

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    IntVec3 pos = new IntVec3(start.x + x, 0, start.z + z);
                    if (pos.InBounds(map) && pos.Walkable(map) && !pos.Impassable(map))
                        passable++;
                }
            }
            return passable > total * 0.6f;
        }

        private bool AreRoomsWithinMapBounds()
        {
            return roomStart.x >= 0 &&
                   roomStart.x + PrebuiltBaseConfig.RoomWidth < map.Size.x &&
                   roomStart.z >= 0 &&
                   roomStart.z + PrebuiltBaseConfig.RoomHeight < map.Size.z &&
                   powerRoomStart.x >= 0 &&
                   powerRoomStart.x + PrebuiltBaseConfig.PowerRoomWidth < map.Size.x &&
                   powerRoomStart.z >= 0 &&
                   powerRoomStart.z + PrebuiltBaseConfig.PowerRoomHeight < map.Size.z;
        }

        private void ClearRoomArea()
        {
            PrebuiltBaseDebug.Log("Clearing main room area");
            ClearArea(roomStart, PrebuiltBaseConfig.RoomWidth, PrebuiltBaseConfig.RoomHeight);
        }

        private void ClearPowerRoomArea()
        {
            PrebuiltBaseDebug.Log("Clearing power room area");
            ClearArea(powerRoomStart, PrebuiltBaseConfig.PowerRoomWidth, PrebuiltBaseConfig.PowerRoomHeight);
        }

        private void ClearArea(IntVec3 start, int width, int height)
        {
            int cleared = 0;
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    IntVec3 pos = new IntVec3(start.x + x, 0, start.z + z);
                    if (pos.InBounds(map))
                        cleared += ClearCell(pos, true, true);
                }
            }
            PrebuiltBaseDebug.Log("Cleared " + cleared + " objects");
        }

        private void PlaceRoomFurniture()
        {
            PrebuiltBaseDebug.Log("Placing furniture...");

            foreach (string buildingStr in PrebuiltBaseConfig.RoomBuildings)
            {
                try
                {
                    string thingDefName;
                    IntVec3 pos;
                    Rot4 rotation;

                    if (!TryParseBuilding(buildingStr, false, out thingDefName, out pos, out rotation))
                        continue;

                    int relX = pos.x - roomStart.x;
                    int relZ = pos.z - roomStart.z;

                    if (relX >= 0 && relX < PrebuiltBaseConfig.RoomWidth &&
                        relZ >= 0 && relZ < PrebuiltBaseConfig.RoomHeight)
                    {
                        int cellType = PrebuiltBaseConfig.RoomLayout[relZ, relX];
                        if (cellType == 0 || cellType == 1 || cellType == 2)
                        {
                            PrebuiltBaseDebug.Warning("Position " + pos + " is wall/door, skipping " + thingDefName);
                            continue;
                        }
                    }

                    ThingDef thingDef = GetRoomThingDef(thingDefName);
                    if (thingDef == null)
                    {
                        PrebuiltBaseDebug.Warning("ThingDef not found: " + thingDefName);
                        continue;
                    }

                    ThingDef stuffDef = GetRoomStuff(thingDef, thingDefName);
                    Thing building;

                    if (stuffDef != null)
                        building = ThingMaker.MakeThing(thingDef, stuffDef);
                    else
                        building = ThingMaker.MakeThing(thingDef);

                    building.SetFaction(Faction.OfPlayer);

                    if (rotation != Rot4.Invalid && building is ThingWithComps twc)
                        twc.Rotation = rotation;

                    ClearCellForBuilding(pos);
                    GenSpawn.Spawn(building, pos, map);
                    PrebuiltBaseDebug.Log("Furniture " + thingDefName + " placed at " + pos);
                }
                catch (Exception ex)
                {
                    PrebuiltBaseDebug.Warning("Furniture error " + buildingStr + ": " + ex.Message);
                }
            }
        }

        private ThingDef GetRoomThingDef(string name)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(name, false);
            if (def != null) return def;

            if (name == "Bed")
                return ThingDefOf.Bed;
            if (name == "VaultLamp")
                return DefDatabase<ThingDef>.GetNamed("VaultLamp", false) ?? ThingDefOf.StandingLamp;
            if (name == "SteelShelf")
            {
                ThingDef shelf = DefDatabase<ThingDef>.GetNamed("SteelShelf", false);
                if (shelf != null) return shelf;
                return DefDatabase<ThingDef>.GetNamed("Shelf", false);
            }
            if (name == "Table")
                return ThingDefOf.Table1x2c;
            if (name == "Chair")
                return ThingDefOf.DiningChair;
            PrebuiltBaseDebug.Warning("Unknown furniture: " + name);
            return null;
        }

        private void PlaceRoomItems()
        {
            PrebuiltBaseDebug.Log("Placing items...");

            foreach (string itemStr in PrebuiltBaseConfig.RoomItems)
            {
                try
                {
                    string thingDefName;
                    int count;
                    string stuffDefName;
                    IntVec3 pos;

                    if (!TryParseItem(itemStr, false, out thingDefName, out count, out stuffDefName, out pos))
                        continue;

                    ThingDef thingDef = DefDatabase<ThingDef>.GetNamed(thingDefName, false);
                    if (thingDef == null)
                    {
                        PrebuiltBaseDebug.Warning("Item def not found: " + thingDefName);
                        continue;
                    }

                    ThingDef stuffDef = null;
                    if (!string.IsNullOrEmpty(stuffDefName))
                        stuffDef = DefDatabase<ThingDef>.GetNamed(stuffDefName, false);
                    else
                        stuffDef = GetDefaultStuff(thingDef);

                    Thing item;
                    if (stuffDef != null)
                        item = ThingMaker.MakeThing(thingDef, stuffDef);
                    else
                        item = ThingMaker.MakeThing(thingDef);

                    if (count > 1 && item.def.stackLimit > 1)
                        item.stackCount = Math.Min(count, item.def.stackLimit);

                    GenSpawn.Spawn(item, pos, map);
                    PrebuiltBaseDebug.Log("Item " + thingDefName + " x" + count + " at " + pos);
                }
                catch (Exception ex)
                {
                    PrebuiltBaseDebug.Warning("Item error " + itemStr + ": " + ex.Message);
                }
            }
        }

        private void BuildPowerRoom()
        {
            try
            {
                PrebuiltBaseDebug.Log("Building power room...");
                BuildFloorForRoom(powerRoomStart, PrebuiltBaseConfig.PowerRoomWidth, PrebuiltBaseConfig.PowerRoomHeight);
                BuildPowerRoomWallsAndDoor();
                BuildRoofForRoom(powerRoomStart, PrebuiltBaseConfig.PowerRoomWidth, PrebuiltBaseConfig.PowerRoomHeight);
                PlacePowerRoomFurniture();
                PlacePowerRoomItems();
                PrebuiltBaseDebug.Log("Power room built");
            }
            catch (Exception ex)
            {
                PrebuiltBaseDebug.Warning("Power room error: " + ex.Message);
            }
        }

        private void BuildFloorForRoom(IntVec3 start, int width, int height)
        {
            TerrainDef floorDef = GetFloorDef();
            if (floorDef == null) return;

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    IntVec3 pos = new IntVec3(start.x + x, 0, start.z + z);
                    if (pos.InBounds(map))
                        map.terrainGrid.SetTerrain(pos, floorDef);
                }
            }
        }

        private void BuildRoofForRoom(IntVec3 start, int width, int height)
        {
            RoofDef roofDef = GetDefOrFallback<RoofDef>(PrebuiltBaseConfig.RoofDef,
                PrebuiltBaseConfig.FallbackRoofDef, RoofDefOf.RoofRockThick);

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    IntVec3 pos = new IntVec3(start.x + x, 0, start.z + z);
                    if (pos.InBounds(map))
                        map.roofGrid.SetRoof(pos, roofDef);
                }
            }
        }

        private void BuildPowerRoomWallsAndDoor()
        {
            ThingDef wallDef = GetDefOrFallback<ThingDef>(PrebuiltBaseConfig.WallDef,
                PrebuiltBaseConfig.FallbackWallDef, ThingDefOf.Wall);
            ThingDef doorDef = GetDefOrFallback<ThingDef>(PrebuiltBaseConfig.PowerRoomDoorDef,
                PrebuiltBaseConfig.FallbackDoorDef, ThingDefOf.Door);
            ThingDef doorStuff = GetStuffDef(PrebuiltBaseConfig.PowerRoomDoorStuffDef, GetPlateSteelDef());
            ThingDef wallStuff = GetStuffDef(PrebuiltBaseConfig.WallStuffDef, GetPlateSteelDef());

            List<string> doorPosList = PrebuiltBaseConfig.PowerRoomDoorPositions.ToList();
            int roomSize = (PrebuiltBaseConfig.PowerRoomDoorSide == "west" || PrebuiltBaseConfig.PowerRoomDoorSide == "east")
                ? PrebuiltBaseConfig.PowerRoomHeight
                : PrebuiltBaseConfig.PowerRoomWidth;

            HashSet<int> doorPositions = ParseDoorPositions(doorPosList, roomSize);

            for (int x = 0; x < PrebuiltBaseConfig.PowerRoomWidth; x++)
            {
                for (int z = 0; z < PrebuiltBaseConfig.PowerRoomHeight; z++)
                {
                    bool isEdge = x == 0 || x == PrebuiltBaseConfig.PowerRoomWidth - 1 ||
                                  z == 0 || z == PrebuiltBaseConfig.PowerRoomHeight - 1;

                    if (!isEdge) continue;

                    IntVec3 pos = new IntVec3(powerRoomStart.x + x, 0, powerRoomStart.z + z);
                    if (!pos.InBounds(map)) continue;

                    bool isDoor = x == 0 && doorPositions.Contains(z);

                    if (isDoor)
                        PlaceDoorSimple(pos, doorDef, doorStuff, false);
                    else
                        PlaceWallSimple(pos, wallDef, wallStuff);
                }
            }
        }

        private void PlacePowerRoomFurniture()
        {
            foreach (string buildingStr in PrebuiltBaseConfig.PowerRoomBuildings)
            {
                try
                {
                    string thingDefName;
                    IntVec3 pos;
                    Rot4 rotation;

                    if (!TryParseBuilding(buildingStr, true, out thingDefName, out pos, out rotation))
                        continue;

                    if (!pos.InBounds(map)) continue;

                    ThingDef thingDef = GetPowerRoomThingDef(thingDefName);
                    if (thingDef == null)
                    {
                        PrebuiltBaseDebug.Warning("Power room def not found: " + thingDefName);
                        continue;
                    }

                    ThingDef stuffDef = GetPowerRoomStuff(thingDef, thingDefName);
                    Thing building;

                    if (stuffDef != null)
                        building = ThingMaker.MakeThing(thingDef, stuffDef);
                    else
                        building = ThingMaker.MakeThing(thingDef);

                    building.SetFaction(Faction.OfPlayer);

                    if (rotation != Rot4.Invalid && building is ThingWithComps twc)
                        twc.Rotation = rotation;

                    GenSpawn.Spawn(building, pos, map);
                    PrebuiltBaseDebug.Log("Power furniture " + thingDefName + " at " + pos);
                }
                catch (Exception ex)
                {
                    PrebuiltBaseDebug.Warning("Power furniture error " + buildingStr + ": " + ex.Message);
                }
            }
        }

        private ThingDef GetPowerRoomThingDef(string name)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamed(name, false);
            if (def != null) return def;

            if (name == "VaultTecPortableGenerator")
            {
                ThingDef gen = DefDatabase<ThingDef>.GetNamed("ChemfuelPoweredGenerator", false);
                if (gen != null) return gen;
                return DefDatabase<ThingDef>.GetNamed("Generator", false);
            }
            if (name == "VaultBattery")
                return ThingDefOf.Battery;
            if (name == "PowerConduit")
                return ThingDefOf.PowerConduit;

            PrebuiltBaseDebug.Warning("Unknown power room def: " + name);
            return null;
        }

        private void PlacePowerRoomItems()
        {
            foreach (string itemStr in PrebuiltBaseConfig.PowerRoomItems)
            {
                try
                {
                    string thingDefName;
                    int count;
                    string stuffDefName;
                    IntVec3 pos;

                    if (!TryParseItem(itemStr, true, out thingDefName, out count, out stuffDefName, out pos))
                        continue;

                    if (!pos.InBounds(map)) continue;

                    ThingDef thingDef = DefDatabase<ThingDef>.GetNamed(thingDefName, false);
                    if (thingDef == null) continue;

                    ThingDef stuffDef = null;
                    if (!string.IsNullOrEmpty(stuffDefName))
                        stuffDef = DefDatabase<ThingDef>.GetNamed(stuffDefName, false);
                    else
                        stuffDef = GetDefaultStuff(thingDef);

                    Thing item;
                    if (stuffDef != null)
                        item = ThingMaker.MakeThing(thingDef, stuffDef);
                    else
                        item = ThingMaker.MakeThing(thingDef);

                    if (count > 1 && item.def.stackLimit > 1)
                        item.stackCount = Math.Min(count, item.def.stackLimit);

                    GenSpawn.Spawn(item, pos, map);
                    PrebuiltBaseDebug.Log("Power item " + thingDefName + " x" + count + " at " + pos);
                }
                catch (Exception ex)
                {
                    PrebuiltBaseDebug.Warning("Power item error " + itemStr + ": " + ex.Message);
                }
            }
        }

        private ThingDef GetPowerRoomStuff(ThingDef thingDef, string thingDefName)
        {
            if (!thingDef.MadeFromStuff) return null;

            if (thingDefName == "VaultTecPortableGenerator" ||
                thingDefName == "ChemfuelPoweredGenerator" ||
                thingDefName == "VaultBattery")
                return GetPlateSteelDef();

            if (thingDefName == "PowerConduit")
            {
                ThingDef copper = DefDatabase<ThingDef>.GetNamed("Copper", false);
                return copper ?? GetPlateSteelDef();
            }

            return GetPlateSteelDef();
        }

        private void ClearAreaAround()
        {
            int minX = Math.Min(roomStart.x, powerRoomStart.x) - PrebuiltBaseConfig.ClearMargin;
            int minZ = Math.Min(roomStart.z, powerRoomStart.z) - PrebuiltBaseConfig.ClearMargin;
            int maxX = Math.Max(roomStart.x + PrebuiltBaseConfig.RoomWidth,
                powerRoomStart.x + PrebuiltBaseConfig.PowerRoomWidth) + PrebuiltBaseConfig.ClearMargin;
            int maxZ = Math.Max(roomStart.z + PrebuiltBaseConfig.RoomHeight,
                powerRoomStart.z + PrebuiltBaseConfig.PowerRoomHeight) + PrebuiltBaseConfig.ClearMargin;

            int cleared = 0;
            for (int x = minX; x < maxX; x++)
            {
                for (int z = minZ; z < maxZ; z++)
                {
                    IntVec3 pos = new IntVec3(x, 0, z);
                    if (!pos.InBounds(map)) continue;

                    bool insideMain = x >= roomStart.x && x < roomStart.x + PrebuiltBaseConfig.RoomWidth &&
                                     z >= roomStart.z && z < roomStart.z + PrebuiltBaseConfig.RoomHeight;
                    bool insidePower = x >= powerRoomStart.x && x < powerRoomStart.x + PrebuiltBaseConfig.PowerRoomWidth &&
                                      z >= powerRoomStart.z && z < powerRoomStart.z + PrebuiltBaseConfig.PowerRoomHeight;

                    if (!insideMain && !insidePower)
                        cleared += ClearCell(pos, PrebuiltBaseConfig.ClearPlants, PrebuiltBaseConfig.ClearFilth);
                }
            }

            PrebuiltBaseDebug.Log("Cleared " + cleared + " objects around rooms");
        }

        private bool TryParseBuilding(string str, bool isPowerRoom, out string thingDefName, out IntVec3 pos, out Rot4 rotation)
        {
            thingDefName = null;
            pos = IntVec3.Invalid;
            rotation = Rot4.Invalid;

            string[] parts = str.Split('@');
            if (parts.Length != 2) return false;

            thingDefName = parts[0];
            string[] coords = parts[1].Split(',');
            if (coords.Length < 2) return false;

            int x, z;
            if (!int.TryParse(coords[0], out x) || !int.TryParse(coords[1], out z))
                return false;

            if (isPowerRoom)
            {
                if (x < 0 || x >= PrebuiltBaseConfig.PowerRoomWidth || z < 0 || z >= PrebuiltBaseConfig.PowerRoomHeight)
                    return false;
                pos = new IntVec3(powerRoomStart.x + x, 0, powerRoomStart.z + z);
            }
            else
            {
                if (x < 0 || x >= PrebuiltBaseConfig.RoomWidth || z < 0 || z >= PrebuiltBaseConfig.RoomHeight)
                    return false;
                pos = new IntVec3(roomStart.x + x, 0, roomStart.z + z);
            }

            if (!pos.InBounds(map)) return false;

            if (coords.Length >= 3)
                rotation = ParseRotation(coords[2]);

            return true;
        }

        private bool TryParseItem(string str, bool isPowerRoom, out string thingDefName, out int count, out string stuffDefName, out IntVec3 pos)
        {
            thingDefName = null;
            count = 0;
            stuffDefName = null;
            pos = IntVec3.Invalid;

            string[] parts = str.Split('@');
            if (parts.Length != 2) return false;

            string[] itemParts = parts[0].Split(':');
            if (itemParts.Length < 2) return false;

            thingDefName = itemParts[0];
            if (!int.TryParse(itemParts[1], out count)) return false;

            stuffDefName = itemParts.Length >= 3 ? itemParts[2] : null;

            string[] coords = parts[1].Split(',');
            if (coords.Length < 2) return false;

            int x, z;
            if (!int.TryParse(coords[0], out x) || !int.TryParse(coords[1], out z))
                return false;

            if (isPowerRoom)
            {
                if (x < 0 || x >= PrebuiltBaseConfig.PowerRoomWidth || z < 0 || z >= PrebuiltBaseConfig.PowerRoomHeight)
                    return false;
                pos = new IntVec3(powerRoomStart.x + x, 0, powerRoomStart.z + z);
            }
            else
            {
                if (x < 0 || x >= PrebuiltBaseConfig.RoomWidth || z < 0 || z >= PrebuiltBaseConfig.RoomHeight)
                    return false;
                pos = new IntVec3(roomStart.x + x, 0, roomStart.z + z);
            }

            return pos.InBounds(map);
        }

        private HashSet<int> ParseDoorPositions(List<string> strings, int roomSize)
        {
            HashSet<int> positions = new HashSet<int>();
            if (strings == null) return positions;

            foreach (string s in strings)
            {
                int x;
                if (int.TryParse(s.Trim(), out x) && x >= 0 && x < roomSize)
                    positions.Add(x);
            }
            return positions;
        }

        private T GetDefOrFallback<T>(string defName, string fallbackName, T defaultDef) where T : Def
        {
            T def = DefDatabase<T>.GetNamed(defName, false);
            if (def != null) return def;

            if (!string.IsNullOrEmpty(fallbackName) && fallbackName != defName)
            {
                def = DefDatabase<T>.GetNamed(fallbackName, false);
                if (def != null) return def;
            }

            return defaultDef;
        }

        private ThingDef GetStuffDef(string stuffName, ThingDef defaultStuff)
        {
            if (string.IsNullOrEmpty(stuffName)) return null;
            return DefDatabase<ThingDef>.GetNamed(stuffName, false) ?? defaultStuff;
        }

        private ThingDef GetPlateSteelDef()
        {
            return DefDatabase<ThingDef>.GetNamed("PlateSteel", false) ?? ThingDefOf.Steel;
        }

        private TerrainDef GetFloorDef()
        {
            TerrainDef def = DefDatabase<TerrainDef>.GetNamed(PrebuiltBaseConfig.FloorDef, false);
            if (def != null) return def;

            foreach (string fallback in PrebuiltBaseConfig.FallbackFloorDefs)
            {
                def = DefDatabase<TerrainDef>.GetNamed(fallback, false);
                if (def != null) return def;
            }

            return TerrainDefOf.Gravel;
        }

        private TerrainDef GetWallFloorDef()
        {
            TerrainDef def = DefDatabase<TerrainDef>.GetNamed(PrebuiltBaseConfig.WallFloorDef, false);
            if (def != null) return def;

            foreach (string fallback in PrebuiltBaseConfig.FallbackWallFloorDefs)
            {
                def = DefDatabase<TerrainDef>.GetNamed(fallback, false);
                if (def != null) return def;
            }

            return GetFloorDef();
        }

        private int ClearCell(IntVec3 pos, bool clearPlants, bool clearFilth)
        {
            if (!pos.InBounds(map)) return 0;

            List<Thing> toRemove = new List<Thing>();
            foreach (Thing thing in pos.GetThingList(map))
            {
                if ((clearPlants && thing.def.category == ThingCategory.Plant) ||
                    (clearFilth && thing.def.category == ThingCategory.Filth) ||
                    thing.def.category == ThingCategory.Item ||
                    (thing.def.mineable && thing.def.building != null && thing.def.building.isNaturalRock))
                {
                    toRemove.Add(thing);
                }
            }

            foreach (Thing thing in toRemove)
                thing.Destroy(DestroyMode.Vanish);

            return toRemove.Count;
        }

        private ThingDef GetRoomStuff(ThingDef thingDef, string thingDefName)
        {
            if (!thingDef.MadeFromStuff) return null;
            return GetPlateSteelDef();
        }

        private ThingDef GetDefaultStuff(ThingDef thingDef)
        {
            if (!thingDef.MadeFromStuff) return null;
            return GetPlateSteelDef();
        }

        private Rot4 ParseRotation(string rotStr)
        {
            string lower = rotStr.ToLower();
            if (lower == "north" || lower == "0")
                return Rot4.North;
            if (lower == "east" || lower == "1")
                return Rot4.East;
            if (lower == "south" || lower == "2")
                return Rot4.South;
            if (lower == "west" || lower == "3")
                return Rot4.West;

            return Rot4.Invalid;
        }
    }
    #endregion

    #region Location Finder
    public static class LocationFinder
    {
        public static IntVec3 FindGoodLocation(Map map)
        {
            return new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
        }
    }
    #endregion

    #region Mod Initialization
    [StaticConstructorOnStartup]
    public static class PrebuiltBaseModInitializer
    {
        static PrebuiltBaseModInitializer()
        {
            PrebuiltBaseDebug.Log("========================================");
            PrebuiltBaseDebug.Log("STATIC CONSTRUCTOR CALLED");
            PrebuiltBaseDebug.Log("========================================");

            try
            {
                // Находим метод для патчинга
                MethodInfo targetMethod = FindTargetMethod();

                if (targetMethod == null)
                {
                    PrebuiltBaseDebug.Error("Could not find any target method to patch!");
                    return;
                }

                PrebuiltBaseDebug.Log("Found target method: " + targetMethod.DeclaringType.Name + "." + targetMethod.Name);

                // Создаем Harmony instance
                var harmony = new Harmony("com.watcher.comps.prebuiltbase");

                // Получаем наш Postfix метод
                MethodInfo postfixMethod = typeof(PrebuiltBaseModInitializer).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);

                if (postfixMethod == null)
                {
                    PrebuiltBaseDebug.Error("Could not find Postfix method!");
                    return;
                }

                // Патчим вручную
                harmony.Patch(targetMethod, null, new HarmonyMethod(postfixMethod));

                PrebuiltBaseDebug.Log("Harmony patch applied successfully");
            }
            catch (Exception ex)
            {
                PrebuiltBaseDebug.Error("Harmony initialization failed: " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        private static MethodInfo FindTargetMethod()
        {
            // Пробуем Game.InitNewGame
            var method = typeof(Game).GetMethod("InitNewGame", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (method != null)
            {
                PrebuiltBaseDebug.Log("Found Game.InitNewGame");
                return method;
            }

            // Альтернатива: MapGenerator.GenerateMap
            method = typeof(MapGenerator).GetMethod("GenerateMap", new[] { typeof(IntVec3), typeof(MapParent), typeof(MapGeneratorDef), typeof(IEnumerable<GenStepWithParams>), typeof(Action<Map>) });
            if (method != null)
            {
                PrebuiltBaseDebug.Log("Found MapGenerator.GenerateMap");
                return method;
            }

            // Еще альтернатива: Game.Start
            method = typeof(Game).GetMethod("Start", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                PrebuiltBaseDebug.Log("Found Game.Start");
                return method;
            }

            // Последняя альтернатива: Map.FinalizeInit
            method = typeof(Map).GetMethod("FinalizeInit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                PrebuiltBaseDebug.Log("Found Map.FinalizeInit");
                return method;
            }

            return null;
        }

        // Этот метод будет вызван как Postfix
        private static void Postfix()
        {
            PrebuiltBaseDebug.Log("========================================");
            PrebuiltBaseDebug.Log("POSTFIX CALLED");
            PrebuiltBaseDebug.Log("========================================");

            if (Current.Game == null)
            {
                PrebuiltBaseDebug.Error("Current.Game is NULL");
                return;
            }

            if (Current.Game.CurrentMap == null)
            {
                PrebuiltBaseDebug.Error("Current.Game.CurrentMap is NULL");
                return;
            }

            if (!IsCorrectScenario())
            {
                PrebuiltBaseDebug.Log("Scenario check failed, skipping base creation");
                return;
            }

            PrebuiltBaseDebug.Log("Scenario OK, starting base creation");

            LongEventHandler.QueueLongEvent(() =>
            {
                try
                {
                    IntVec3 center = LocationFinder.FindGoodLocation(Current.Game.CurrentMap);
                    PrebuiltBaseDebug.Log("Build center: " + center);
                    RoomBuilder builder = new RoomBuilder(Current.Game.CurrentMap, center);
                    builder.BuildRoom();
                }
                catch (Exception ex)
                {
                    PrebuiltBaseDebug.Error("LongEvent error: " + ex.Message + "\n" + ex.StackTrace);
                }
            }, "SetupPrebuiltBase", false, null);
        }

        private static bool IsCorrectScenario()
        {
            PrebuiltBaseDebug.Log("=== SCENARIO CHECK (Initializer) ===");

            if (Current.Game == null)
            {
                PrebuiltBaseDebug.Error("Current.Game is NULL");
                return false;
            }

            if (Current.Game.Scenario == null)
            {
                PrebuiltBaseDebug.Error("Current.Game.Scenario is NULL");
                return false;
            }

            string currentName = Current.Game.Scenario.name ?? "NULL";
            PrebuiltBaseDebug.Log("Current scenario name: '" + currentName + "'");

            // Ищем ScenarioDef по имени сценария через все возможные способы
            ScenarioDef currentDef = FindScenarioDefByName(currentName);
            string currentDefName = currentDef?.defName ?? "NULL";
            string currentLabel = currentDef?.label ?? "NULL";

            PrebuiltBaseDebug.Log("Found ScenarioDef: defName='" + currentDefName + "', label='" + currentLabel + "'");

            // Проверка 1: Точное совпадение defName
            if (currentDefName == PrebuiltBaseConfig.RequiredScenarioDefName)
            {
                PrebuiltBaseDebug.Log("Matched by defName: " + currentDefName);
                return true;
            }

            // Проверка 2: Проверка по label
            ScenarioDef required = DefDatabase<ScenarioDef>.GetNamedSilentFail(PrebuiltBaseConfig.RequiredScenarioDefName);
            if (required != null)
            {
                string requiredLabel = required.label ?? required.defName;

                if (currentLabel == requiredLabel)
                {
                    PrebuiltBaseDebug.Log("Matched by label: " + currentLabel);
                    return true;
                }

                // Проверка label без скобок
                int idx = currentLabel.IndexOf('(');
                if (idx > 0)
                {
                    string cleanCurrentLabel = currentLabel.Substring(0, idx).Trim();
                    if (cleanCurrentLabel == requiredLabel)
                    {
                        PrebuiltBaseDebug.Log("Matched by clean label: " + cleanCurrentLabel);
                        return true;
                    }
                }
            }

            // Проверка 3: Прямое совпадение имени с RequiredScenarioDefName
            if (currentName == PrebuiltBaseConfig.RequiredScenarioDefName)
            {
                PrebuiltBaseDebug.Log("Matched by exact scenario name: " + currentName);
                return true;
            }

            // Проверка 4: Проверка по ключевым словам в defName, label или имени сценария
            foreach (string keyword in PrebuiltBaseConfig.AllowedScenarioKeywords)
            {
                if (currentDefName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    currentLabel.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    currentName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PrebuiltBaseDebug.Log("Matched by keyword '" + keyword + "' in: " + currentDefName + " / " + currentLabel + " / " + currentName);
                    return true;
                }
            }

            PrebuiltBaseDebug.Log("Scenario check FAILED - no match found");
            return false;
        }

        private static ScenarioDef FindScenarioDefByName(string scenarioName)
        {
            if (string.IsNullOrEmpty(scenarioName)) return null;

            // Убираем скобки и пробелы для сравнения
            string cleanName = scenarioName;
            int idx = scenarioName.IndexOf('(');
            if (idx > 0)
                cleanName = scenarioName.Substring(0, idx).Trim();

            PrebuiltBaseDebug.Log("Searching for ScenarioDef, clean name: '" + cleanName + "', original: '" + scenarioName + "'");

            // Сначала ищем по всем ScenarioDef напрямую
            foreach (ScenarioDef def in DefDatabase<ScenarioDef>.AllDefs)
            {
                // Точное совпадение по defName
                if (def.defName == cleanName || def.defName == scenarioName)
                {
                    PrebuiltBaseDebug.Log("Found by exact defName match: " + def.defName);
                    return def;
                }

                // Точное совпадение по label
                string defLabel = def.label ?? def.defName;
                if (defLabel == cleanName || defLabel == scenarioName)
                {
                    PrebuiltBaseDebug.Log("Found by exact label match: " + def.defName + " (label: " + defLabel + ")");
                    return def;
                }
            }

            // Если точное совпадение не найдено, ищем по частичному совпадению
            foreach (ScenarioDef def in DefDatabase<ScenarioDef>.AllDefs)
            {
                string defLabel = def.label ?? def.defName;

                // Частичное совпадение по defName (contains)
                if (def.defName.IndexOf(cleanName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cleanName.IndexOf(def.defName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PrebuiltBaseDebug.Log("Found by partial defName match: " + def.defName);
                    return def;
                }

                // Частичное совпадение по label (contains)
                if (defLabel.IndexOf(cleanName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cleanName.IndexOf(defLabel, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    PrebuiltBaseDebug.Log("Found by partial label match: " + def.defName + " (label: " + defLabel + ")");
                    return def;
                }
            }

            PrebuiltBaseDebug.Warning("ScenarioDef not found for: " + scenarioName);
            return null;
        }
    }
    #endregion
}