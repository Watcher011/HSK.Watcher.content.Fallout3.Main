using System;
using RimWorld;
using Verse;
using UnityEngine;

namespace Watcher.Comps
{
    public class CompProperties_GlowingPawn : CompProperties
    {
        public float lightRange = 3f;
        public string lightColorString = "(255, 255, 255, 1)";

        public CompProperties_GlowingPawn()
        {
            compClass = typeof(CompGlowingPawn);
        }

        public ColorInt GetColorInt()
        {
            if (!string.IsNullOrEmpty(lightColorString))
            {
                try
                {
                    string clean = lightColorString.Trim('(', ')');
                    string[] parts = clean.Split(',');
                    if (parts.Length >= 3)
                    {
                        int r = int.Parse(parts[0].Trim());
                        int g = int.Parse(parts[1].Trim());
                        int b = int.Parse(parts[2].Trim());
                        int a = parts.Length > 3 ? int.Parse(parts[3].Trim()) : 255;
                        return new ColorInt(r, g, b, a);
                    }
                }
                catch { }
            }
            return new ColorInt(255, 255, 255, 255);
        }
    }

    public class DefModExtension_Glow : DefModExtension
    {
        public bool canGlow = true;
    }

    public class CompGlowingPawn : ThingComp
    {
        private bool initialized = false;
        private int tickCounter = 0;
        private const int UPDATE_INTERVAL = 5;
        private CompGlower currentGlower = null;
        private bool cleanupDone = false;

        public CompProperties_GlowingPawn Props => (CompProperties_GlowingPawn)props;

        public override void CompTick()
        {
            base.CompTick();

            if (cleanupDone)
                return;

            if (initialized)
            {
                bool isDead = parent is Pawn pawn && pawn.Dead;
                bool isDespawned = !parent.Spawned || parent.Map == null;

                if (isDead || isDespawned)
                {
                    DoCleanup();
                    return;
                }
            }

            if (!initialized && parent.Spawned && parent.Map != null)
            {
                bool canGlow = true;
                if (parent.def.HasModExtension<DefModExtension_Glow>())
                {
                    var ext = parent.def.GetModExtension<DefModExtension_Glow>();
                    canGlow = ext.canGlow;
                }

                if (!canGlow)
                    return;

                var existing = parent.TryGetComp<CompGlower>();
                if (existing != null)
                {
                    currentGlower = existing;
                    initialized = true;
                    return;
                }

                CreateGlower();
                initialized = true;
                return;
            }

            if (!initialized || !parent.Spawned)
                return;

            tickCounter++;
            if (tickCounter >= UPDATE_INTERVAL)
            {
                tickCounter = 0;
                UpdatePosition();
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            DoCleanup(previousMap);
            base.PostDestroy(mode, previousMap);
        }

        public override void Notify_Killed(Map map, DamageInfo? dinfo)
        {
            base.Notify_Killed(map, dinfo);
            DoCleanup(map);
        }

        private void CreateGlower()
        {
            if (parent.Map == null || cleanupDone)
                return;

            RemoveGlower();

            var glowerProps = new CompProperties_Glower();
            glowerProps.glowRadius = Props.lightRange;
            glowerProps.glowColor = Props.GetColorInt();

            currentGlower = new CompGlower();
            currentGlower.parent = parent;
            currentGlower.Initialize(glowerProps);

            parent.AllComps.Add(currentGlower);

            try
            {
                parent.Map.glowGrid.RegisterGlower(currentGlower);
            }
            catch { }
        }

        private void UpdatePosition()
        {
            if (currentGlower == null || parent.Map == null || cleanupDone)
                return;

            try
            {
                parent.Map.glowGrid.DeRegisterGlower(currentGlower);
                parent.Map.glowGrid.RegisterGlower(currentGlower);
            }
            catch { }
        }

        private void DoCleanup()
        {
            DoCleanup(parent.Map);
        }

        private void DoCleanup(Map map)
        {
            if (cleanupDone)
                return;

            cleanupDone = true;
            initialized = false;

            if (currentGlower != null)
            {
                try
                {
                    if (map != null)
                        map.glowGrid.DeRegisterGlower(currentGlower);
                }
                catch { }

                try
                {
                    parent.AllComps.Remove(currentGlower);
                }
                catch { }

                currentGlower = null;
            }

            var other = parent.TryGetComp<CompGlower>();
            if (other != null)
            {
                try
                {
                    if (map != null)
                        map.glowGrid.DeRegisterGlower(other);
                }
                catch { }

                try
                {
                    parent.AllComps.Remove(other);
                }
                catch { }
            }
        }

        private void RemoveGlower()
        {
            if (currentGlower != null)
            {
                try
                {
                    if (parent.Map != null)
                        parent.Map.glowGrid.DeRegisterGlower(currentGlower);
                }
                catch { }

                try
                {
                    parent.AllComps.Remove(currentGlower);
                }
                catch { }

                currentGlower = null;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref initialized, "initialized", false);
            Scribe_Values.Look(ref cleanupDone, "cleanupDone", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                initialized = false;
                cleanupDone = false;
                currentGlower = null;
            }
        }
    }
}