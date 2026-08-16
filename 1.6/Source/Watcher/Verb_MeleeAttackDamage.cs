using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;

//Некоторые виды оружия ближнего боя при попадании вызывают взрыв и расходуют ядерные боеприпасы (или само оружие). Это превращает обычный удар в мини-взрыв.

namespace Watcher
{
    public class Verb_MeleeAttackDamage : Verb_MeleeAttack
    {
        protected override DamageWorker.DamageResult ApplyMeleeDamageToTarget(LocalTargetInfo target)
        {
            DamageInfo dinfo = new DamageInfo(
                def: verbProps.meleeDamageDef,
                amount: (float)verbProps.meleeDamageDef.defaultDamage, // ← правильное поле
                armorPenetration: verbProps.meleeArmorPenetrationBase,
                angle: (CasterPawn.Rotation.Opposite.AsAngle + Rand.Range(-30f, 30f)) % 360f,
                instigator: CasterPawn,
                weapon: EquipmentSource?.def,
                intendedTarget: target.Thing);

            DamageWorker.DamageResult result = target.Thing.TakeDamage(dinfo);

            if (target.Thing is Pawn pawn && !pawn.Dead && pawn.stances != null)
                pawn.stances.stagger.StaggerFor(95);

            return result;
        }

        /* ---------------------------------------------------------- */
        protected override bool TryCastShot()
        {
            Pawn caster = CasterPawn;
            if (!caster.Spawned || caster.stances.FullBodyBusy)
                return false;

            Thing target = currentTarget.Thing;
            if (!CanHitTarget(target))
                //Log.Warning($"{caster} meleed {target} from out of melee position.");

            caster.rotationTracker.Face(target.DrawPos);

            if (!IsTargetImmobile(currentTarget) && caster.skills != null)
                caster.skills.Learn(SkillDefOf.Melee,
                                    200f * verbProps.AdjustedFullCycleTime(this, caster));

            Pawn victim = target as Pawn;
            if (victim != null && !victim.Dead &&
                (caster.MentalStateDef != MentalStateDefOf.SocialFighting ||
                 victim.MentalStateDef != MentalStateDefOf.SocialFighting))
            {
                victim.mindState.meleeThreat = caster;
                victim.mindState.lastMeleeThreatHarmTick = Find.TickManager.TicksGame;
            }

            Vector3 motePos = target.DrawPos;
            bool hit = Rand.Chance(GetNonMissChance(target)) &&
                      !Rand.Chance(GetDodgeChance(target));

            if (hit)
            {
                if (verbProps.impactMote != null)
                    MoteMaker.MakeStaticMote(motePos, target.Map, verbProps.impactMote);

                Explosion();
            }
            else
            {
                MoteMaker.ThrowText(motePos, target.Map, "TextMote_Dodge".Translate(), 1.9f);
            }

            if (caster.Spawned) caster.Drawer.Notify_MeleeAttackOn(target);
            if (victim?.Spawned == true && !victim.Dead)
                victim.stances.stagger.StaggerFor(95);

            if (caster.Spawned) caster.rotationTracker.FaceCell(target.Position);
            caster.caller?.Notify_DidMeleeAttack();

            return hit;
        }

        /* ---------------------------------------------------------- */
        private void Explosion()
        {
            GenExplosion.DoExplosion(
                currentTarget.Thing.Position,
                CasterPawn.Map,
                2f,
                FalloutDamageDefOf.FalloutBomb,
                EquipmentSource,
                15,
                0.6f);

            Thing ammo = CasterPawn.inventory?.innerContainer
                .FirstOrDefault(t => t?.def == ThingDefOfMYLocal.NuclearLandmine);

            if (ammo != null)
            {
                ammo.stackCount--;
                if (ammo.stackCount <= 0)
                {
                    CasterPawn.inventory.innerContainer.Remove(ammo);
                    CasterPawn.inventory.Notify_ItemRemoved(ammo);
                    ammo.Destroy();
                }
            }
            else if (!EquipmentSource.Destroyed)
                EquipmentSource.Destroy();
        }

        /* ---------------------------------------------------------- */
        private bool IsTargetImmobile(LocalTargetInfo target)
        {
            if (!(target.Thing is Pawn pawn))
                return true;
            return pawn.Downed || pawn.GetPosture() != PawnPosture.Standing;
        }

        private float GetNonMissChance(LocalTargetInfo target)
            => surpriseAttack || IsTargetImmobile(target)
                ? 1f
                : CasterPawn.GetStatValue(StatDefOf.MeleeHitChance);

        private float GetDodgeChance(LocalTargetInfo target)
        {
            if (surpriseAttack || IsTargetImmobile(target) ||
                !(target.Thing is Pawn pawn))
                return 0f;

            return pawn.GetStatValue(StatDefOf.MeleeDodgeChance);
        }
    }
}