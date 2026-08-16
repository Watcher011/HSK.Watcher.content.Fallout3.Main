using RimWorld;
using Verse;
using System.Collections.Generic;

namespace Watcher.Comps
{
    // Компонент для снятия гедонифа ToxicBuildup при прохождении
    public class CompToxicBuildupRemover : ThingComp
    {
        // Настройки из XML
        public CompProperties_ToxicBuildupRemover Props =>
            (CompProperties_ToxicBuildupRemover)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
        }

        // Метод для снятия ToxicBuildup
        public void RemoveToxicBuildup(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.Dead)
                return;

            // Ищем хеддиф ToxicBuildup
            Hediff toxicBuildup = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup);

            if (toxicBuildup != null)
            {
                // Полностью удаляем хеддиф
                pawn.health.RemoveHediff(toxicBuildup);

                // Отправляем сообщение в лог (опционально)
                //if (Props.showMessage)
                //{
                //    Messages.Message(
                //        "Watcher_ToxicBuildupRemoved".Translate(pawn.LabelShort),
                //        pawn,
                //        MessageTypeDefOf.PositiveEvent
                //    );
                //}
            }
        }

        public override void CompTick()
        {
            base.CompTick();

            // Проверяем прохождение пешек каждые 60 тиков (примерно 1 секунда)
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                CheckPawnsPassing();
            }
        }

        private void CheckPawnsPassing()
        {
            // Получаем все пешки на клетке с постройкой
            List<Thing> thingList = parent.Position.GetThingList(parent.Map);

            foreach (Thing thing in thingList)
            {
                if (thing is Pawn pawn && pawn.IsColonistPlayerControlled)
                {
                    // Проверяем, находится ли пешка в состоянии "проходит через дверь"
                    if (IsPawnPassingThrough(pawn))
                    {
                        RemoveToxicBuildup(pawn);
                    }
                }
            }
        }

        private bool IsPawnPassingThrough(Pawn pawn)
        {
            // Проверяем различные условия прохождения через дверь
            if (pawn.pather.Moving)
            {
                // Проверяем маршрут пешки
                if (pawn.pather.curPath != null && pawn.pather.curPath.NodesLeftCount > 0)
                {
                    // Если следующая клетка или текущая клетка совпадает с позицией двери
                    if (pawn.pather.nextCell == parent.Position || pawn.Position == parent.Position)
                    {
                        return true;
                    }
                }
            }

            // Дополнительная проверка: если пешка стоит на двери и взаимодействует с ней
            if (pawn.Position == parent.Position &&
                (pawn.CurJob != null && pawn.CurJob.targetA.Thing == parent))
            {
                return true;
            }

            return false;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
        }
    }

    // Свойства компонента для XML-настройки
    public class CompProperties_ToxicBuildupRemover : CompProperties
    {
        public bool showMessage = true; // Показывать сообщение при очистке
        public float removalEfficiency = 1.0f; // Эффективность удаления (1.0 = 100%)

        public CompProperties_ToxicBuildupRemover()
        {
            compClass = typeof(CompToxicBuildupRemover);
        }
    }
}