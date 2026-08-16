using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Watcher.Comps
{
    // ========== ОСНОВНОЙ РАБОЧИЙ ВАРИАНТ ==========
    public class CompProperties_SummonItem : CompProperties
    {
        public string summonedPawnKind = "Wolf_Timber";

        public CompProperties_SummonItem()
        {
            this.compClass = typeof(CompSummonItem);
        }
    }

    public class CompSummonItem : ThingComp
    {
        private CompProperties_SummonItem Props => (CompProperties_SummonItem)this.props;

        // 1. Кнопка в интерфейсе предмета
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (this.parent.Faction == Faction.OfPlayer && this.parent.Spawned)
            {
                Command_Action command = new Command_Action
                {
                    defaultLabel = "Призвать " + GetCreatureName(),
                    defaultDesc = "Немедленно призвать существо здесь",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Tame", true),
                    action = () => UseItemNow()
                };

                yield return command;
            }
        }

        // 2. Контекстное меню при правом клике на предмет - ИСПРАВЛЕНО
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            // Простая проверка доступности
            if (!selPawn.CanReach(this.parent, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption("Нельзя использовать: недоступно", null);
                yield break;
            }

            if (!selPawn.CanReserve(this.parent))
            {
                yield return new FloatMenuOption("Нельзя использовать: занято", null);
                yield break;
            }

            // Создаем опцию меню
            FloatMenuOption option = new FloatMenuOption(
                "Использовать " + this.parent.LabelCap + " (призвать " + GetCreatureName() + ")",
                () => UseItemWithPawn(selPawn)
            );

            yield return option;
        }

        private void UseItemNow()
        {
            if (this.parent.Map == null)
            {
                Messages.Message("Предмет должен быть на карте", MessageTypeDefOf.RejectInput);
                return;
            }

            // Находим ближайшего колониста
            Pawn nearestColonist = FindNearestColonist();
            if (nearestColonist == null)
            {
                Messages.Message("Нет колонистов рядом", MessageTypeDefOf.RejectInput);
                return;
            }

            // Призываем
            SummonCreature(nearestColonist);

            // Уничтожаем предмет
            this.parent.Destroy();
        }

        private void UseItemWithPawn(Pawn user)
        {
            // То же самое, но с конкретным колонистом
            SummonCreature(user);
            this.parent.Destroy();
        }

        private void SummonCreature(Pawn user)
        {
            if (user == null || user.Map == null) return;

            // Получаем тип существа
            PawnKindDef pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(Props.summonedPawnKind);
            if (pawnKindDef == null)
            {
                Log.Error($"CompSummonItem: Не найден {Props.summonedPawnKind}");
                return;
            }

            // Создаем существо
            PawnGenerationRequest request = new PawnGenerationRequest(
                kind: pawnKindDef,
                faction: Faction.OfPlayer,
                tile: user.Map.Tile,
                forceGenerateNewPawn: true
            );

            Pawn summoned = PawnGenerator.GeneratePawn(request);

            // Позиция рядом с предметом
            IntVec3 spawnPos = this.parent.Position;
            for (int i = 1; i <= 3; i++)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(this.parent.Position, i, true))
                {
                    if (cell.InBounds(user.Map) && cell.Standable(user.Map))
                    {
                        spawnPos = cell;
                        break;
                    }
                }
                if (spawnPos != this.parent.Position) break;
            }

            // Спавним
            GenSpawn.Spawn(summoned, spawnPos, user.Map);

            // Фракция игрока
            summoned.SetFaction(Faction.OfPlayer);

            // Настраиваем животное
            if (summoned.RaceProps.Animal)
            {
                if (summoned.training != null)
                {
                    summoned.training.Train(TrainableDefOf.Obedience, null, true);
                }

                if (summoned.playerSettings == null)
                {
                    summoned.playerSettings = new Pawn_PlayerSettings(summoned);
                }

                summoned.playerSettings.Master = user;
            }

            // Эффекты
            SoundDefOf.PsychicPulseGlobal.PlayOneShot(new TargetInfo(spawnPos, user.Map));
            FleckMaker.ThrowLightningGlow(spawnPos.ToVector3Shifted(), user.Map, 1f);

            // Сообщение
            Messages.Message(
                $"{summoned.LabelCap} присоединился к колонии!",
                summoned,
                MessageTypeDefOf.PositiveEvent
            );
        }

        private Pawn FindNearestColonist()
        {
            if (this.parent.Map == null) return null;

            Pawn nearest = null;
            float minDist = float.MaxValue;

            foreach (Pawn colonist in this.parent.Map.mapPawns.FreeColonists)
            {
                float dist = colonist.Position.DistanceTo(this.parent.Position);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = colonist;
                }
            }

            return nearest;
        }

        private string GetCreatureName()
        {
            PawnKindDef pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(Props.summonedPawnKind);
            return pawnKindDef?.LabelCap ?? Props.summonedPawnKind;
        }

        public override string CompInspectStringExtra()
        {
            return $"Призывает: {GetCreatureName()}";
        }
    }

    // ========== ВАРИАНТ С ВЫБОРОМ КОЛОНИСТА ==========
    public class CompProperties_SummonChoice : CompProperties
    {
        public string summonedPawnKind = "Wolf_Timber";

        public CompProperties_SummonChoice()
        {
            this.compClass = typeof(CompSummonChoice);
        }
    }

    public class CompSummonChoice : ThingComp
    {
        private CompProperties_SummonChoice Props => (CompProperties_SummonChoice)this.props;

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (this.parent.Faction == Faction.OfPlayer && this.parent.Spawned)
            {
                Command_Action command = new Command_Action
                {
                    defaultLabel = "Выбрать колониста",
                    defaultDesc = "Выберите кто будет использовать предмет",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/ForbidOff", true),
                    action = () => OpenColonistMenu()
                };

                yield return command;
            }
        }

        private void OpenColonistMenu()
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();

            foreach (Pawn colonist in this.parent.Map.mapPawns.FreeColonists)
            {
                // Простая проверка без параметра out
                bool canReach = colonist.CanReach(this.parent, PathEndMode.Touch, Danger.Deadly);

                if (canReach && colonist.CanReserve(this.parent))
                {
                    options.Add(new FloatMenuOption(
                        colonist.LabelShortCap,
                        () => UseWithColonist(colonist)
                    ));
                }
                else
                {
                    options.Add(new FloatMenuOption(
                        colonist.LabelShortCap + " (недоступно)",
                        null
                    ));
                }
            }

            if (options.Count > 0)
            {
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }

        private void UseWithColonist(Pawn colonist)
        {
            SummonCreature(colonist);
            this.parent.Destroy();
        }

        private void SummonCreature(Pawn user)
        {
            if (user == null || user.Map == null) return;

            PawnKindDef pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(Props.summonedPawnKind);
            if (pawnKindDef == null) return;

            PawnGenerationRequest request = new PawnGenerationRequest(
                kind: pawnKindDef,
                faction: Faction.OfPlayer,
                tile: user.Map.Tile,
                forceGenerateNewPawn: true
            );

            Pawn summoned = PawnGenerator.GeneratePawn(request);

            IntVec3 spawnPos = this.parent.Position;
            for (int i = 1; i <= 3; i++)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(this.parent.Position, i, true))
                {
                    if (cell.InBounds(user.Map) && cell.Standable(user.Map))
                    {
                        spawnPos = cell;
                        break;
                    }
                }
                if (spawnPos != this.parent.Position) break;
            }

            GenSpawn.Spawn(summoned, spawnPos, user.Map);
            summoned.SetFaction(Faction.OfPlayer);

            if (summoned.RaceProps.Animal)
            {
                if (summoned.training != null)
                    summoned.training.Train(TrainableDefOf.Obedience, null, true);

                if (summoned.playerSettings == null)
                    summoned.playerSettings = new Pawn_PlayerSettings(summoned);

                summoned.playerSettings.Master = user;
            }

            SoundDefOf.PsychicPulseGlobal.PlayOneShot(new TargetInfo(spawnPos, user.Map));
            FleckMaker.ThrowLightningGlow(spawnPos.ToVector3Shifted(), user.Map, 1f);

            Messages.Message($"{user.LabelShortCap} призвал {summoned.LabelCap}!", MessageTypeDefOf.PositiveEvent);
        }

        public override string CompInspectStringExtra()
        {
            var pawnKindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(Props.summonedPawnKind);
            return pawnKindDef != null ? $"Призывает: {pawnKindDef.LabelCap}" : "";
        }
    }
}