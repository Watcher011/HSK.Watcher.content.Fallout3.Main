using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;


//Этот код добавляет в игру событие, где из карьеров (шахт, копей) вылезают агрессивные кротокрысы (MoleRats) и атакуют колонию.

namespace Watcher.Events
{
    public class IncidentWorker_QuarryAnimalAttack : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;

            // Проверка населения колонии - правильный способ
            if (map.mapPawns.FreeColonistsCount < 4)
                return false;

            List<Thing> quarries = map.listerThings.ThingsOfDef(ThingDef.Named("QRY_Quarry"))
                .Concat(map.listerThings.ThingsOfDef(ThingDef.Named("QRY_MediQuarry")))
                .Concat(map.listerThings.ThingsOfDef(ThingDef.Named("QRY_MiniQuarry")))
                .ToList();

            if (!quarries.Any())
                return false;

            Thing quarry = quarries.RandomElement();
            int count = Rand.RangeInclusive(3, 5);
            PawnKindDef moleRat = PawnKindDef.Named("MoleRat");

            // Устанавливаем фракцию для параметров инцидента
            parms.faction = Find.FactionManager.FirstFactionOfDef(FactionDef.Named("Monster"));
            if (parms.faction == null)
            {
                parms.faction = Faction.OfInsects;
            }

            for (int i = 0; i < count; i++)
            {
                IntVec3 spawnCell = CellFinder.RandomClosewalkCellNear(quarry.Position, map, 5);

                // Генерация pawn с фракцией из параметров инцидента
                Pawn newMoleRat = PawnGenerator.GeneratePawn(moleRat, parms.faction);

                // Заменяем Hediff на перманентную ярость
                if (i == 0 && Rand.Chance(0.3f))
                {
                    // Для "Старосты Норы" делаем перманентную ярость
                    newMoleRat.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent);
                    newMoleRat.Name = new NameSingle("MoleRatQuarryAttack.unionRepresentative".Translate());
                }
                else
                {
                    // Для обычных кротокрысов временная ярость
                    newMoleRat.mindState.mentalStateHandler.TryStartMentalState(
                        MentalStateDefOf.Manhunter,
                        "MoleRatQuarryAttack.mentalStateReason".Translate(),
                        true
                    );
                }

                GenSpawn.Spawn(newMoleRat, spawnCell, map);
            }

            // Основное письмо с параметром количества
            string mainLetter = "MoleRatQuarryAttack.letterText".Translate(count);

            // Расширенное сатирическое письмо
            string extendedLetter = "MoleRatQuarryAttack.letterTextExtended".Translate();

            // Выбор случайного варианта письма
            string finalLetter = Rand.Chance(0.7f) ? mainLetter : extendedLetter;
            string letterLabel = Rand.Chance(0.7f) ?
                "MoleRatQuarryAttack.letterLabel".Translate() :
                "MoleRatQuarryAttack.laborDispute".Translate();

            Find.LetterStack.ReceiveLetter(letterLabel, finalLetter, this.def.letterDef, new LookTargets(quarry.Position, map));

            return true;
        }
    }
}