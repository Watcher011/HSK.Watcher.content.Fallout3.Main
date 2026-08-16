using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Watcher.Hediffs
{
    ///Этот код добавляет в игру уникальную механику смерти, при которой тело превращается в плодородную землю, дерево и агрессивные растения.
    public class HediffComp_KillAtSeverity : HediffComp
    {
        public HediffCompProperties_KillAtSeverity Props => (HediffCompProperties_KillAtSeverity)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            if (parent.Severity >= Props.severityThreshold && parent.pawn != null && !parent.pawn.Dead)
            {
                parent.pawn.Kill(null, parent);
            }
        }
    }

    public class HediffCompProperties_KillAtSeverity : HediffCompProperties
    {
        public float severityThreshold = 0.95f;

        public HediffCompProperties_KillAtSeverity()
        {
            compClass = typeof(HediffComp_KillAtSeverity);
        }
    }

    /// <summary>
    /// Handles death transformation: corpse disappears, replaced by soil, tree, monsters.
    /// </summary>
    public class HediffComp_HaroldDeathTransform : HediffComp
    {
        public HediffCompProperties_HaroldDeathTransform Props => (HediffCompProperties_HaroldDeathTransform)props;

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff hediff)
        {
            base.Notify_PawnDied(dinfo, hediff);

            if (parent?.pawn?.Corpse == null)
                return;

            Pawn pawn = parent.pawn;
            Corpse corpse = pawn.Corpse;
            Map map = corpse.Map;

            if (map == null)
                return;

            IntVec3 center = corpse.Position;
            corpse.Destroy(DestroyMode.Vanish);
            ClearCellForPlanting(map, center);
            SpawnFertileSoil(map, center);
            SpawnHaroldTree(map, center);
            SpawnSporePlants(map, center);

            if (pawn.Faction == Faction.OfPlayer)
            {
                Find.LetterStack.ReceiveLetter(
                    "HaroldTransformationLabel".Translate(pawn.Name.ToStringShort),
                    "HaroldTransformationText".Translate(pawn.Name.ToStringShort),
                    LetterDefOf.NegativeEvent,
                    new TargetInfo(center, map));
            }
        }

        private static void ClearCellForPlanting(Map map, IntVec3 cell)
        {
            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                Thing thing = things[i];
                if (thing != null && !thing.Destroyed)
                    thing.Destroy(DestroyMode.Vanish);
            }

            FilthMaker.RemoveAllFilth(cell, map);
            map.snowGrid.SetDepth(cell, 0f);

            TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
            if (terrain == null || terrain.fertility <= 0 || !terrain.affordances.Contains(TerrainAffordanceDefOf.Light))
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
        }

        private void SpawnFertileSoil(Map map, IntVec3 center)
        {
            TerrainDef richSoil = TerrainDefOf.SoilRich ?? TerrainDefOf.Soil;
            int soilCount = 0;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, Props.soilRadius, true))
            {
                if (soilCount >= Props.soilTileCount)
                    break;

                if (!cell.InBounds(map))
                    continue;

                TerrainDef currentTerrain = map.terrainGrid.TerrainAt(cell);
                if (currentTerrain != null && IsNaturalTerrain(currentTerrain))
                {
                    map.terrainGrid.SetTerrain(cell, richSoil);
                    soilCount++;
                }
            }
        }

        private static bool IsNaturalTerrain(TerrainDef terrain)
        {
            return terrain == TerrainDefOf.Soil
                || terrain == TerrainDefOf.Sand
                || terrain == TerrainDefOf.Gravel
                || terrain == TerrainDefOf.Marsh
                || terrain == TerrainDefOf.Mud
                || terrain == TerrainDefOf.SoilRich
                || terrain.defName == "SoftSand"
                || terrain.defName == "Ice"
                || (!terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy) && terrain.fertility > 0);
        }

        private void SpawnHaroldTree(Map map, IntVec3 center)
        {
            ThingDef treeDef = ThingDef.Named(Props.treeDefName);
            if (treeDef == null)
            {
                //Log.Error("[Watcher] Could not find ThingDef for tree: " + Props.treeDefName);
                return;
            }

            if (!center.InBounds(map))
            {
                //Log.Warning("[Watcher] Center cell out of bounds for tree spawn.");
                return;
            }

            TerrainDef terrain = map.terrainGrid.TerrainAt(center);
            if (terrain == null || terrain.fertility < treeDef.plant?.fertilityMin)
                map.terrainGrid.SetTerrain(center, TerrainDefOf.SoilRich);

            Thing tree = ThingMaker.MakeThing(treeDef);
            GenSpawn.Spawn(tree, center, map);

            if (tree is Plant plant)
                plant.Growth = 1f;
        }

        private void SpawnSporePlants(Map map, IntVec3 center)
        {
            PawnKindDef sporeKind = PawnKindDef.Named(Props.sporePlantKindDef);
            if (sporeKind == null)
            {
                //Log.Error("[Watcher] Could not find PawnKindDef for: " + Props.sporePlantKindDef);
                return;
            }

            Faction hostileFaction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Insect);
            if (hostileFaction == null)
            {
                foreach (Faction f in Find.FactionManager.AllFactions)
                {
                    if (f.HostileTo(Faction.OfPlayer) && !f.defeated && !f.IsPlayer)
                    {
                        hostileFaction = f;
                        break;
                    }
                }
            }

            if (hostileFaction == null)
            {
                //Log.Warning("[Watcher] No hostile faction found for SporePlant spawn.");
                return;
            }

            int spawnCount = Rand.RangeInclusive(Props.sporePlantMinCount, Props.sporePlantMaxCount);

            for (int i = 0; i < spawnCount; i++)
            {
                if (CellFinder.TryFindRandomSpawnCellForPawnNear(center, map, out IntVec3 spawnCell, 5, c =>
                    c.Standable(map) && !c.Fogged(map) && c.GetFirstPawn(map) == null))
                {
                    Pawn sporePlant = PawnGenerator.GeneratePawn(sporeKind, hostileFaction);
                    if (sporePlant != null)
                    {
                        GenSpawn.Spawn(sporePlant, spawnCell, map);
                        sporePlant.mindState?.mentalStateHandler?.TryStartMentalState(MentalStateDefOf.Manhunter);
                    }
                }
            }
        }
    }

    public class HediffCompProperties_HaroldDeathTransform : HediffCompProperties
    {
        public int soilTileCount = 10;
        public float soilRadius = 3f;
        public string treeDefName = "Plant_TreeHarold";
        public string sporePlantKindDef = "SporePlant";
        public int sporePlantMinCount = 2;
        public int sporePlantMaxCount = 3;

        public HediffCompProperties_HaroldDeathTransform()
        {
            compClass = typeof(HediffComp_HaroldDeathTransform);
        }
    }
}