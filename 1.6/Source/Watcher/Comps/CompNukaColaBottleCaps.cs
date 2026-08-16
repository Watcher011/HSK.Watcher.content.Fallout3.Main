using RimWorld;
using System;
using Verse;

namespace Watcher.Comps
{
    public class IngestionOutcomeDoer_SpawnItem : IngestionOutcomeDoer
    {
        public ThingDef thingDef;
        public int count = 1;
        public float spawnRadius = 0.1f;
        public string message;


        protected override void DoIngestionOutcomeSpecial(Pawn pawn, Thing ingested, int ingestedCount)
        {
            if (pawn == null || pawn.Map == null || pawn.Dead || thingDef == null)
                return;

            try
            {
                // Создаем предмет
                Thing thing = ThingMaker.MakeThing(thingDef);
                thing.stackCount = Math.Max(1, count);

                // Находим место для спавна - в первую очередь прямо под колонистом
                IntVec3 dropCell = pawn.Position;

                // Проверяем, можно ли разместить предмет прямо под колонистом
                bool canPlaceAtPawnPosition = dropCell.InBounds(pawn.Map) &&
                                              dropCell.Walkable(pawn.Map) &&
                                              !dropCell.Fogged(pawn.Map);

                // Если нельзя разместить под колонистом, ищем ближайшую доступную клетку
                if (!canPlaceAtPawnPosition)
                {
                    // Ищем в радиусе spawnRadius (у вас 0.1, что означает только соседние клетки)
                    for (int i = 0; i < GenRadial.NumCellsInRadius(spawnRadius); i++)
                    {
                        IntVec3 cell = pawn.Position + GenRadial.RadialPattern[i];
                        if (cell.InBounds(pawn.Map) && cell.Walkable(pawn.Map) && !cell.Fogged(pawn.Map))
                        {
                            dropCell = cell;
                            break;
                        }
                    }
                }

                // Если всё ещё не нашли подходящую клетку, пробуем найти любую рядом
                if (dropCell == pawn.Position && !canPlaceAtPawnPosition)
                {
                    // Ищем в чуть большем радиусе (до 3 клеток)
                    for (int radius = 1; radius <= 3; radius++)
                    {
                        bool found = false;
                        foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, radius, true))
                        {
                            if (cell.InBounds(pawn.Map) && cell.Walkable(pawn.Map) && !cell.Fogged(pawn.Map))
                            {
                                dropCell = cell;
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                    }
                }

                // Спавним предмет
                GenPlace.TryPlaceThing(thing, dropCell, pawn.Map, ThingPlaceMode.Near);

                // Визуальный эффект
                FleckMaker.ThrowMetaPuffs(new TargetInfo(dropCell, pawn.Map));

                // Сообщение
                //if (!message.NullOrEmpty() && pawn.IsColonistPlayerControlled)
                //{
                //    Messages.Message(message.CapitalizeFirst(), thing, MessageTypeDefOf.PositiveEvent);
                //}
            }
            catch (Exception ex)
            {
                Log.Error($"IngestionOutcomeDoer_SpawnItem error: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}