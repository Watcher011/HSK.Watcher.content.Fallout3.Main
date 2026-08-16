using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;


//Анклав("Несущие демократию") - агрессивная фракция, которая не терпит конкурентов в добыче нефти.
//Если игрок строит более одной нефтяной вышки, Анклав организует усиленный рейд, чтобы уничтожить "незаконное" производство.

namespace Watcher.Events
{
    public class IncidentWorker_BringersofDemocracy : IncidentWorker
    {
        // Этот метод проверяет, может ли событие сработать в данный момент
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = (Map)parms.target;

            if (map == null)
                return false;

            // 1. Проверяем фракцию Энклава - существует ли она в игре
            Faction enclaveFaction = Find.FactionManager.FirstFactionOfDef(FactionDefMY.Enclave);
            if (enclaveFaction == null || enclaveFaction.defeated)
                return false;

            // 2. Проверяем отношения с фракцией Энклава
            if (enclaveFaction.RelationKindWith(Faction.OfPlayer) == FactionRelationKind.Ally)
                return false; // Не атакуют союзников

            // 3. Проверяем количество нефтяных вышек
            int oilWellCount = map.listerThings.AllThings
                .Count(thing => thing.def.defName == "OilWell" && thing.Faction == Faction.OfPlayer);

            if (oilWellCount <= 1)
                return false; // Нужно минимум 2 вышки

            // 4. Проверяем минимальное количество колонистов (опционально)
            if (map.mapPawns.FreeColonistsCount < 3)
                return false; // Не нападают на слишком маленькие колонии

            // 5. Проверяем, не слишком ли рано в игре
            if (GenDate.DaysPassedFloat < 15f)
                return false; // Даем игроку время подготовиться

            // 6. Проверяем, не было ли недавно других крупных нападений
            if (Find.TickManager.TicksGame < LastMajorRaidTick(map) + 60000 * 10) // 10 дней между крупными рейдами
                return false;

            return true; // Все условия выполнены
        }

        private int LastMajorRaidTick(Map map)
        {
            // Здесь можно добавить логику отслеживания последнего крупного рейда
            // Для простоты возвращаем 0
            return 0;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;

            // Проверяем есть ли больше 1х нефтяных вышек у игрока
            int oilWellCount = map.listerThings.AllThings
                .Count(thing => thing.def.defName == "OilWell" && thing.Faction == Faction.OfPlayer);

            if (oilWellCount <= 1)
                return false;

            // Сообщение игроку
            DiaNode node = new DiaNode("LetterLabelEnclave".Translate() + "\n\n" + "EnclaveProtection".Translate());
            DiaOption okOption = new DiaOption("OK".Translate());
            okOption.resolveTree = true;

            node.options.Add(okOption);

            Dialog_NodeTree dialog = new Dialog_NodeTree(node, true, false, "LetterLabelEnclave".Translate());
            Find.WindowStack.Add(dialog);
            Find.TickManager.slower.SignalForceNormalSpeedShort();

            // Настраиваем рейд
            parms.faction = Find.FactionManager.FirstFactionOfDef(FactionDefMY.Enclave);
            parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
            parms.points = StorytellerUtility.DefaultThreatPointsNow(map) * 1.6f; // Усиленный рейд

            // Запускаем стандартный рейд
            return IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
        }
    }
}
