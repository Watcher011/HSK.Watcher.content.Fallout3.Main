using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

//Этот код добавляет в игру событие, где на консоль связи поступает таинственный вызов с угрозами, который приводит к рейду с десантированием в капсулах.

namespace Watcher.Events
{
    public class IncidentWorker_StrangeCall : IncidentWorker
    {
        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;

            if (map == null || !HasCommsConsole(map))
                return false;

            Faction enemyFaction = GetEnclaveFaction() ?? GetRandomHostileFaction();
            if (enemyFaction == null)
                return false;

            string combinedMessage = GenerateCombinedMessage();
            CreateDialog(combinedMessage, map, enemyFaction);

            return true;
        }

        private string GenerateCombinedMessage()
        {
            string firstMessage = "StrangeCall.Messages.Threat0".Translate();

            List<string> remainingMessages = new List<string>
        {
            "StrangeCall.Messages.Threat1",
            "StrangeCall.Messages.Threat2",
            "StrangeCall.Messages.Threat3"
        };

            string randomMessage = remainingMessages.RandomElement().Translate();

            return $"{firstMessage}\n\n{randomMessage}";
        }

        private Faction GetEnclaveFaction()
        {
            try
            {
                // Ищем фракцию Анклава по defName
                FactionDef enclaveDef = DefDatabase<FactionDef>.GetNamedSilentFail("Enclave");
                if (enclaveDef == null)
                {
                    //Log.Warning("StrangeCall: Enclave faction def not found, using random hostile faction");
                    return null;
                }

                Faction enclaveFaction = Find.FactionManager.FirstFactionOfDef(enclaveDef);
                if (enclaveFaction == null)
                {
                    //Log.Warning("StrangeCall: Enclave faction not found in game, using random hostile faction");
                    return null;
                }

                // Проверяем, что фракция активна
                if (enclaveFaction.defeated)
                {
                    //Log.Warning("StrangeCall: Enclave faction is defeated, using random hostile faction");
                    return null;
                }

                //Log.Message($"StrangeCall: Found Enclave faction - {enclaveFaction.Name}");
                return enclaveFaction;
            }
            catch (System.Exception ex)
            {
                //Log.Error("StrangeCall error getting Enclave faction: " + ex.Message);
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
                //Log.Error("StrangeCall error getting random hostile faction: " + ex.Message);
                return null;
            }
        }

        private void CreateDialog(string message, Map map, Faction faction)
        {
            try
            {
                bool isEnclave = faction.def.defName == "Enclave";
                string factionMessage = isEnclave ?
                    "StrangeCall.Messages.EnclaveInfo".Translate(faction.Name) :
                    "StrangeCall.Messages.FactionInfo".Translate(faction.Name);

                string fullMessage = $"{message}\n\n{factionMessage}";

                DiaNode node = new DiaNode(fullMessage);

                DiaOption answerOption = new DiaOption("ContinueCrolling".Translate());
                answerOption.action = () => {
                    ExecuteDropPodRaid(map, faction, isEnclave);
                    string raidMessage = isEnclave ?
                        "StrangeCall.Warnings.EnclaveRaidResponse".Translate(faction.Name) :
                        "StrangeCall.Warnings.RaidResponse".Translate(faction.Name);
                    Messages.Message(raidMessage, MessageTypeDefOf.ThreatBig);
                };
                answerOption.resolveTree = true;

                DiaOption ignoreOption = new DiaOption("Reset".Translate());
                ignoreOption.resolveTree = true;

                node.options = new List<DiaOption> { answerOption, ignoreOption };

                Find.WindowStack.Add(new Dialog_NodeTree(node, true, true, "StrangeCall.Titles.Main".Translate()));
            }
            catch (System.Exception ex)
            {
                //Log.Error("StrangeCall dialog error: " + ex.Message);
            }
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

        private void ExecuteDropPodRaid(Map map, Faction faction, bool isEnclave)
        {
            try
            {
                // Создаем параметры для рейда
                IncidentParms raidParms = new IncidentParms();
                raidParms.target = map;
                raidParms.faction = faction;
                raidParms.points = StorytellerUtility.DefaultThreatPointsNow(map) * (isEnclave ? 1.7f : 1.6f); // Анклав сильнее

                // Используем стратегию немедленной атаки
                raidParms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;

                // Устанавливаем режим прибытия - десант в капсулах
                raidParms.raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop;

                // Находим точку для десантирования
                raidParms.spawnCenter = FindDropCenter(map);
                raidParms.spawnRotation = Rot4.Random;

                //Log.Message($"StrangeCall: Starting {(isEnclave ? "Enclave" : "faction")} drop pod raid. Faction: {faction.Name}, Points: {raidParms.points}");

                // Запускаем рейд
                if (IncidentDefOf.RaidEnemy.Worker.CanFireNow(raidParms))
                {
                    bool success = IncidentDefOf.RaidEnemy.Worker.TryExecute(raidParms);
                    if (success)
                    {
                        //Log.Message($"StrangeCall: {(isEnclave ? "Enclave" : "Faction")} drop pod raid executed successfully");
                        CreateDropEffects(map, raidParms.spawnCenter, isEnclave);
                    }
                    else
                    {
                        //Log.Error($"StrangeCall: Failed to execute {(isEnclave ? "Enclave" : "faction")} drop pod raid");
                        // Fallback - обычный рейд
                        ExecuteStandardRaid(map, faction, isEnclave);
                    }
                }
                else
                {
                    //Log.Warning($"StrangeCall: Cannot fire {(isEnclave ? "Enclave" : "faction")} raid now, using standard raid");
                    ExecuteStandardRaid(map, faction, isEnclave);
                }
            }
            catch (System.Exception ex)
            {
                //Log.Error($"StrangeCall drop pod raid error: {ex.Message}");
                //Log.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        private IntVec3 FindDropCenter(Map map)
        {
            // Ищем точку в центре карты, но не на воде и не в горах
            for (int i = 0; i < 10; i++)
            {
                IntVec3 candidate = CellFinder.RandomCell(map);
                if (candidate.Standable(map) &&
                    !candidate.Fogged(map) &&
                    candidate.GetRoom(map)?.TouchesMapEdge == false &&
                    !candidate.Roofed(map))
                {
                    return candidate;
                }
            }

            // Запасной вариант - случайная клетка
            return CellFinder.RandomCell(map);
        }

        private void ExecuteStandardRaid(Map map, Faction faction, bool isEnclave)
        {
            try
            {
                IncidentParms raidParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
                raidParms.faction = faction;
                raidParms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;

                IncidentDefOf.RaidEnemy.Worker.TryExecute(raidParms);

                string raidMessage = isEnclave ?
                    "StrangeCall.Warnings.EnclaveStandardRaid".Translate(faction.Name) :
                    "StrangeCall.Warnings.StandardRaid".Translate(faction.Name);

                Messages.Message(raidMessage, MessageTypeDefOf.ThreatBig);
            }
            catch (System.Exception ex)
            {
                //Log.Error($"StrangeCall standard raid error: {ex.Message}");
            }
        }

        private void CreateDropEffects(Map map, IntVec3 center, bool isEnclave)
        {
            try
            {
                // Визуальные эффекты
                for (int i = 0; i < 5; i++)
                {
                    IntVec3 pos = center + new IntVec3(Rand.Range(-2, 2), 0, Rand.Range(-2, 2));
                    if (pos.InBounds(map))
                    {
                        FleckMaker.ThrowSmoke(pos.ToVector3Shifted(), map, 1.5f);

                        if (isEnclave)
                        {
                            // Специальные эффекты для Анклава - синие/энергетические
                            if (Rand.Value < 0.4f)
                            {
                                FleckMaker.ThrowLightningGlow(pos.ToVector3Shifted(), map, 0.8f);
                            }
                            if (Rand.Value < 0.3f)
                            {
                                FleckMaker.ThrowMicroSparks(pos.ToVector3Shifted(), map);
                            }
                        }
                        else
                        {
                            // Стандартные эффекты для других фракций
                            if (Rand.Value < 0.3f)
                            {
                                FleckMaker.ThrowLightningGlow(pos.ToVector3Shifted(), map, 0.8f);
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                //Log.Error($"StrangeCall drop effects error: {ex.Message}");
            }
        }
    }
}
