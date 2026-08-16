using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

//Игра тайно проверяет имена колонистов. Если кто-то носит имя, связанное с "Наблюдателем" (Watcher), то эта скрытая фракция посылает карательный рейд, чтобы устранить свидетеля.

//Лор: "Наблюдатели" - таинственная организация, которая не хочет, чтобы о ней знали. Если колонист носит их имя или имеет информацию о них, они посылают убийц.

namespace Watcher.GameComponents
{
    public class GameComponent_WatcherCheck : GameComponent
        {
            private int lastCheckDay = -1;
            private bool eventTriggered = false; // Флаг срабатывания события

            private string[] badNames = {
            "Наблюдатель", "Watcher", "Наблюдатель011", "Watcher011",
            "наблюдатель", "watcher", "наблюдатель011", "watcher011"
        };

            public GameComponent_WatcherCheck(Game game) { }

            public override void GameComponentTick()
            {
                // Если событие уже сработало, ничего не проверяем
                if (eventTriggered) return;

                // Проверяем раз в полдня (30000 тиков = 12 часов)
                if (Find.TickManager.TicksGame % 30000 != 0) return;

                int currentDay = GenDate.DaysPassed;
                if (currentDay == lastCheckDay) return;

                lastCheckDay = currentDay;
                CheckForWatcher();
            }

        private void CheckForWatcher()
        {
            // все карты, на которых есть ваши пешки
            List<Pawn> colonists = PawnsFinder.AllMaps
                .Where(p => p.Faction == Faction.OfPlayer && !p.Dead && p.IsColonist)
                .ToList();

            foreach (Pawn colonist in colonists)
            {
                string name = colonist.Name?.ToStringFull ?? "";

                if (badNames.Any(bad =>
                        name.IndexOf(bad, System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    TriggerRaid(colonist);
                    eventTriggered = true;
                    return;
                }
            }
        }

        private void TriggerRaid(Pawn watcher)
            {
                Map map = watcher.MapHeld ?? Find.CurrentMap;
                if (map == null) return;

                IncidentParms parms = new IncidentParms
                {
                    target = map,
                    points = StorytellerUtility.DefaultThreatPointsNow(map) * 1.5f
                };

                if (IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
                {
                    Find.LetterStack.ReceiveLetter("watcherLabel".Translate(), "watcherText".Translate(), LetterDefOf.NegativeEvent);
                }
            }

            public override void ExposeData()
            {
                Scribe_Values.Look(ref lastCheckDay, "lastCheckDay", -1);
                Scribe_Values.Look(ref eventTriggered, "eventTriggered", false); // Сохраняем флаг
            }
        }
    }

