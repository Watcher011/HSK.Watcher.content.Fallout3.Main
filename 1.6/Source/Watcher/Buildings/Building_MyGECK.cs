using RimWorld;
using SK;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Watcher.Buildings
{
    public class Building_GECK : Building
    {
        private List<CompRefuelable> fuelComps;
        private TerrainDef soilDef;
        private CompProperties_GECK geckProps;
        private bool hasActivated;

        /* ------------------------------------------------------------------ */
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            fuelComps = GetComps<CompRefuelable>().ToList();
            soilDef = TerrainDefOfLocal.SoilRich;
            geckProps = def.GetCompProperties<CompProperties_GECK>();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref hasActivated, "hasActivated", false);
        }

        /* >>> ВАЖНО: protected override, а не public override <<< */
        protected override void Tick()
        {
            base.Tick();

            if (!hasActivated && Find.TickManager.TicksGame % 250 == 0)
                TickRare();
        }

        public override void TickRare()
        {
            if (hasActivated) return;

            // все CompRefuelable должны быть заправлены
            if (fuelComps.All(c => c.HasFuel))
                ActivateGECK();
        }

        /* ------------------------------------------------------------------ */
        private void ActivateGECK()
        {
            hasActivated = true;

            CreateFertileArea();

            // расходуем всё топливо
            foreach (var fc in fuelComps)
                if (fc.HasFuel) fc.ConsumeFuel(fc.Fuel);

            // эффект
            if (geckProps?.activationEffect != null)
            {
                var eff = geckProps.activationEffect.Spawn();
                eff.Trigger(new TargetInfo(Position, Map), new TargetInfo(Position, Map));
                eff.Cleanup();
            }

            Destroy(DestroyMode.Vanish);

            // сообщение
            if (!geckProps?.activationMessage.NullOrEmpty() ?? false)
                Messages.Message(geckProps.activationMessage, MessageTypeDefOf.PositiveEvent);
        }

        /* ------------------------------------------------------------------ */
        private void CreateFertileArea()
        {
            int radius = geckProps?.radius ?? 30;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(Position, radius, useCenter: true))
            {
                if (!c.InBounds(Map)) continue;

                TerrainDef terr = c.GetTerrain(Map);
                if (terr.fertility < soilDef.fertility && terr.changeable)
                    Map.terrainGrid.SetTerrain(c, soilDef);
            }
        }
    }

    /* ====================================================================== */
    public class CompProperties_GECK : CompProperties
    {
        public int radius = 30;
        public string activationMessage;
        public EffecterDef activationEffect;

        public CompProperties_GECK() => compClass = typeof(CompGECK);
    }

    public class CompGECK : ThingComp
    {
        public CompProperties_GECK Props => (CompProperties_GECK)props;
    }
}