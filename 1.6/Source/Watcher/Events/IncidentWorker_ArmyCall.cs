using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

//Игрок может использовать консоль связи, чтобы вызвать военную поддержку. Однако это 50/50:

//50 % шанс - приходит реальная помощь(силовая броня)

//50 % шанс - это ловушка, и враги десантируются прямо в центр базы



namespace Watcher.Events
{
    public class IncidentWorker_ArmyCall : IncidentWorker
    {
        // Дни, которые должны пройти с начала игры
        private const float MinDaysPassed = 30f;
        // Минимальное количество колонистов
        private const int MinColonistCount = 4;
        // Минимальный уровень богатства (примерно)
        private const int MinWealth = 50000;
        // Минимальные дни с последнего вызова
        private const int MinDaysSinceLastCall = 20;

        // Статическая переменная для отслеживания последнего вызова
        private static int lastCallTick = -999999;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            try
            {
                Map map = (Map)parms.target;

                if (map == null)
                {
                    
                    return false;
                }

                // 1. Проверяем наличие консоли связи
                if (!HasCommsConsole(map))
                {
                 
                    return false;
                }

                // 2. Проверяем, прошло ли достаточно времени с начала игры
                float daysPassed = GenDate.DaysPassedFloat;
                if (daysPassed < MinDaysPassed)
                {
                   
                    return false;
                }

                // 3. Проверяем минимальное количество колонистов
                int colonistCount = map.mapPawns.FreeColonistsCount;
                if (colonistCount < MinColonistCount)
                {
                   
                    return false;
                }

                // 4. Проверяем уровень богатства колонии
                float colonyWealth = map.wealthWatcher.WealthTotal;
                if (colonyWealth < MinWealth)
                {
                  
                    return false;
                }

                // 5. Проверяем, не было ли недавно другого вызова
                int ticksSinceLastCall = Find.TickManager.TicksGame - lastCallTick;
                int daysSinceLastCall = ticksSinceLastCall / 60000;

                if (daysSinceLastCall < MinDaysSinceLastCall && lastCallTick > 0)
                {
                  
                    return false;
                }

                // 6. Проверяем, есть ли враждебные фракции для возможного десанта
                if (!HasValidHostileFactions())
                {
                   
                    return false;
                }

                // 7. Проверяем, не находится ли колония в кризисе (много раненых и т.д.)
                if (IsColonyInCrisis(map))
                {
                  
                    return false;
                }

                // 8. Проверяем, не слишком ли много врагов на карте
                if (HasTooManyEnemies(map))
                {
                  
                    return false;
                }

               
                return true;
            }
            catch (System.Exception ex)
            {
               
                return false;
            }
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;

            if (map == null || !HasCommsConsole(map))
                return false;

            // Запоминаем время вызова
            lastCallTick = Find.TickManager.TicksGame;

            CreateDialog(map);

            return true;
        }

        // ========== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ CanFireNowSub ==========

        private bool HasValidHostileFactions()
        {
            try
            {
                // Проверяем, есть ли фракция Анклава
                Faction enclaveFaction = GetEnclaveFaction();
                if (enclaveFaction != null && !enclaveFaction.defeated)
                {
                  
                    return true;
                }

                // Или любая другая враждебная фракция
                Faction anyHostile = GetRandomHostileFaction();
                if (anyHostile != null)
                {
                  
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsColonyInCrisis(Map map)
        {
            try
            {
                // Проверяем процент раненых колонистов
                int totalColonists = map.mapPawns.FreeColonistsCount;
                int downedColonists = map.mapPawns.FreeColonists.Count(p => p.Downed);

                float downedPercentage = totalColonists > 0 ? (float)downedColonists / totalColonists : 0f;

                // Если более 40% колонистов ранены или без сознания
                if (downedPercentage > 0.4f)
                {
                  
                    return true;
                }

                // Проверяем наличие крупных пожаров
                int fireCount = map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count;
                if (fireCount > 5) // Более 5 пожаров
                {
                 
                    return true;
                }

                // Исправленная проверка нападений - сравниваем с Danger.Deadly правильно
                StoryDanger currentDanger = map.dangerWatcher.DangerRating;

                // Правильное сравнение в RimWorld 1.6
                if (currentDanger == StoryDanger.High || currentDanger == StoryDanger.High)
                {
                  
                    return true;
                }

                // Альтернативный вариант: проверка есть ли активные враги
                bool hasActiveEnemies = map.mapPawns.AllPawns.Any(p =>
                    p.Faction != null &&
                    p.Faction.HostileTo(Faction.OfPlayer) &&
                    !p.Dead &&
                    !p.Downed &&
                    p.mindState != null &&
                    p.mindState.mentalStateHandler != null &&
                    (p.mindState.mentalStateHandler.CurStateDef == MentalStateDefOf.Berserk ||
                     p.mindState.mentalStateHandler.CurStateDef == MentalStateDefOf.Manhunter));

                if (hasActiveEnemies)
                {
                   
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool HasTooManyEnemies(Map map)
        {
            try
            {
                // Считаем врагов на карте
                int enemyCount = map.mapPawns.AllPawns.Count(p =>
                    p.Faction != null &&
                    p.Faction.HostileTo(Faction.OfPlayer) &&
                    !p.Dead &&
                    !p.Downed);

                // Если врагов больше чем колонистов * 2
                int colonistCount = map.mapPawns.FreeColonistsCount;
                if (enemyCount > colonistCount * 2)
                {
                  
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // ========== ОСТАЛЬНЫЕ МЕТОДЫ (остаются без изменений) ==========

        private void CreateDialog(Map map)
        {
            try
            {
                string message = "ArmyCall.Messages.Main".Translate();

                DiaNode node = new DiaNode(message);

                // Вариант "ДА Сэр" - 50% броня, 50% десант
                DiaOption yesOption = new DiaOption("ArmyCall.Answers.YesSir".Translate());
                yesOption.action = () => {
                    HandleYesResponse(map);
                };
                yesOption.resolveTree = true;

                // Вариант "Эм вы ошиблись" - ничего не происходит
                DiaOption noOption = new DiaOption("ArmyCall.Answers.Mistake".Translate());
                noOption.resolveTree = true;

                node.options = new List<DiaOption> { yesOption, noOption };

                Find.WindowStack.Add(new Dialog_NodeTree(node, true, true, "ArmyCall.Titles.Main".Translate()));
            }
            catch (System.Exception ex)
            {
                //Log.Error("ArmyCall dialog error: " + ex.Message);
            }
        }

        private void HandleYesResponse(Map map)
        {
            // 50% шанс на вражеский десант, 50% на броню
            if (Rand.Value < 0.5f)
            {
                ExecuteEnemyDrop(map);
                Messages.Message("ArmyCall.Warnings.TrapResponse".Translate(),
                               MessageTypeDefOf.ThreatBig);
            }
            else
            {
                GivePowerArmor(map);
                Messages.Message("ArmyCall.Warnings.YesResponse".Translate(),
                               MessageTypeDefOf.PositiveEvent);
            }
        }

        private void ExecuteEnemyDrop(Map map)
        {
            try
            {
                Faction enemyFaction = GetEnclaveFaction() ?? GetRandomHostileFaction();
                if (enemyFaction == null)
                {
                    // Если нет вражеских фракций, даем броню
                    GivePowerArmor(map);
                    return;
                }

                bool isEnclave = enemyFaction.def.defName == "Enclave";

                IncidentParms raidParms = new IncidentParms
                {
                    target = map,
                    faction = enemyFaction,
                    points = StorytellerUtility.DefaultThreatPointsNow(map) * (isEnclave ? 1.4f : 1.3f), // Анклав сильнее
                    raidStrategy = RaidStrategyDefOf.ImmediateAttack,
                    raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop
                };

                // Находим точку для десантирования в центре базы
                raidParms.spawnCenter = FindDropCenter(map);

                if (IncidentDefOf.RaidEnemy.Worker.CanFireNow(raidParms))
                {
                    IncidentDefOf.RaidEnemy.Worker.TryExecute(raidParms);
                    CreateDropEffects(map, raidParms.spawnCenter, isEnclave);

                    string raidMessage = isEnclave ?
                        "ArmyCall.Warnings.EnclaveTrap".Translate() :
                        "ArmyCall.Warnings.TrapResponse".Translate();
                    Messages.Message(raidMessage, MessageTypeDefOf.ThreatBig);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"ArmyCall enemy drop error: {ex.Message}");
                // При ошибке даем броню
                GivePowerArmor(map);
            }
        }

        private Faction GetEnclaveFaction()
        {
            try
            {
                // Ищем фракцию Анклава по defName
                FactionDef enclaveDef = DefDatabase<FactionDef>.GetNamedSilentFail("Enclave");
                if (enclaveDef == null)
                {
                    //Log.Warning("ArmyCall: Enclave faction def not found, using random hostile faction");
                    return null;
                }

                Faction enclaveFaction = Find.FactionManager.FirstFactionOfDef(enclaveDef);
                if (enclaveFaction == null)
                {
                    //Log.Warning("ArmyCall: Enclave faction not found in game, using random hostile faction");
                    return null;
                }

                // Проверяем, что фракция активна
                if (enclaveFaction.defeated)
                {
                    //Log.Warning("ArmyCall: Enclave faction is defeated, using random hostile faction");
                    return null;
                }

               
                return enclaveFaction;
            }
            catch (System.Exception ex)
            {
                //Log.Error("ArmyCall error getting Enclave faction: " + ex.Message);
                return null;
            }
        }

        private Faction GetRandomHostileFaction()
        {
            try
            {
                return Find.FactionManager.AllFactions
                    .Where(f => f.HostileTo(Faction.OfPlayer) &&
                           !f.def.hidden &&
                           !f.IsPlayer &&
                           !f.defeated &&
                           f.def.pawnGroupMakers != null &&
                           f.def.pawnGroupMakers.Any(pgm => pgm.kindDef == PawnGroupKindDefOf.Combat))
                    .RandomElementWithFallback();
            }
            catch (System.Exception ex)
            {
                //Log.Error("ArmyCall error getting hostile faction: " + ex.Message);
                return null;
            }
        }

        private IntVec3 FindDropCenter(Map map)
        {
            // Ищем точку в центре колонии
            List<Thing> colonyBuildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .Where(b => b.Faction == Faction.OfPlayer)
                .ToList();

            if (colonyBuildings.Count > 0)
            {
                Thing centralBuilding = colonyBuildings.OrderBy(b => b.Position.DistanceTo(map.Center)).First();
                if (CellFinder.TryFindRandomCellNear(centralBuilding.Position, map, 8,
                    cell => cell.Standable(map) && !cell.Fogged(map) && !cell.Roofed(map),
                    out IntVec3 dropPos))
                {
                    return dropPos;
                }
            }

            return map.Center;
        }

        private void CreateDropEffects(Map map, IntVec3 center, bool isEnclave)
        {
            try
            {
                // Визуальные эффекты вражеского десанта
                for (int i = 0; i < 6; i++)
                {
                    IntVec3 pos = center + new IntVec3(Rand.Range(-3, 3), 0, Rand.Range(-3, 3));
                    if (pos.InBounds(map))
                    {
                        FleckMaker.ThrowSmoke(pos.ToVector3Shifted(), map, 2f);

                        if (isEnclave)
                        {
                            // Специальные эффекты для Анклава
                            if (Rand.Value < 0.5f)
                            {
                                FleckMaker.ThrowLightningGlow(pos.ToVector3Shifted(), map, 1.2f);
                            }
                            if (Rand.Value < 0.3f)
                            {
                                FleckMaker.ThrowMicroSparks(pos.ToVector3Shifted(), map);
                            }
                        }
                        else
                        {
                            // Стандартные эффекты для других фракций
                            if (Rand.Value < 0.4f)
                            {
                                FleckMaker.ThrowLightningGlow(pos.ToVector3Shifted(), map, 1.2f);
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                //Log.Error($"ArmyCall drop effects error: {ex.Message}");
            }
        }

        private void GivePowerArmor(Map map)
        {
            try
            {
                ThingDef powerArmorDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_VaultX01PowerArmor");
                ThingDef helmetDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_VaultX01ArmorHelmet");

                if (powerArmorDef == null || helmetDef == null)
                {
                    GiveFallbackItems(map);
                    return;
                }

                Thing powerArmor = ThingMaker.MakeThing(powerArmorDef);
                Thing helmet = ThingMaker.MakeThing(helmetDef);

                powerArmor.HitPoints = powerArmor.MaxHitPoints;
                helmet.HitPoints = helmet.MaxHitPoints;

                IntVec3 spawnPos = FindSpawnPosition(map);

                GenSpawn.Spawn(powerArmor, spawnPos, map);
                GenSpawn.Spawn(helmet, spawnPos, map);

                Messages.Message("ArmyCall.Items.Received".Translate(), MessageTypeDefOf.PositiveEvent);
            }
            catch (System.Exception ex)
            {
                //Log.Error($"ArmyCall error giving power armor: {ex.Message}");
                Messages.Message("ArmyCall.Errors.SpawnFailed".Translate(), MessageTypeDefOf.NegativeEvent);
            }
        }

        private IntVec3 FindSpawnPosition(Map map)
        {
            Building commsConsole = map.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.CommsConsole).FirstOrDefault();
            IntVec3 searchCenter = commsConsole?.Position ?? map.Center;

            if (CellFinder.TryFindRandomCellNear(searchCenter, map, 5,
                cell => cell.Standable(map) && cell.GetFirstItem(map) == null,
                out IntVec3 spawnPos))
            {
                return spawnPos;
            }

            return CellFinder.RandomCell(map);
        }

        private void GiveFallbackItems(Map map)
        {
            ThingDef fallbackArmor = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_PowerArmor") ??
                                  ThingDefOfMYLocal.Apparel_VaultX01PowerArmor;
            ThingDef fallbackHelmet = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_PowerArmorHelmet") ??
                                    ThingDefOfMYLocal.Apparel_VaultX01ArmorHelmet;

            IntVec3 spawnPos = FindSpawnPosition(map);

            if (fallbackArmor != null)
            {
                Thing armor = ThingMaker.MakeThing(fallbackArmor);
                armor.HitPoints = armor.MaxHitPoints;
                GenSpawn.Spawn(armor, spawnPos, map);
            }

            if (fallbackHelmet != null)
            {
                Thing helmet = ThingMaker.MakeThing(fallbackHelmet);
                helmet.HitPoints = helmet.MaxHitPoints;
                GenSpawn.Spawn(helmet, spawnPos + new IntVec3(1, 0, 0), map);
            }

            Messages.Message("ArmyCall.Items.FallbackReceived".Translate(), MessageTypeDefOf.PositiveEvent);
        }

        private bool HasCommsConsole(Map map)
        {
            try
            {
                return map.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.CommsConsole).Any();
            }
            catch
            {
                return false;
            }
        }
    }
}