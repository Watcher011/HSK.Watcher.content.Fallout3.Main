using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI.Group;

//Этот код добавляет в игру сложное событие, связанное с таинственной фракцией "Анклав" (Enclave), которая пытается завербовать колонистов через радиоволны.

namespace Watcher.Events
{

    public static class EnclaveDebug
    {
        public const bool ENABLED = true;

        public static void Log(string message)
        {
            if (ENABLED)
                Verse.Log.Message($"[EnclaveDebug] {message}");
        }

        public static void Warning(string message)
        {
            if (ENABLED)
                Verse.Log.Warning($"[EnclaveDebug] {message}");
        }

        public static void Error(string message)
        {
            Verse.Log.Error($"[EnclaveDebug] {message}");
        }
    }

    [DefOf]
    public static class EnclaveDefOf
    {
        public static ThingDef MusicRadio;
        public static HediffDef MysteriousInfection;
        public static FactionDef Enclave;
        public static IncidentDef EnclaveRecruitment;
        public static ThoughtDef ThreeDogBlues;
        public static ThingDef CE_Embrasure;

        static EnclaveDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(EnclaveDefOf));
        }
    }

    public class IncidentWorker_EnclaveRecruitment : IncidentWorker
    {
        private const int MIN_RADIOS_REQUIRED = 3;
        private const float INFECTION_CHANCE = 0.30f;
        private const float BLUES_CHANCE = 0.35f;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!(parms.target is Map map))
            {
                EnclaveDebug.Log("CanFireNowSub: target is not Map");
                return false;
            }

            int radioCount = CountWorkingRadios(map);
            EnclaveDebug.Log($"CanFireNowSub: Found {radioCount} working radios (need {MIN_RADIOS_REQUIRED}+)");

            return radioCount >= MIN_RADIOS_REQUIRED;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            EnclaveDebug.Log("=== Enclave Recruitment Incident Started ===");

            Map map = (Map)parms.target;

            int radioCount = CountWorkingRadios(map);
            if (radioCount < MIN_RADIOS_REQUIRED)
            {
                EnclaveDebug.Warning($"Not enough radios! Have {radioCount}, need {MIN_RADIOS_REQUIRED}");
                return false;
            }

            Pawn targetPawn = GetRandomColonist(map);
            if (targetPawn == null)
            {
                EnclaveDebug.Warning("No valid colonist found!");
                return false;
            }

            EnclaveDebug.Log($"Target colonist selected: {targetPawn.Name}");

            float roll = Rand.Value;
            EnclaveDebug.Log($"Roll = {roll:F3}");

            if (roll < INFECTION_CHANCE)
            {
                EnclaveDebug.Log("Result = INFECTION (hidden) + SLEEPER AGENT");
                InfectPawn(targetPawn);
                SendDeceptiveLetter();
            }
            else if (roll < INFECTION_CHANCE + BLUES_CHANCE)
            {
                EnclaveDebug.Log("Result = THREE DOG BLUES (positive)");
                ApplyBluesEffect(map);
                SendBluesLetter();
            }
            else
            {
                EnclaveDebug.Log("Result = STATIC (neutral)");
                SendStaticLetter();
            }

            EnclaveDebug.Log("=== Enclave Recruitment Incident Completed ===");
            return true;
        }

        private int CountWorkingRadios(Map map)
        {
            var radios = map.listerBuildings.AllBuildingsColonistOfDef(EnclaveDefOf.MusicRadio);
            int count = 0;

            foreach (var building in radios)
            {
                var powerComp = building.TryGetComp<CompPowerTrader>();
                bool isWorking = powerComp?.PowerOn == true && !building.IsBrokenDown();
                if (isWorking) count++;
            }

            return count;
        }

        private Pawn GetRandomColonist(Map map)
        {
            List<Pawn> colonists = new List<Pawn>();
            foreach (Pawn p in map.mapPawns.FreeColonists)
            {
                if (p.RaceProps.Humanlike && !p.Dead)
                    colonists.Add(p);
            }

            if (colonists.Count == 0) return null;
            return colonists.RandomElement();
        }

        private void InfectPawn(Pawn pawn)
        {
            Hediff hediff = HediffMaker.MakeHediff(EnclaveDefOf.MysteriousInfection, pawn);
            pawn.health.AddHediff(hediff);
        }

        private void ApplyBluesEffect(Map map)
        {
            foreach (Pawn pawn in map.mapPawns.FreeColonists)
            {
                if (!pawn.Dead && pawn.RaceProps.Humanlike)
                {
                    pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(EnclaveDefOf.ThreeDogBlues);
                }
            }
        }

        private void SendDeceptiveLetter()
        {
            string title = "EnclaveRecruitment_DeceptiveTitle".Translate();
            string text = "EnclaveRecruitment_DeceptiveText".Translate();
            Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.NeutralEvent);
        }

        private void SendBluesLetter()
        {
            string title = "EnclaveRecruitment_BluesTitle".Translate();
            string text = "EnclaveRecruitment_BluesText".Translate();
            Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.PositiveEvent);
        }

        private void SendStaticLetter()
        {
            string title = "EnclaveRecruitment_StaticTitle".Translate();
            string text = "EnclaveRecruitment_StaticText".Translate();
            Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.NeutralEvent);
        }
    }

    public class HediffComp_SleeperAgent : HediffComp
    {
        private const float EMBRASURE_DESTROY_PERCENT = 0.09f;
        private const float EXPLOSION_RADIUS = 3.9f;

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            EnclaveDebug.Log($"SleeperAgent: Hediff expired on {Pawn.Name}");

            if (Pawn.Dead)
            {
                EnclaveDebug.Log("SleeperAgent: Pawn is dead, aborting");
                return;
            }

            if (Pawn.Faction != Faction.OfPlayer)
            {
                EnclaveDebug.Log($"SleeperAgent: Pawn not in player faction (current: {Pawn.Faction?.Name}), aborting");
                return;
            }

            Map map = Pawn.Map;
            if (map == null)
            {
                EnclaveDebug.Log("SleeperAgent: Pawn is not on a map, aborting");
                return;
            }

            Faction enclave = GetOrCreateEnclaveFaction();
            if (enclave == null)
            {
                EnclaveDebug.Error("SleeperAgent: Cannot find or create Enclave faction!");
                return;
            }

            SetFactionHostile(enclave, Faction.OfPlayer);

            string pawnName = Pawn.Name.ToStringFull;
            EnclaveDebug.Log($"SleeperAgent: Converting {pawnName} to Enclave faction");

            Pawn.SetFaction(enclave);
            Pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter);

            string title = "EnclaveRecruitment_BetrayalTitle".Translate();
            string text = "EnclaveRecruitment_BetrayalText".Translate(pawnName);
            Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.ThreatBig, new LookTargets(Pawn));

            EnclaveDebug.Log($"SleeperAgent: {pawnName} is now hostile Enclave agent!");

            SpawnEnclaveRaid(map, enclave);
            DestroyRandomEmbrasures(map);
        }

        private Faction GetOrCreateEnclaveFaction()
        {
            Faction enclave = Find.FactionManager.FirstFactionOfDef(EnclaveDefOf.Enclave);

            if (enclave == null)
            {
                EnclaveDebug.Log("SleeperAgent: Creating new Enclave faction");
                FactionGeneratorParms parms = new FactionGeneratorParms(EnclaveDefOf.Enclave, default, true);
                enclave = FactionGenerator.NewGeneratedFaction(parms);

                if (enclave != null)
                {
                    Find.FactionManager.Add(enclave);
                }
            }

            return enclave;
        }

        private void SetFactionHostile(Faction faction, Faction other)
        {
            faction.TryAffectGoodwillWith(other, -200, canSendMessage: false, canSendHostilityLetter: false);
            EnclaveDebug.Log($"SetFactionHostile: {faction.Name} goodwill with {other.Name} is now {faction.GoodwillWith(other)}");
        }

        private void SpawnEnclaveRaid(Map map, Faction enclave)
        {
            EnclaveDebug.Log("SleeperAgent: Spawning Enclave raid from map edge");

            float points = StorytellerUtility.DefaultThreatPointsNow(map) * 2.0f;
            points = Mathf.Clamp(points, 200f, 3000f);

            PawnGroupMakerParms groupParms = new PawnGroupMakerParms
            {
                tile = map.Tile,
                faction = enclave,
                points = points,
                groupKind = PawnGroupKindDefOf.Combat
            };

            List<Pawn> raiders = PawnGroupMakerUtility.GeneratePawns(groupParms).ToList();

            if (raiders.Count == 0)
            {
                EnclaveDebug.Warning("SleeperAgent: No raiders generated!");
                return;
            }

            foreach (Pawn raider in raiders)
            {
                IntVec3 spawnCell = FindValidSpawnCell(map);
                GenPlace.TryPlaceThing(raider, spawnCell, map, ThingPlaceMode.Near);
            }

            LordJob_AssaultColony lordJob = new LordJob_AssaultColony(enclave, canKidnap: true, canTimeoutOrFlee: true);
            LordMaker.MakeNewLord(enclave, lordJob, map, raiders);

            string title = "EnclaveRecruitment_RaidTitle".Translate();
            string text = "EnclaveRecruitment_RaidText".Translate(enclave.Name, raiders.Count);
            Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.ThreatBig, new LookTargets(raiders));

            EnclaveDebug.Log($"SleeperAgent: Raid spawned with {raiders.Count} raiders, {points:F0} points");
        }

        private IntVec3 FindValidSpawnCell(Map map)
        {
            for (int i = 0; i < 20; i++)
            {
                IntVec3 edgeCell = CellFinder.RandomEdgeCell(map);

                if (edgeCell.IsValid && edgeCell.InBounds(map) && edgeCell.Standable(map))
                {
                    return edgeCell;
                }

                IntVec3 nearbyCell = IntVec3.Invalid;
                if (TryFindNearbyStandableCell(edgeCell, map, out nearbyCell))
                {
                    return nearbyCell;
                }
            }

            return DropCellFinder.FindRaidDropCenterDistant(map);
        }

        private bool TryFindNearbyStandableCell(IntVec3 center, Map map, out IntVec3 result)
        {
            result = IntVec3.Invalid;

            for (int x = -5; x <= 5; x++)
            {
                for (int z = -5; z <= 5; z++)
                {
                    IntVec3 checkCell = center + new IntVec3(x, 0, z);
                    if (checkCell.InBounds(map) && checkCell.Standable(map) && !checkCell.Fogged(map))
                    {
                        result = checkCell;
                        return true;
                    }
                }
            }

            return false;
        }

        private void DestroyRandomEmbrasures(Map map)
        {
            if (EnclaveDefOf.CE_Embrasure == null)
            {
                EnclaveDebug.Warning("SleeperAgent: CE_Embrasure def not found, skipping embrasure destruction");
                return;
            }

            List<Building> embrasures = map.listerBuildings.AllBuildingsColonistOfDef(EnclaveDefOf.CE_Embrasure).ToList();

            if (embrasures.Count == 0)
            {
                EnclaveDebug.Log("SleeperAgent: No embrasures found on map");
                return;
            }

            EnclaveDebug.Log($"SleeperAgent: Found {embrasures.Count} embrasures, destroying {EMBRASURE_DESTROY_PERCENT:P0}");

            embrasures.Shuffle();
            int toDestroy = Mathf.Max(1, Mathf.RoundToInt(embrasures.Count * EMBRASURE_DESTROY_PERCENT));

            List<Building> targets = new List<Building>();
            for (int i = 0; i < toDestroy && i < embrasures.Count; i++)
            {
                targets.Add(embrasures[i]);
            }

            List<IntVec3> explosionCells = new List<IntVec3>();

            foreach (Building embrasure in targets)
            {
                if (embrasure.Destroyed) continue;

                IntVec3 pos = embrasure.Position;
                explosionCells.Add(pos);
                embrasure.Destroy(DestroyMode.KillFinalize);
            }

            foreach (IntVec3 cell in explosionCells)
            {
                GenExplosion.DoExplosion(
                    center: cell,
                    map: map,
                    radius: EXPLOSION_RADIUS,
                    damType: DamageDefOf.Bomb,
                    instigator: null,
                    damAmount: 50,
                    armorPenetration: 0.5f,
                    weapon: null,
                    projectile: null,
                    intendedTarget: null,
                    applyDamageToExplosionCellsNeighbors: true,
                    preExplosionSpawnThingDef: null,
                    preExplosionSpawnChance: 0f,
                    preExplosionSpawnThingCount: 1,
                    postExplosionSpawnThingDef: ThingDefOf.Filth_RubbleRock,
                    postExplosionSpawnChance: 0.5f,
                    postExplosionSpawnThingCount: 1,
                    chanceToStartFire: 0.1f,
                    damageFalloff: true
                );
            }

            if (targets.Count > 0)
            {
                string title = "EnclaveRecruitment_DefenseDestroyedTitle".Translate();
                string text = "EnclaveRecruitment_DefenseDestroyedText".Translate(targets.Count);

                List<TargetInfo> targetInfos = new List<TargetInfo>();
                foreach (IntVec3 cell in explosionCells)
                {
                    targetInfos.Add(new TargetInfo(cell, map));
                }

                Find.LetterStack.ReceiveLetter(title, text, LetterDefOf.NegativeEvent, new LookTargets(targetInfos));
            }

            EnclaveDebug.Log($"SleeperAgent: Destroyed {targets.Count} embrasures with explosions");
        }
    }

    public class HediffCompProperties_SleeperAgent : HediffCompProperties
    {
        public HediffCompProperties_SleeperAgent()
        {
            compClass = typeof(HediffComp_SleeperAgent);
        }
    }
}