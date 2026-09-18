// ============================================================
// Watcher.Buildings - Nuclear Reactor
// Собрано из: WatcherDefOf.cs, CompNuclearReactor.cs, WorkGiver_RepairReactorCore.cs, JobDriver_RepairReactorCore.cs
// Обновлено: радиация теперь распространяется по комнате (Room), а не по радиусу
//            (кроме отката для улицы и разового выброса при Meltdown)
// ============================================================

using System.Collections.Generic;
using RimWorld;
using System.Linq;
using UnityEngine;
using Verse.AI;
using Verse.Sound;
using Verse;

namespace Watcher.Buildings
{
    // ---- из WatcherDefOf.cs ----
    [DefOf]
    public static class WatcherDefOf
    {
        public static JobDef Watcher_RepairReactorCore;

        static WatcherDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(WatcherDefOf));
        }
    }

    // ---- из CompNuclearReactor.cs ----
    public enum ReactorState : byte
    {
        Offline,
        RampingUp,
        Working,
        RampingDown,
        Destabilizing,
        Meltdown
    }

    public class CompProperties_NuclearReactor : CompProperties
    {
        // Мощность на выходе при полном разгоне
        public float workingPowerOutput = 3000f;

        public int rampUpDurationTicks = 60000;
        public int rampDownDurationTicks = 15000;

        public float destabilizationHealthThreshold = 0.5f;
        public float destabilizationChancePerTickWhenDamaged = 0.00003f;
        public float spontaneousDestabilizationMtbDays = 0.00003f;

        // Хедиф радиации
        public HediffDef radiationHediff;
        public float radiationRadius = 8f;
        public float radiationSeverityPerTick = 0.008f;

        // Радиоактивные отходы — теперь это ПРЕДМЕТ, который кладётся на пол.
        // ВАЖНО: это должен быть обычный ThingDef (item), НЕ Filth-деф.
        public ThingDef wasteItemDef;
        public float wasteSpawnRadius = 6f;
        public float wasteSpawnMtbTicksPerCell = 12000f;

        // Звуки
        public SoundDef soundDestabilizing;
        public SoundDef soundMeltdown;

        public ThingDef coreComponentDef;
        public int coreComponentWorkTicks = 4000;

        public bool meltdownIsIrreversible = true;

        // Если true — при коллапсе реактора здание физически уничтожается взрывом.
        public bool destroyBuildingOnMeltdown = true;

        // Сколько предметов-отходов разбрасывается воронкой в момент уничтожения
        public int meltdownCraterWasteCount = 12;
        public float meltdownCraterRadius = 4f;

        // ---- Параметры взрыва при коллапсе ядра ----
        // DamageDef взрыва; если null — используется DamageDefOf.Bomb
        public DamageDef meltdownExplosionDamageDef;
        public float meltdownExplosionRadius = 4.9f;
        public int meltdownExplosionDamage = 250;
        public float meltdownExplosionChanceToStartFire = 0.4f;
        // Отдельный звук для взрыва; если null — играет soundMeltdown
        public SoundDef meltdownExplosionSound;

        public int ticksUntilMeltdown = 30000;

        public CompProperties_NuclearReactor()
        {
            compClass = typeof(CompNuclearReactor);
        }
    }

    public class CompNuclearReactor : ThingComp
    {
        public CompProperties_NuclearReactor Props => (CompProperties_NuclearReactor)props;

        public bool NeedsCoreReplacement => state == ReactorState.Destabilizing;

        private CompPowerTrader powerTrader;
        private CompRefuelable refuelable;
        private CompFlickable flickable;

        public ReactorState state = ReactorState.Offline;

        private float powerFraction = 0f;
        private int destabilizedTicksElapsed = 0;
        private int ticksToNextWaste = 0;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            powerTrader = parent.GetComp<CompPowerTrader>();
            refuelable = parent.GetComp<CompRefuelable>();
            flickable = parent.GetComp<CompFlickable>();
            ResetWasteTimer();
        }

        private bool WantsOn => flickable == null || flickable.SwitchIsOn;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref state, "reactorState", ReactorState.Offline);
            Scribe_Values.Look(ref powerFraction, "reactorPowerFraction", 0f);
            Scribe_Values.Look(ref destabilizedTicksElapsed, "destabilizedTicksElapsed", 0);
            // Ключ оставил старый, чтобы не терять таймер в существующих сейвах.
            Scribe_Values.Look(ref ticksToNextWaste, "ticksToNextFilth", 0);
        }

        private bool HasFuel => refuelable == null || refuelable.HasFuel;

        public override void CompTick()
        {
            base.CompTick();

            switch (state)
            {
                case ReactorState.Offline: TickOffline(); break;
                case ReactorState.RampingUp: TickRampUp(); break;
                case ReactorState.Working: TickWorking(); break;
                case ReactorState.RampingDown: TickRampDown(); break;
                case ReactorState.Destabilizing: TickDestabilizing(); break;
                case ReactorState.Meltdown: TickMeltdown(); break;
            }

            // TriggerMeltdown() мог уничтожить parent прямо в этом тике.
            if (!parent.Spawned)
            {
                return;
            }

            ApplyPowerOutput();
        }

        // ---------- Offline / ramp logic ----------

        private void TickOffline()
        {
            powerFraction = 0f;
            if (HasFuel && WantsOn)
            {
                state = ReactorState.RampingUp;
            }
        }

        private void TickRampUp()
        {
            if (!HasFuel || !WantsOn)
            {
                state = ReactorState.RampingDown;
                return;
            }

            float step = 1f / Mathf.Max(1, Props.rampUpDurationTicks);
            powerFraction = Mathf.Min(1f, powerFraction + step);

            if (powerFraction >= 1f)
            {
                state = ReactorState.Working;
            }

            CheckForDestabilization();
        }

        private void TickWorking()
        {
            powerFraction = 1f;

            if (!HasFuel || !WantsOn)
            {
                state = ReactorState.RampingDown;
                return;
            }

            CheckForDestabilization();
        }

        private void TickRampDown()
        {
            float step = 1f / Mathf.Max(1, Props.rampDownDurationTicks);
            powerFraction = Mathf.Max(0f, powerFraction - step);

            if (powerFraction <= 0f)
            {
                state = ReactorState.Offline;
                return;
            }

            if (HasFuel && WantsOn)
            {
                state = ReactorState.RampingUp;
            }

            CheckForDestabilization();
        }

        private void CheckForDestabilization()
        {
            if (parent.HitPoints < parent.MaxHitPoints * Props.destabilizationHealthThreshold)
            {
                if (Rand.MTBEventOccurs(1f / Mathf.Max(0.0001f, Props.destabilizationChancePerTickWhenDamaged), 1f, 1f))
                {
                    BeginDestabilization();
                    return;
                }
            }

            if (Props.spontaneousDestabilizationMtbDays > 0f &&
                Rand.MTBEventOccurs(Props.spontaneousDestabilizationMtbDays, 60000f, 1f))
            {
                BeginDestabilization();
            }
        }

        // ---------- Destabilization ----------

        public void BeginDestabilization()
        {
            if (state == ReactorState.Destabilizing || state == ReactorState.Meltdown)
            {
                return;
            }

            state = ReactorState.Destabilizing;
            destabilizedTicksElapsed = 0;

            // Разовое оповещение-письмо (не Message, а Letter) — приходит один раз
            // в момент входа в состояние Destabilizing, т.к. BeginDestabilization()
            // защищён от повторного входа проверкой в начале метода.
            // Используем стандартный ванильный LetterDef ThreatBig — свой LetterDef
            // с кастомной иконкой не нужен.
            Find.LetterStack.ReceiveLetter(
                "Watcher_ReactorDestabilizingLabel".Translate(),
                "Watcher_ReactorDestabilizing".Translate(parent.LabelShortCap),
                LetterDefOf.ThreatBig,
                new LookTargets(parent)
            );

            if (Props.soundDestabilizing != null)
            {
                Props.soundDestabilizing.PlayOneShot(SoundInfo.InMap(parent));
            }
        }

        private void TickDestabilizing()
        {
            destabilizedTicksElapsed++;

            powerFraction = 1f;

            ApplyRadiationHediffs();
            TrySpawnWaste();

            if (destabilizedTicksElapsed >= Props.ticksUntilMeltdown)
            {
                TriggerMeltdown();
            }
        }

        // ---------- Radiation ----------

        /// <summary>
        /// Точка входа: во время Destabilizing радиация распространяется по замкнутой
        /// комнате реактора (через GetRoom), а не по прямому радиусу. Если реактор
        /// стоит на улице / в помещении без крыши — откатываемся на старую логику по радиусу.
        /// </summary>
        private void ApplyRadiationHediffs()
        {
            Room room = parent.GetRoom();

            if (room == null || room.PsychologicallyOutdoors)
            {
                ApplyRadiationInRadius(Props.radiationRadius, Props.radiationSeverityPerTick);
                return;
            }

            ApplyRadiationInRoom(room, Props.radiationSeverityPerTick);
        }

        /// <summary>
        /// Накладывает радиационный хедиф на всех живых пешек, находящихся в той же
        /// комнате, что и реактор. Стены и двери останавливают распространение.
        /// </summary>
        private void ApplyRadiationInRoom(Room room, float severity)
        {
            if (Props.radiationHediff == null) return;

            IReadOnlyList<Pawn> pawns = parent.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.RaceProps.IsFlesh && pawn.GetRoom() == room)
                {
                    AddRadiationSeverity(pawn, severity);
                }
            }
        }

        /// <summary>
        /// Старая радиус-логика. Используется как фолбэк для улицы и для разового
        /// мощного выброса в момент TriggerMeltdown(), когда комната может уже не существовать.
        /// </summary>
        private void ApplyRadiationInRadius(float radius, float severity)
        {
            if (Props.radiationHediff == null) return;

            foreach (IntVec3 cell in GenRadial.RadialCellsAround(parent.Position, radius, true))
            {
                if (!cell.InBounds(parent.Map)) continue;

                List<Thing> thingsHere = cell.GetThingList(parent.Map);
                for (int i = 0; i < thingsHere.Count; i++)
                {
                    if (thingsHere[i] is Pawn pawn && pawn.RaceProps.IsFlesh)
                    {
                        AddRadiationSeverity(pawn, severity);
                    }
                }
            }
        }

        private void AddRadiationSeverity(Pawn pawn, float severity)
        {
            if (pawn?.health?.hediffSet == null) return;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.radiationHediff);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(Props.radiationHediff, pawn);
                hediff.Severity = severity;
                pawn.health.AddHediff(hediff);
            }
            else
            {
                hediff.Severity += severity;
            }
        }

        // ---------- Waste items ----------

        private void TrySpawnWaste()
        {
            if (Props.wasteItemDef == null) return;

            ticksToNextWaste--;
            if (ticksToNextWaste > 0) return;
            ResetWasteTimer();

            IntVec3 cell = parent.Position + GenRadial.RadialPattern[
                Rand.Range(0, GenRadial.NumCellsInRadius(Props.wasteSpawnRadius))
            ];

            if (cell.InBounds(parent.Map) && cell.Walkable(parent.Map))
            {
                SpawnWasteAt(cell, parent.Map);
            }
        }

        /// <summary>
        /// Спавнит один предмет-отход в указанной клетке. Больше не использует FilthMaker,
        /// поэтому wasteItemDef должен быть обычным ThingDef (item).
        /// </summary>
        private void SpawnWasteAt(IntVec3 cell, Map map)
        {
            if (Props.wasteItemDef == null) return;

            Thing waste = ThingMaker.MakeThing(Props.wasteItemDef);
            waste.stackCount = 1;
            GenPlace.TryPlaceThing(waste, cell, map, ThingPlaceMode.Near);
        }

        private void ResetWasteTimer()
        {
            ticksToNextWaste = Mathf.Max(1, Rand.Range(
                (int)(Props.wasteSpawnMtbTicksPerCell * 0.5f),
                (int)(Props.wasteSpawnMtbTicksPerCell * 1.5f)
            ));
        }

        /// <summary>
        /// Стабилизация ядра заменой активной зоны реактора.
        /// </summary>
        public bool TryStabilizeWithComponent(Thing componentThing)
        {
            if (state != ReactorState.Destabilizing) return false;
            if (Props.coreComponentDef == null || componentThing?.def != Props.coreComponentDef) return false;

            componentThing.Destroy();

            destabilizedTicksElapsed = 0;
            state = HasFuel ? ReactorState.RampingUp : ReactorState.Offline;
            powerFraction = 0f;

            Messages.Message(
                "Watcher_ReactorStabilized".Translate(parent.LabelShortCap),
                new LookTargets(parent),
                MessageTypeDefOf.PositiveEvent
            );

            return true;
        }

        private void TriggerMeltdown()
        {
            if (Props.soundMeltdown != null)
            {
                Props.soundMeltdown.PlayOneShot(SoundInfo.InMap(parent));
            }

            state = ReactorState.Meltdown;
            powerFraction = 0f;

            Messages.Message(
                "Watcher_ReactorMeltdown".Translate(parent.LabelShortCap),
                new LookTargets(parent),
                MessageTypeDefOf.NegativeEvent
            );

            // Разовый мощный радиационный выброс — намеренно по радиусу, а не по комнате:
            // в момент взрыва комната может уже разрушаться вместе со зданием.
            ApplyRadiationInRadius(Props.radiationRadius * 2f, Props.radiationSeverityPerTick * 40f);

            if (!Props.meltdownIsIrreversible)
            {
                state = ReactorState.Offline;
                return;
            }

            if (Props.destroyBuildingOnMeltdown)
            {
                DestroyReactorInMeltdown();
            }
            // Иначе — реактор остаётся "мёртвым саркофагом" в состоянии Meltdown.
        }

        /// <summary>
        /// Необратимый коллапс: разбрасываем предметы-отходы, устраиваем взрыв,
        /// который уничтожает постройку. Если взрыв не добил — уничтожаем вручную.
        /// </summary>
        private void DestroyReactorInMeltdown()
        {
            Map map = parent.Map;
            IntVec3 position = parent.Position;

            // Если parent уже не на карте — просто ничего не делаем.
            if (map == null)
            {
                if (parent.Spawned) parent.Destroy(DestroyMode.KillFinalize);
                return;
            }

            // 1) Воронка из предметов-отходов
            if (Props.wasteItemDef != null)
            {
                for (int i = 0; i < Props.meltdownCraterWasteCount; i++)
                {
                    IntVec3 cell = position + GenRadial.RadialPattern[
                        Rand.Range(0, GenRadial.NumCellsInRadius(Props.meltdownCraterRadius))
                    ];

                    if (cell.InBounds(map) && cell.Walkable(map))
                    {
                        SpawnWasteAt(cell, map);
                    }
                }
            }

            // 2) Взрыв. Именно он должен разнести постройку.
            DoMeltdownExplosion(map, position);

            // 3) Страховка: если взрыв не убил здание (например, слишком мало damage) —
            // добиваем его сами, чтобы не оставалось "висящего" реактора без визуального эффекта.
            if (parent.Spawned && !parent.Destroyed)
            {
                parent.Destroy(DestroyMode.KillFinalize);
            }
        }

        private void DoMeltdownExplosion(Map map, IntVec3 position)
        {
            DamageDef damageDef = Props.meltdownExplosionDamageDef ?? DamageDefOf.Bomb;
            SoundDef explosionSound = Props.meltdownExplosionSound; // допускается null

            GenExplosion.DoExplosion(
                center: position,
                map: map,
                radius: Props.meltdownExplosionRadius,
                damType: damageDef,
                instigator: parent,
                damAmount: Props.meltdownExplosionDamage,
                armorPenetration: -1f,
                explosionSound: explosionSound,
                weapon: null,
                projectile: null,
                intendedTarget: null,
                postExplosionSpawnThingDef: null,
                postExplosionSpawnChance: 0f,
                postExplosionSpawnThingCount: 1,
                postExplosionGasType: null,
                applyDamageToExplosionCellsNeighbors: false,
                preExplosionSpawnThingDef: null,
                preExplosionSpawnChance: 0f,
                preExplosionSpawnThingCount: 1,
                chanceToStartFire: Props.meltdownExplosionChanceToStartFire,
                damageFalloff: true
            );
        }

        private void TickMeltdown()
        {
            powerFraction = 0f;

            if (!Props.meltdownIsIrreversible) return;

            // Аварийный режим без уничтожения здания: продолжаем фонить и гадить предметами.
            ApplyRadiationHediffs();
            TrySpawnWaste();
        }

        // ---------- Power output ----------

        private void ApplyPowerOutput()
        {
            if (powerTrader == null) return;

            float target = Mathf.Abs(Props.workingPowerOutput) * powerFraction;
            powerTrader.PowerOutput = target;
        }

        // ---------- Gizmos ----------

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (!Prefs.DevMode) yield break;

            yield return new Command_Action
            {
                defaultLabel = "DEV: Состояние -> Offline",
                defaultDesc = "Сбросить реактор в выключенное состояние, мощность 0.",
                action = delegate
                {
                    state = ReactorState.Offline;
                    powerFraction = 0f;
                    destabilizedTicksElapsed = 0;
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV: Начать разгон (RampingUp)",
                defaultDesc = "Запустить плавный разгон с 0% до 100% мощности.",
                action = delegate
                {
                    state = ReactorState.RampingUp;
                    powerFraction = 0f;
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV: Мгновенно на полную мощность (Working)",
                defaultDesc = "Пропустить разгон и сразу выйти на 100% мощности.",
                action = delegate
                {
                    state = ReactorState.Working;
                    powerFraction = 1f;
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV: Начать останов (RampingDown)",
                defaultDesc = "Плавно снизить мощность до 0% и уйти в Offline.",
                action = delegate
                {
                    state = ReactorState.RampingDown;
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV: Начать дестабилизацию",
                defaultDesc = "Запустить аварийный сценарий: звук, хедиф радиации в комнате реактора, спавн предметов-отходов. Если не остановить — через ticksUntilMeltdown наступит коллапс со взрывом.",
                action = BeginDestabilization
            };

            if (state == ReactorState.Destabilizing)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Стабилизировать (пропустить компонент)",
                    defaultDesc = "Симулирует успешную замену активной зоны без реального предмета-компонента.",
                    action = delegate
                    {
                        destabilizedTicksElapsed = 0;
                        state = HasFuel ? ReactorState.RampingUp : ReactorState.Offline;
                        powerFraction = 0f;
                    }
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: Ускорить время до коллапса",
                    defaultDesc = "Домотать destabilizedTicksElapsed почти до порога ticksUntilMeltdown.",
                    action = delegate
                    {
                        destabilizedTicksElapsed = Mathf.Max(0, Props.ticksUntilMeltdown - 10);
                    }
                };
            }

            yield return new Command_Action
            {
                defaultLabel = "DEV: Форсировать коллапс (Meltdown + взрыв)",
                defaultDesc = "Сразу вызвать TriggerMeltdown(): радиация, разброс предметов-отходов, взрыв и уничтожение постройки.",
                action = TriggerMeltdown
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV: Наложить радиацию вокруг (тест хедифа)",
                defaultDesc = "Разово применить радиационный хедиф ко всем пешкам в комнате реактора (или по радиусу на улице).",
                action = delegate
                {
                    Room room = parent.GetRoom();
                    if (room == null || room.PsychologicallyOutdoors)
                        ApplyRadiationInRadius(Props.radiationRadius, Props.radiationSeverityPerTick * 20f);
                    else
                        ApplyRadiationInRoom(room, Props.radiationSeverityPerTick * 20f);
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV: Заспавнить предмет-отход сейчас",
                defaultDesc = "Принудительно вызвать TrySpawnWaste(), игнорируя таймер.",
                action = delegate
                {
                    ticksToNextWaste = 0;
                    TrySpawnWaste();
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV: Инфо о состоянии в лог",
                defaultDesc = "Вывести текущее состояние, powerFraction, тики дестабилизации и PowerOutput в лог.",
                action = delegate
                {
                    Log.Message(
                        $"[NuclearReactor] state={state}, powerFraction={powerFraction:F2}, " +
                        $"destabilizedTicksElapsed={destabilizedTicksElapsed}/{Props.ticksUntilMeltdown}, " +
                        $"HasFuel={HasFuel}, WantsOn={WantsOn}, " +
                        $"PowerOutput={(powerTrader != null ? powerTrader.PowerOutput.ToString("F0") : "нет CompPowerTrader")}"
                    );
                }
            };
        }

        public override string CompInspectStringExtra()
        {
            string stateLabel;
            switch (state)
            {
                case ReactorState.Offline:
                    stateLabel = "Watcher_ReactorState_Offline".Translate();
                    break;
                case ReactorState.RampingUp:
                    stateLabel = "Watcher_ReactorState_RampingUp".Translate(Mathf.RoundToInt(powerFraction * 100f));
                    break;
                case ReactorState.Working:
                    stateLabel = "Watcher_ReactorState_Working".Translate();
                    break;
                case ReactorState.RampingDown:
                    stateLabel = "Watcher_ReactorState_RampingDown".Translate(Mathf.RoundToInt(powerFraction * 100f));
                    break;
                case ReactorState.Destabilizing:
                    stateLabel = "Watcher_ReactorState_Destabilizing".Translate(
                        Mathf.RoundToInt((float)(Props.ticksUntilMeltdown - destabilizedTicksElapsed) / 2500f)
                    );
                    break;
                case ReactorState.Meltdown:
                    stateLabel = "Watcher_ReactorState_Meltdown".Translate();
                    break;
                default:
                    stateLabel = "";
                    break;
            }

            return stateLabel;
        }
    }

    // ---- из WorkGiver_RepairReactorCore.cs ----
    public class WorkGiver_RepairReactorCore : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false) => false;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompNuclearReactor comp = t.TryGetComp<CompNuclearReactor>();
            if (comp == null || !comp.NeedsCoreReplacement) return false;

            if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, forced)) return false;

            Thing component = FindNearbyComponent(pawn, comp);
            return component != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompNuclearReactor comp = t.TryGetComp<CompNuclearReactor>();
            if (comp == null) return null;

            Thing component = FindNearbyComponent(pawn, comp);
            if (component == null) return null;

            Job job = JobMaker.MakeJob(WatcherDefOf.Watcher_RepairReactorCore, t, component);
            job.count = 1;
            return job;
        }

        private Thing FindNearbyComponent(Pawn pawn, CompNuclearReactor comp)
        {
            ThingDef componentDef = comp.Props.coreComponentDef;
            if (componentDef == null) return null;

            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(componentDef),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                (Thing x) => !x.IsForbidden(pawn) && pawn.CanReserve(x)
            );
        }
    }

    // ---- из JobDriver_RepairReactorCore.cs ----
    public class JobDriver_RepairReactorCore : JobDriver
    {
        private const TargetIndex ReactorInd = TargetIndex.A;
        private const TargetIndex ComponentInd = TargetIndex.B;

        private Thing Reactor => job.GetTarget(ReactorInd).Thing;
        private Thing ComponentThing => job.GetTarget(ComponentInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Reactor, job, 1, -1, null, errorOnFailed)
                && pawn.Reserve(ComponentThing, job, 1, 1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedNullOrForbidden(ReactorInd);
            this.FailOnDestroyedNullOrForbidden(ComponentInd);

            this.FailOn(delegate
            {
                CompNuclearReactor c = Reactor?.TryGetComp<CompNuclearReactor>();
                return c == null || !c.NeedsCoreReplacement;
            });

            yield return Toils_Goto.GotoThing(ComponentInd, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(ComponentInd);

            yield return Toils_Goto.GotoThing(ReactorInd, PathEndMode.InteractionCell);

            Toil work = ToilMaker.MakeToil("MakeNewToils");
            work.initAction = delegate
            {
                CompNuclearReactor comp = Reactor.TryGetComp<CompNuclearReactor>();
                work.actor.jobs.curDriver.ticksLeftThisToil = comp?.Props.coreComponentWorkTicks ?? 4000;
            };
            work.tickAction = delegate
            {
                work.actor.skills?.Learn(SkillDefOf.Construction, 0.1f);
            };
            work.defaultCompleteMode = ToilCompleteMode.Delay;
            work.WithProgressBarToilDelay(ReactorInd);
            work.FailOnDestroyedNullOrForbidden(ReactorInd);
            work.FailOnCannotTouch(ReactorInd, PathEndMode.InteractionCell);
            work.activeSkill = () => SkillDefOf.Construction;
            yield return work;

            Toil finish = ToilMaker.MakeToil("MakeNewToils");
            finish.initAction = delegate
            {
                CompNuclearReactor comp = Reactor.TryGetComp<CompNuclearReactor>();
                Thing carried = pawn.carryTracker.CarriedThing;
                if (comp != null && carried != null)
                {
                    comp.TryStabilizeWithComponent(carried);
                }
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }
}