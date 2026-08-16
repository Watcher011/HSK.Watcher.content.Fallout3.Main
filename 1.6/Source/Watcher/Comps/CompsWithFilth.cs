using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Watcher.Comps
{
    /// <summary>
    /// Свойства компонента для уборки
    /// </summary>
    public class CompProperties_Cleanable : CompProperties
    {
        /// <summary>
        /// Количество работы для полной уборки
        /// </summary>
        public int workToClean = 70;

        public CompProperties_Cleanable()
        {
            compClass = typeof(CompCleanable);
        }
    }

    /// <summary>
    /// Компонент, делающий объект убираемым
    /// </summary>
    public class CompCleanable : ThingComp
    {
        /// <summary>
        /// Доступ к свойствам компонента
        /// </summary>
        public CompProperties_Cleanable Props => (CompProperties_Cleanable)props;

        /// <summary>
        /// Сколько работы уже выполнено
        /// </summary>
        private float cleanedWork = 0f;

        /// <summary>
        /// Сохранение/загрузка данных
        /// </summary>
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref cleanedWork, "cleanedWork", 0f);
        }

        /// <summary>
        /// Проверка, можно ли убирать объект
        /// </summary>
        public bool CanClean()
        {
            return parent.Spawned && !parent.Destroyed;
        }

        /// <summary>
        /// Выполнить работу по уборке
        /// </summary>
        /// <param name="workDone">Количество выполненной работы за тик</param>
        public void DoCleanWork(float workDone)
        {
            cleanedWork += workDone;

            // Отладка - каждую секунду
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                Log.Message($"[Watcher] Cleaning progress: {cleanedWork:F1}/{Props.workToClean}");
            }

            // Если уборка завершена
            if (cleanedWork >= Props.workToClean)
            {
                FinishClean();
            }
        }

        /// <summary>
        /// Завершение уборки
        /// </summary>
        private void FinishClean()
        {
            //Log.Message($"[Watcher] Filth cleaned! Progress reached {cleanedWork}");

            // Воспроизводим звук уборки
            if (parent.Map != null)
            {
                SoundDefOf.Interact_CleanFilth.PlayOneShot(new TargetInfo(parent.Position, parent.Map));
            }

            // Уничтожаем объект
            if (!parent.Destroyed)
            {
                parent.Destroy();
                //Log.Message("[Watcher] Filth destroyed");
            }
        }

        /// <summary>
        /// Прогресс уборки (0-1)
        /// </summary>
        public float Progress => cleanedWork / Props.workToClean;
    }

    /// <summary>
    /// Грязь с поддержкой компонентов (Glower, Radioactive, Cleanable)
    /// </summary>
    public class FilthRadioactive : ThingWithComps
    {
        /// <summary>
        /// Таймер автоматического исчезновения
        /// </summary>
        private int disposeTick = -1;

        /// <summary>
        /// Статический конструктор для проверки загрузки
        /// </summary>
        static FilthRadioactive()
        {
        //    Log.Message("[Watcher] FilthRadioactive loaded successfully!");
        }

        /// <summary>
        /// Вызывается при появлении объекта на карте
        /// </summary>
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);

            // Настраиваем автоматическое исчезновение из тега filth в XML
            if (def.filth != null && def.filth.disappearsInDays != null)
            {
                float days = def.filth.disappearsInDays.RandomInRange;
                if (days > 0f)
                {
                    disposeTick = Find.TickManager.TicksGame + (int)(days * 60000f);
                    //Log.Message($"[Watcher] Filth will disappear in {days} days (tick {disposeTick})");
                }
            }
        }

        /// <summary>
        /// Каждый тик
        /// </summary>
        protected override void Tick()
        {
            base.Tick(); // Вызывает компоненты (Glower, Radioactive, Cleanable)

            // Проверка на автоматическое исчезновение
            if (disposeTick > 0 && Find.TickManager.TicksGame >= disposeTick)
            {
                //Log.Message("[Watcher] Filth disappeared naturally");
                Destroy();
            }
        }

        /// <summary>
        /// Переопределяем Destroy для отладки
        /// </summary>
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            //Log.Message($"[Watcher] Filth being destroyed at tick {Find.TickManager.TicksGame}");
            base.Destroy(mode);
        }

        /// <summary>
        /// Сохранение/загрузка данных
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref disposeTick, "disposeTick", -1);
        }
    }

    /// <summary>
    /// WorkGiver для уборки радиоактивной грязи
    /// </summary>
    public class WorkGiver_CleanRadioactiveFilth : WorkGiver_Scanner
    {
        /// <summary>
        /// Что ищем для уборки
        /// </summary>
        public override ThingRequest PotentialWorkThingRequest
        {
            get
            {
                return ThingRequest.ForDef(ThingDef.Named("Filth_NFRAD"));
            }
        }

        /// <summary>
        /// Режим пути - вплотную к объекту
        /// </summary>
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        /// <summary>
        /// Всегда сканировать всю карту
        /// </summary>
        public override bool AllowUnreachable => false;

        /// <summary>
        /// Проверка, можно ли дать задание на эту вещь
        /// </summary>
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            // Проверяем, что это наша грязь
            if (t.def.defName != "Filth_NFRAD")
                return false;

            // Проверяем, что объект в домашней зоне
            if (!t.Map.areaManager.Home[t.Position])
                return false;

            // Проверяем доступность и запреты
            if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, forced))
                return false;

            // Проверяем компонент Cleanable
            CompCleanable cleanable = t.TryGetComp<CompCleanable>();
            if (cleanable == null || !cleanable.CanClean())
                return false;

            return true;
        }

        /// <summary>
        /// Создание задания на уборку
        /// </summary>
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            // Используем специальное задание для нашей грязи
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("CleanRadioactiveFilth"), t);
            job.ignoreDesignations = true;
            return job;
        }
    }

    /// <summary>
    /// Драйвер задания для уборки радиоактивной грязи
    /// </summary>
    public class JobDriver_CleanRadioactiveFilth : JobDriver
    {
        /// <summary>
        /// Целевая грязь
        /// </summary>
        private Thing Filth => job.GetTarget(TargetIndex.A).Thing;

        /// <summary>
        /// Компонент Cleanable
        /// </summary>
        private CompCleanable Cleanable => Filth?.TryGetComp<CompCleanable>();

        /// <summary>
        /// Резервирование цели
        /// </summary>
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Filth, job, 1, -1, null, errorOnFailed);
        }

        /// <summary>
        /// Создание последовательности действий
        /// </summary>
        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Проверяем, что грязь еще существует
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => Cleanable == null || !Cleanable.CanClean());
            this.FailOn(() => Filth == null || Filth.Destroyed);

            // Идем к грязи
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            // Собственно уборка
            Toil cleanToil = new Toil();

            cleanToil.initAction = () =>
            {
                // Начинаем уборку
                pawn.pather.StopDead();
                //Log.Message("[Watcher] Starting to clean");
            };

            cleanToil.tickAction = () =>
            {
                // Проверяем, существует ли еще грязь
                if (Filth == null || Filth.Destroyed)
                {
                    //Log.Message("[Watcher] Filth destroyed during cleaning");
                    ReadyForNextToil();
                    return;
                }

                // Каждый тик делаем работу по уборке
                float workDone = 0.01f; // Базовая скорость

                // Учитываем скорость уборки поселенца
                workDone *= pawn.GetStatValue(StatDefOf.CleaningSpeed);

                // Выполняем работу
                Cleanable?.DoCleanWork(workDone);

                // Визуальный эффект

            };

            cleanToil.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
            cleanToil.WithProgressBar(TargetIndex.A, () => Cleanable?.Progress ?? 0f);

            // ВАЖНО: Завершаемся, когда объект уничтожен
            cleanToil.AddFinishAction(() =>
            {
                //Log.Message("[Watcher] Cleaning toil finished");
            });

            // Завершаем задание, когда объект уничтожен
            cleanToil.defaultCompleteMode = ToilCompleteMode.Delay;

            // Рассчитываем длительность на основе скорости уборки
            float cleaningSpeed = pawn.GetStatValue(StatDefOf.CleaningSpeed);
            if (cleaningSpeed < 0.1f) cleaningSpeed = 1f;
            cleanToil.defaultDuration = (int)((Cleanable?.Props.workToClean ?? 70) / (0.01f * cleaningSpeed));

            yield return cleanToil;

            // Завершаем задание
            yield return new Toil
            {
                initAction = () =>
                {
                    //Log.Message("[Watcher] Cleaning job completed");
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        /// <summary>
        /// Для совместимости
        /// </summary>
        public override object[] TaleParameters()
        {
            return new object[] { Filth?.def };
        }
    }
}