using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;


//Этот код добавляет в игру событие, где по всей карте рассыпаются ловушки с охотничьими дронами, которые мгновенно активируются и атакуют колонию.

namespace Watcher.Events
{
    public class IncidentWorker_RedThreatHunterDrones : IncidentWorker
    {
        private const int DroneTrapCount = 10;
        private const float MinDistanceFromEdge = 5f;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            return TryFindSpawnCell(map, out IntVec3 _);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;
            List<Thing> spawnedTraps = new List<Thing>();

            // Получаем враждебную фракцию для дронов (механоиды или любая враждебная ультра-фракция)
            Faction enemyFaction = Find.FactionManager.AllFactions
                .Where(f => f.def.techLevel == TechLevel.Ultra && f.HostileTo(Faction.OfPlayer))
                .FirstOrDefault() ?? Find.FactionManager.OfMechanoids;

            // Спавним 20 ловушек с охотничьими дронами по всей карте под открытым небом
            for (int i = 0; i < DroneTrapCount; i++)
            {
                if (TryFindSpawnCell(map, out IntVec3 cell))
                {
                    Thing trap = ThingMaker.MakeThing(ThingDefOf.HunterDroneTrap);
                    if (trap != null)
                    {
                        // Устанавливаем враждебную фракцию перед размещением
                        trap.SetFaction(enemyFaction);

                        GenPlace.TryPlaceThing(trap, cell, map, ThingPlaceMode.Direct);
                        spawnedTraps.Add(trap);

                        // Принудительно активируем ловушку — вызываем Spring
                        TryActivateTrap(trap);
                    }
                }
            }

            if (spawnedTraps.Count == 0)
            {
                return false;
            }

            // Отправляем письмо вручную с keyed переводом
            string letterLabel = "RedThreat_LetterLabel".Translate();
            string letterText = "RedThreat_LetterText".Translate();

            Find.LetterStack.ReceiveLetter(
                letterLabel,
                letterText,
                LetterDefOf.ThreatSmall,
                spawnedTraps.FirstOrDefault(),
                enemyFaction
            );

            return true;
        }

        /// <summary>
        /// Принудительно активирует ловушку с дроном, заставляя его атаковать игрока
        /// </summary>
        private void TryActivateTrap(Thing trap)
        {
            if (trap == null) return;

            // HunterDroneTrap наследуется от Building_Trap — вызываем Spring()
            var springMethod = trap.GetType().GetMethod("Spring",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (springMethod != null)
            {
                springMethod.Invoke(trap, new object[] { null });
                return;
            }

            // Альтернативно: если есть компонент CompExplosive, активируем его
            var explosiveComp = trap.TryGetComp<CompExplosive>();
            if (explosiveComp != null)
            {
                var startWickMethod = explosiveComp.GetType().GetMethod("StartWick",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (startWickMethod != null)
                {
                    startWickMethod.Invoke(explosiveComp, new object[] { null, false });
                }
            }

            // Если это dormant thing с CompCanBeDormant, пробудить
            var dormantComp = trap.TryGetComp<CompCanBeDormant>();
            if (dormantComp != null)
            {
                var wakeUpMethod = dormantComp.GetType().GetMethod("WakeUp",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (wakeUpMethod != null)
                {
                    wakeUpMethod.Invoke(dormantComp, null);
                }
            }
        }

        private bool TryFindSpawnCell(Map map, out IntVec3 cell)
        {
            return CellFinder.TryFindRandomCell(
                map,
                (IntVec3 c) => IsValidSpawnCell(c, map),
                out cell);
        }

        private bool IsValidSpawnCell(IntVec3 c, Map map)
        {
            if (!c.Standable(map))
                return false;
            if (c.DistanceToEdge(map) < MinDistanceFromEdge)
                return false;
            if (map.roofGrid.Roofed(c))
                return false;
            if (c.Fogged(map))
                return false;
            if (!GenConstruct.CanBuildOnTerrain(ThingDefOf.HunterDroneTrap, c, map, Rot4.North, null, null))
                return false;
            List<Thing> things = c.GetThingList(map);
            if (things.Any(t => t.def == ThingDefOf.HunterDroneTrap))
                return false;
            return true;
        }
    }
}