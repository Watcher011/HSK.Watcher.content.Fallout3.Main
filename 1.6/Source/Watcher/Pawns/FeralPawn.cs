using RimWorld;
using SK;
using Verse;

//Этот код добавляет в игру особый тип персонажа, который вызывает токсичную погоду при нахождении на карте.

namespace Watcher
{
    internal class FeralPawn : Pawn
    {
        /* >>> protected override, а не public override <<< */
        protected override void Tick()
        {
            base.Tick();

            if (Find.TickManager.TicksGame % 250 == 0)
                TickRare();

            if (!Controller.Settings.disableBarghestEclipse &&
                Find.TickManager.TicksGame % 18000 == 0 &&
                Map != null &&
                !Map.gameConditionManager.ConditionIsActive(GameConditionDefOfLocal.ToxicWeather))
            {
                Map.gameConditionManager.RegisterCondition(
                    GameConditionMaker.MakeCondition(GameConditionDefOfLocal.ToxicWeather, 90000));
            }
        }
    }
}