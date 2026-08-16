using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using UnityEngine;

namespace Watcher.Comps
{
    public class CompProperties_DirectionalExplosive : CompProperties
    {
        public float explosiveRadius = 3f;
        public float damageAmountBase = 100f;
        public DamageDef damageDef;
        public float armorPenetrationSharp = 0f;
        public float armorPenetrationBlunt = 0f;
        public float angle = 60f;
        public int wickTicks = 15;
        public List<DamageDef> startWickOnDamageTaken;

        public bool hasMotionSensor = false;
        public float sensorRange = 3f;
        public int sensorScanInterval = 60;
        public bool sensorOnlyHostile = true;
        public bool sensorOnlyPawns = true;

        public SoundDef explosionSound;

        public CompProperties_DirectionalExplosive()
        {
            compClass = typeof(CompDirectionalExplosive);
        }
    }

    public class CompDirectionalExplosive : ThingComp
    {
        private int wickTicksLeft = -1;
        private bool wickStarted = false;
        private int sensorTickCounter = 0;
        private static readonly Color MineColor = new Color(1f, 0.2f, 0.2f, 0.3f);

        public CompProperties_DirectionalExplosive Props => (CompProperties_DirectionalExplosive)props;

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref wickTicksLeft, "wickTicksLeft", -1);
            Scribe_Values.Look(ref wickStarted, "wickStarted", false);
            Scribe_Values.Look(ref sensorTickCounter, "sensorTickCounter", 0);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (wickStarted && wickTicksLeft > 0)
            {
                wickTicksLeft--;
                if (wickTicksLeft <= 0) Detonate();
            }
            if (!wickStarted && Props.hasMotionSensor)
            {
                sensorTickCounter++;
                if (sensorTickCounter >= Props.sensorScanInterval)
                {
                    sensorTickCounter = 0;
                    CheckMotionSensor();
                }
            }
        }

        public void CheckMotionSensor()
        {
            if (parent.Map == null) return;
            var pawnsInRange = parent.Map.mapPawns.AllPawnsSpawned.Where(p =>
                p.Position.InHorDistOf(parent.Position, Props.sensorRange) &&
                p != parent && p.Awake() && !p.Dead && !p.Downed
            ).ToList();
            foreach (Pawn pawn in pawnsInRange)
            {
                if (ShouldTriggerOn(pawn))
                {
                    StartWick();
                    break;
                }
            }
        }

        private bool ShouldTriggerOn(Pawn pawn)
        {
            bool isHostile = pawn.HostileTo(parent.Faction);
            if (Props.sensorOnlyHostile && !isHostile) return false;
            if (Props.sensorOnlyPawns && !isHostile && !pawn.RaceProps.Humanlike) return false;
            if (!GenSight.LineOfSight(parent.Position, pawn.Position, parent.Map)) return false;
            return true;
        }

        public void StartWick()
        {
            if (wickStarted) return;
            wickStarted = true;
            wickTicksLeft = Props.wickTicks;
            if (Props.hasMotionSensor)
                MoteMaker.ThrowText(parent.DrawPos, parent.Map, "Обнаружено!", Color.red);
        }

        public void Detonate()
        {
            if (parent.Destroyed) return;
            float baseAngle = parent.Rotation.AsAngle;

            // Направленный урон по сектору
            var targets = parent.Map.listerThings.AllThings.Where(t =>
                t.Position.InHorDistOf(parent.Position, Props.explosiveRadius) &&
                t != parent && (t is Pawn || t.def.useHitPoints)
            ).ToList();

            foreach (Thing target in targets)
            {
                if (IsInSector(target.Position, baseAngle))
                {
                    ApplyDamage(target);
                }
            }

            // Взрыв для эффекта
            SoundDef sound = Props.explosionSound ?? SoundDef.Named("Bomb_Explode") ?? SoundDefOf.Thunder_OnMap;
            GenExplosion.DoExplosion(
                parent.Position,
                parent.Map,
                Mathf.RoundToInt(Props.explosiveRadius * 0.3f),
                Props.damageDef,
                parent,
                Mathf.RoundToInt(Props.damageAmountBase * 0.3f),
                ignoredThings: null,
                explosionSound: sound
            );

            parent.Destroy(DestroyMode.KillFinalize);
        }

        private bool IsInSector(IntVec3 targetPos, float baseAngle)
        {
            Vector3 toTarget = (targetPos - parent.Position).ToVector3();
            float targetAngle = Vector3.SignedAngle(Vector3.forward, toTarget, Vector3.up);
            float minAngle = baseAngle - Props.angle / 2f;
            float maxAngle = baseAngle + Props.angle / 2f;
            float normTarget = NormalizeAngle(targetAngle);
            float normMin = NormalizeAngle(minAngle);
            float normMax = NormalizeAngle(maxAngle);
            return normMin <= normMax
                ? (normTarget >= normMin && normTarget <= normMax)
                : (normTarget >= normMin || normTarget <= normMax);
        }

        private float NormalizeAngle(float angle)
        {
            while (angle < 0) angle += 360f;
            while (angle >= 360f) angle -= 360f;
            return angle;
        }

        private void ApplyDamage(Thing target)
        {
            if (target == null || target.Destroyed) return;
            float dist = target.Position.DistanceTo(parent.Position);
            float distFactor = Mathf.Clamp01(1f - (dist / Props.explosiveRadius));
            float damage = Props.damageAmountBase * distFactor;

            DamageInfo dinfo = new DamageInfo(
                Props.damageDef,
                damage,
                Props.armorPenetrationSharp,
                Props.armorPenetrationBlunt,
                parent,
                null,
                parent.def,
                DamageInfo.SourceCategory.ThingOrUnknown
            );
            target.TakeDamage(dinfo);

            if (target is Pawn pawn && !pawn.Dead && !pawn.Destroyed)
            {
                IntVec3 pushDir = (target.Position - parent.Position);
                if (pushDir != IntVec3.Zero)
                {
                    IntVec3 newPos = target.Position + pushDir;
                    if (newPos.InBounds(parent.Map) && newPos.Walkable(parent.Map))
                    {
                        pawn.Position = newPos;
                        pawn.Notify_Teleported();
                    }
                }
            }
        }

        public override void PostDraw()
        {
            base.PostDraw();
            if (Find.Selector.IsSelected(parent))
                DrawSector();
        }

        private void DrawSector()
        {
            float baseAngle = parent.Rotation.AsAngle;
            float halfAngle = Props.angle / 2f;
            Vector3 center = parent.DrawPos;
            List<Vector3> verts = new List<Vector3> { center };

            for (int i = 0; i <= 20; i++)
            {
                float angle = baseAngle - halfAngle + (Props.angle * i / 20f);
                float rad = angle * Mathf.Deg2Rad;
                verts.Add(center + new Vector3(
                    Mathf.Sin(rad) * Props.explosiveRadius,
                    0f,
                    Mathf.Cos(rad) * Props.explosiveRadius
                ));
            }

            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.TRIANGLES);
            GL.Color(MineColor);
            for (int i = 1; i < verts.Count - 1; i++)
            {
                GL.Vertex(verts[0]);
                GL.Vertex(verts[i]);
                GL.Vertex(verts[i + 1]);
            }
            GL.End();
            GL.PopMatrix();
        }

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);
            if (!wickStarted && Props.startWickOnDamageTaken != null)
            {
                if (Props.startWickOnDamageTaken.Contains(dinfo.Def))
                    StartWick();
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!wickStarted)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Активировать",
                    defaultDesc = "Запустить таймер взрыва вручную",
                    icon = TexCommand.DesirePower,
                    action = StartWick
                };
                if (Props.hasMotionSensor)
                {
                    yield return new Command_Action
                    {
                        defaultLabel = "Тест датчика",
                        defaultDesc = "Проверить датчик движения",
                        icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport", true) ?? BaseContent.BadTex,
                        action = CheckMotionSensor
                    };
                }
            }
        }
    }

    public class PlaceWorker_DirectionalMine : PlaceWorker
    {
        public override void DrawGhost(ThingDef def, IntVec3 center, Rot4 rot, Color ghostCol, Thing thing = null)
        {
            var props = def.GetCompProperties<CompProperties_DirectionalExplosive>();
            if (props == null) return;
            List<IntVec3> sectorCells = GetSectorCells(center, rot, props.explosiveRadius, props.angle).ToList();
            GenDraw.DrawFieldEdges(sectorCells, Color.red);
        }

        private IEnumerable<IntVec3> GetSectorCells(IntVec3 center, Rot4 rot, float radius, float angle)
        {
            float baseAngle = rot.AsAngle;
            float halfAngle = angle / 2f;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, radius, true))
            {
                Vector3 toCell = (cell - center).ToVector3();
                float cellAngle = Vector3.SignedAngle(Vector3.forward, toCell, Vector3.up);
                float normCell = NormalizeAngle(cellAngle);
                float normMin = NormalizeAngle(baseAngle - halfAngle);
                float normMax = NormalizeAngle(baseAngle + halfAngle);
                bool inSector = (normMin <= normMax)
                    ? (normCell >= normMin && normCell <= normMax)
                    : (normCell >= normMin || normCell <= normMax);
                if (inSector) yield return cell;
            }
        }

        private float NormalizeAngle(float angle)
        {
            while (angle < 0) angle += 360f;
            while (angle >= 360f) angle -= 360f;
            return angle;
        }
    }
}