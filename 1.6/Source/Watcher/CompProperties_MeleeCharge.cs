using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using CombatExtended;
using UnityEngine;
using HarmonyLib;

//namespace Watcher.Comps
//{
//    public class CompProperties_MeleeCharge : CompProperties
//    {
//        public ThingDef fuelType;
//        public int fuelPerHit = 1;
//        public int initialCharges = 0;
//        public int maxCharges = -1;
//        public bool canReload = true;
//        public int reloadTicks = 60;
//        public int fuelPerReload = 1;
//        public float unpoweredDamageMult = 0.3f;
//        public DamageDef unpoweredDamageDef;

//        public CompProperties_MeleeCharge()
//        {
//            compClass = typeof(CompMeleeCharge);
//        }
//    }

//    public class CompMeleeCharge : ThingComp
//    {
//        public CompProperties_MeleeCharge Props => (CompProperties_MeleeCharge)props;

//        public int currentCharges;
//        public bool isReloading;
//        public int reloadProgress;

//        public int CurrentCharges => currentCharges;
//        public bool HasCharges => currentCharges > 0;
//        public bool IsReloading => isReloading;

//        public override void PostSpawnSetup(bool respawningAfterLoad)
//        {
//            base.PostSpawnSetup(respawningAfterLoad);
//            if (!respawningAfterLoad) currentCharges = Props.initialCharges;
//        }

//        public override void PostExposeData()
//        {
//            base.PostExposeData();
//            Scribe_Values.Look(ref currentCharges, "currentCharges", 0);
//            Scribe_Values.Look(ref isReloading, "isReloading", false);
//            Scribe_Values.Look(ref reloadProgress, "reloadProgress", 0);
//        }

//        public bool ConsumeCharge()
//        {
//            if (currentCharges <= 0) return false;
//            currentCharges -= Props.fuelPerHit;
//            if (currentCharges < 0) currentCharges = 0;
//            return true;
//        }

//        public void AddCharges(int amount)
//        {
//            currentCharges += amount;
//            if (Props.maxCharges > 0 && currentCharges > Props.maxCharges)
//                currentCharges = Props.maxCharges;
//        }

//        public override void CompTick()
//        {
//            base.CompTick();
//            if (isReloading)
//            {
//                reloadProgress++;
//                if (reloadProgress >= Props.reloadTicks)
//                    CompleteReload();
//            }
//        }

//        public void StartReload()
//        {
//            if (!Props.canReload || isReloading) return;
//            isReloading = true;
//            reloadProgress = 0;
//        }

//        public void CancelReload()
//        {
//            isReloading = false;
//            reloadProgress = 0;
//        }

//        private void CompleteReload()
//        {
//            isReloading = false;
//            Pawn wielder = GetWielder();
//            if (wielder != null && Props.fuelType != null)
//            {
//                Thing fuel = FindFuelInInventory(wielder);
//                if (fuel != null && fuel.stackCount >= Props.fuelPerReload)
//                {
//                    fuel.SplitOff(Props.fuelPerReload);
//                    AddCharges(1);
//                    Messages.Message($"{parent.Label} перезаряжен! Зарядов: {currentCharges}", wielder, MessageTypeDefOf.PositiveEvent);
//                }
//                else
//                {
//                    Messages.Message($"Нет {Props.fuelType.label} для перезарядки!", wielder, MessageTypeDefOf.NegativeEvent);
//                }
//            }
//        }

//        public Pawn GetWielder()
//        {
//            if (parent.ParentHolder is Pawn_EquipmentTracker equipmentTracker)
//                return equipmentTracker.pawn;
//            if (parent.ParentHolder is Pawn_InventoryTracker inventoryTracker)
//                return inventoryTracker.pawn;
//            return null;
//        }

//        private Thing FindFuelInInventory(Pawn pawn)
//        {
//            if (pawn.inventory?.innerContainer == null) return null;
//            foreach (Thing thing in pawn.inventory.innerContainer)
//                if (thing.def == Props.fuelType) return thing;
//            return null;
//        }

//        public override string CompInspectStringExtra()
//        {
//            string result = $"Заряды: {currentCharges}";
//            if (Props.maxCharges > 0) result += $"/{Props.maxCharges}";
//            if (isReloading) result += $" (Перезарядка: {(float)reloadProgress / Props.reloadTicks:P0})";
//            else if (!HasCharges) result += " [РАЗРЯЖЕНО]";
//            return result;
//        }

//        public IEnumerable<Gizmo> GetGizmos()
//        {
//            Pawn wielder = GetWielder();
//            if (wielder == null) yield break;

//            if (Props.canReload && !HasCharges && !isReloading)
//            {
//                Command_Action reloadCmd = new Command_Action
//                {
//                    defaultLabel = "Перезарядить",
//                    defaultDesc = $"Использовать {Props.fuelType?.label ?? "топливо"}",
//                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Reload", true) ?? BaseContent.BadTex,
//                    action = StartReload
//                };

//                Thing fuel = FindFuelInInventory(wielder);
//                if (fuel == null || fuel.stackCount < Props.fuelPerReload)
//                {
//                    reloadCmd.Disable($"Нет {Props.fuelType?.label ?? "топлива"}");
//                }

//                yield return reloadCmd;
//            }

//            if (isReloading)
//            {
//                yield return new Command_Action
//                {
//                    defaultLabel = "Отменить",
//                    defaultDesc = "Прервать перезарядку",
//                    icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", true) ?? BaseContent.BadTex,
//                    action = CancelReload
//                };
//            }

//            if (HasCharges)
//            {
//                yield return new Command_Action
//                {
//                    defaultLabel = $"Заряды: {currentCharges}/{Props.maxCharges}",
//                    defaultDesc = "Оружие заряжено",
//                    icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport", true) ?? BaseContent.BadTex,
//                    action = () => { }
//                };
//            }
//        }
//    }

//    [StaticConstructorOnStartup]
//    public static class MeleeChargeHarmonyPatcher
//    {
//        static MeleeChargeHarmonyPatcher()
//        {
//            var harmony = new Harmony("Watcher.MeleeCharge");

//            // Патчим GetGizmos в Pawn_EquipmentTracker
//            var getGizmosMethod = AccessTools.Method(typeof(Pawn_EquipmentTracker), "GetGizmos");
//            if (getGizmosMethod != null)
//            {
//                harmony.Patch(getGizmosMethod,
//                    postfix: new HarmonyMethod(typeof(MeleeChargeHarmonyPatcher), nameof(Pawn_EquipmentTracker_GetGizmos_Postfix)));
//            }

//            // Патчим TryCastShot
//            System.Type verbMeleeAttackCEType = FindVerbMeleeAttackCEType();
//            if (verbMeleeAttackCEType != null)
//            {
//                var tryCastShotMethod = AccessTools.Method(verbMeleeAttackCEType, "TryCastShot");
//                if (tryCastShotMethod != null)
//                {
//                    harmony.Patch(tryCastShotMethod,
//                        prefix: new HarmonyMethod(typeof(MeleeChargeHarmonyPatcher), nameof(TryCastShot_Prefix)));
//                }

//                // УБРАН ПАТЧ DamageInfosToApply - он вызывает NRE в CE
//                // Вместо этого используем подход: при разрядке урон просто уменьшается через Hediff или принимаем как есть
//            }
//        }

//        private static System.Type FindVerbMeleeAttackCEType()
//        {
//            var type = System.Type.GetType("CombatExtended.Verb_MeleeAttackCE, CombatExtended");
//            if (type != null) return type;

//            var ceAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
//                .FirstOrDefault(a => a.GetName().Name == "CombatExtended");

//            if (ceAssembly != null)
//            {
//                return ceAssembly.GetTypes()
//                    .FirstOrDefault(t => t.Name.Contains("Verb") && t.Name.Contains("Melee") && t.Name.Contains("CE"));
//            }

//            return null;
//        }

//        public static void Pawn_EquipmentTracker_GetGizmos_Postfix(Pawn_EquipmentTracker __instance, ref IEnumerable<Gizmo> __result)
//        {
//            if (__instance.pawn == null) return;

//            ThingWithComps primary = __instance.Primary;
//            if (primary == null) return;

//            var comp = primary.GetComp<CompMeleeCharge>();
//            if (comp == null) return;

//            var gizmos = __result?.ToList() ?? new List<Gizmo>();
//            gizmos.AddRange(comp.GetGizmos());
//            __result = gizmos;
//        }

//        private static readonly Dictionary<Verb, bool> chargeStates = new Dictionary<Verb, bool>();

//        public static void TryCastShot_Prefix(Verb __instance)
//        {
//            var equipment = __instance.EquipmentSource;
//            if (equipment == null) return;

//            var comp = equipment.GetComp<CompMeleeCharge>();
//            if (comp == null) return;

//            // Запоминаем было ли заряжено оружие
//            chargeStates[__instance] = comp.HasCharges;

//            // Тратим заряд
//            comp.ConsumeCharge();

//            // Если зарядов не было - модифицируем verbProps временно для этого удара
//            if (!comp.HasCharges && comp.Props.unpoweredDamageDef != null)
//            {
//                // Сохраняем оригинальные значения
//                var originalDamageDef = __instance.verbProps.meleeDamageDef;
//                var originalPower = __instance.verbProps.AdjustedMeleeDamageAmount(__instance, __instance.CasterPawn);

//                // Модифицируем verbProps для этого удара
//                // К сожалению, verbProps readonly, поэтому мы не можем его изменить напрямую
//                // Вместо этого применяем урон через Hediff или дополнительный удар

//                // Применяем ослабленный урон напрямую к цели после оригинального удара
//                if (__instance.CurrentTarget.IsValid)
//                {
//                    ApplyUnpoweredDamage(__instance, comp);
//                }
//            }
//        }

//        private static void ApplyUnpoweredDamage(Verb verb, CompMeleeCharge comp)
//        {
//            if (verb.CurrentTarget.Thing == null) return;

//            float baseDamage = verb.verbProps.AdjustedMeleeDamageAmount(verb, verb.CasterPawn);
//            float modifiedDamage = baseDamage * comp.Props.unpoweredDamageMult;
//            float armorPenetration = verb.verbProps.AdjustedArmorPenetration(verb, verb.CasterPawn) * 0.5f;

//            DamageDef damageDef = comp.Props.unpoweredDamageDef ?? DamageDefOf.Blunt;

//            DamageInfo dinfo = new DamageInfo(
//                damageDef,
//                modifiedDamage,
//                armorPenetration,
//                -1f,
//                verb.Caster,
//                null,
//                verb.EquipmentSource?.def,
//                DamageInfo.SourceCategory.ThingOrUnknown
//            );

//            verb.CurrentTarget.Thing.TakeDamage(dinfo);
//        }
//    }

//    public class Verb_MeleeAttackCharge : Verb_MeleeAttackCE
//    {
//        private CompMeleeCharge chargeComp;

//        public override void WarmupComplete()
//        {
//            if (chargeComp == null && EquipmentSource != null)
//                chargeComp = EquipmentSource.GetComp<CompMeleeCharge>();
//            base.WarmupComplete();
//        }

//        public override void Notify_EquipmentLost()
//        {
//            base.Notify_EquipmentLost();
//            chargeComp?.CancelReload();
//        }
//    }
//}