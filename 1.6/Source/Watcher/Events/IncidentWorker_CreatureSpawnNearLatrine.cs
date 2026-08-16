using RimWorld;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

//Этот код добавляет в игру событие, где из уборной (выгребной ямы) появляется агрессивное существо, атакующее колонию.

namespace Watcher.Events
{
    public class IncidentWorker_SewageCreature : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;

            // Ищем уборные на карте
            Thing latrine = map.listerThings.ThingsOfDef(ThingDef.Named("PitLatrine")).FirstOrDefault();
            if (latrine == null) return false;

            // Показываем всплывающее окно (вместо письма)
            ShowPopupWindow();

            // Ищем клетку для спавна рядом с уборной
            IntVec3 spawnCell = CellFinder.RandomClosewalkCellNear(latrine.Position, map, 5);
            if (!spawnCell.IsValid || !spawnCell.Standable(map))
            {
                spawnCell = CellFinder.RandomClosewalkCellNear(latrine.Position, map, 10);
            }

            // Спавним существо
            Pawn creature = PawnGenerator.GeneratePawn(PawnKindDef.Named("SewageCreature"), Faction.OfInsects);
            GenSpawn.Spawn(creature, spawnCell, map, WipeMode.Vanish);

            // Настраиваем поведение для атаки построек и дверей
            ConfigureCreatureBehavior(creature, map);

            // Простые эффекты появления
            CreateSpawnEffects(spawnCell, map);

            // Все еще сохраняем письмо в лог (для истории)
            LogIncidentToHistory();

            // Принудительно нормальная скорость
            Find.TickManager.slower.SignalForceNormalSpeedShort();

            return true;
        }

        private void ShowPopupWindow()
        {
            // Простое диалоговое окно с переводом
            string text = "SewageCreatureIncidentLetterText".Translate();
            DiaNode node = new DiaNode(text);

            DiaOption okOption = new DiaOption("OK".Translate());
            okOption.resolveTree = true;
            node.options.Add(okOption);

            string title = "SewageCreatureIncidentLetterLabel".Translate();
            Dialog_NodeTree dialog = new Dialog_NodeTree(node, true, false, title);

            Find.WindowStack.Add(dialog);
        }

        private void ConfigureCreatureBehavior(Pawn creature, Map map)
        {
            // Маньяк (атакует всех живых существ)
            creature.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter);

            // Даем явную задачу атаковать
            GiveAttackJob(creature, map);
        }

        private void GiveAttackJob(Pawn creature, Map map)
        {
            if (creature.jobs == null) return;

            // Ищем цель для атаки
            LocalTargetInfo target = FindAttackTarget(map, creature.Position);

            if (target.IsValid && target.Thing != null)
            {
                Job attackJob = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
                attackJob.maxNumMeleeAttacks = 999;
                attackJob.expiryInterval = 2500;
                attackJob.attackDoorIfTargetLost = true; // Важно: атаковать двери если потерял цель
                creature.jobs.StartJob(attackJob, JobCondition.InterruptForced);
            }
            else
            {
                // Если нет целей, просто патрулируем с агрессивным поведением
                Job wanderJob = JobMaker.MakeJob(JobDefOf.Goto, GetRandomColonyPosition(map));
                wanderJob.expiryInterval = 1500;
                wanderJob.attackDoorIfTargetLost = true;
                creature.jobs.StartJob(wanderJob, JobCondition.InterruptForced);
            }
        }

        private LocalTargetInfo FindAttackTarget(Map map, IntVec3 position)
        {
            // 1. Сначала ищем ближайшего колониста
            Pawn colonist = map.mapPawns.FreeColonists
                .Where(p => p != null && !p.Dead && p.Spawned)
                .OrderBy(p => p.Position.DistanceToSquared(position))
                .FirstOrDefault();

            if (colonist != null) return new LocalTargetInfo(colonist);

            // 2. Ищем двери
            Thing door = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .Where(t => t.def.IsDoor && t.Faction == Faction.OfPlayer && !t.Destroyed)
                .OrderBy(t => t.Position.DistanceToSquared(position))
                .FirstOrDefault();

            if (door != null) return new LocalTargetInfo(door);

            // 3. Ищем другие постройки
            Thing building = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .Where(t => t.Faction == Faction.OfPlayer && !t.Destroyed && t.def.passability != Traversability.Impassable)
                .OrderBy(t => t.Position.DistanceToSquared(position))
                .FirstOrDefault();

            if (building != null) return new LocalTargetInfo(building);

            // 4. Возвращаем невалидную цель
            return LocalTargetInfo.Invalid;
        }

        private IntVec3 GetRandomColonyPosition(Map map)
        {
            var buildings = map.listerBuildings.allBuildingsColonist;
            if (buildings.Count > 0)
            {
                return buildings.RandomElement().Position;
            }
            return map.Center;
        }

        private void CreateSpawnEffects(IntVec3 spawnCell, Map map)
        {
            // Простые эффекты
            for (int i = 0; i < 3; i++)
            {
                FleckMaker.ThrowSmoke(spawnCell.ToVector3Shifted(), map, 1.5f);
                FleckMaker.ThrowDustPuff(spawnCell.ToVector3Shifted(), map, 1f);
            }

            // Используем существующий звук
            SoundDefOf.PsychicPulseGlobal.PlayOneShot(new TargetInfo(spawnCell, map));
        }

        private void LogIncidentToHistory()
        {
            // Сохраняем в лог писем
            Find.LetterStack.ReceiveLetter(
                "SewageCreatureIncidentLetterLabel".Translate(),
                "SewageCreatureIncidentLetterText".Translate(),
                LetterDefOf.ThreatSmall
            );
        }
    }
}