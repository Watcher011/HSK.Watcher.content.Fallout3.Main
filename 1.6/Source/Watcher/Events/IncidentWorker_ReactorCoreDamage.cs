using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;

//Этот код добавляет в игру событие, где ядро реактора (ReactorCoreA) получает критические повреждения, что приводит к взрыву, радиации и устойчивому пожару.

namespace Watcher.Events
{
    public class IncidentWorker_ReactorCoreDamage : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            Map map = (Map)parms.target;

            if (DefDatabase<ThingDef>.GetNamedSilentFail("ReactorCoreA") == null)
            {
                return false;
            }

            List<Thing> reactors = map.listerThings.ThingsOfDef(ThingDef.Named("ReactorCoreA"));
            return reactors != null && reactors.Count > 0;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            Map map = (Map)parms.target;

            List<Thing> reactors = map.listerThings.ThingsOfDef(ThingDef.Named("ReactorCoreA"));
            if (reactors == null || reactors.Count == 0)
            {
                return false;
            }

            Thing reactor = reactors.RandomElement();
            IntVec3 reactorPos = reactor.Position;

            // Повреждение реактора
            int targetHP = Mathf.RoundToInt(reactor.MaxHitPoints * Rand.Range(0.25f, 0.35f));
            reactor.HitPoints = targetHP;

            // Письмо на весь экран с использованием ThreatBig
            Letter letter = LetterMaker.MakeLetter(
                label: "LetterLabelReactorCoreCriticalDamage".Translate(),
                text: "LetterReactorCoreCriticalDamage".Translate(),
                def: LetterDefOf.ThreatBig,
                lookTargets: new LookTargets(reactor)
            );
            Find.LetterStack.ReceiveLetter(letter);

            // Добавляем мысли ДО взрыва
            AddThoughtsToColonists(map, reactorPos);

            // Взрыв БЕЗ звука
            GenExplosion.DoExplosion(
                center: reactorPos,
                map: map,
                radius: 5.0f,
                damType: DamageDefOf.Bomb,
                instigator: reactor,
                damAmount: 100,
                armorPenetration: 0.6f,
                applyDamageToExplosionCellsNeighbors: true
            );

            // Задержка перед поджогом и устойчивый огонь
            DelayedFireStarter fireStarter = new DelayedFireStarter(map);
            fireStarter.reactorPos = reactorPos;
            fireStarter.reactor = reactor;
            fireStarter.ticksToFire = 120;
            map.components.Add(fireStarter);

            // Спавн кастомной грязи Filth_NFRAD
            ThingDef filthDef = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_NFRAD");
            if (filthDef != null)
            {
                int spawnedCount = 0;

                foreach (IntVec3 cell in GenAdj.CellsAdjacent8Way(reactor))
                {
                    if (cell.InBounds(map) && cell.Walkable(map))
                    {
                        Thing filth = ThingMaker.MakeThing(filthDef);
                        if (filth != null)
                        {
                            GenPlace.TryPlaceThing(filth, cell, map, ThingPlaceMode.Near);
                            spawnedCount++;
                        }
                    }
                }

                foreach (IntVec3 cell in GenRadial.RadialCellsAround(reactorPos, 2f, true))
                {
                    if (cell.InBounds(map) && cell != reactorPos && Rand.Chance(0.5f))
                    {
                        Thing filth = ThingMaker.MakeThing(filthDef);
                        if (filth != null)
                        {
                            GenPlace.TryPlaceThing(filth, cell, map, ThingPlaceMode.Near);
                        }
                    }
                }

                if (spawnedCount > 0)
                {
                    Messages.Message(
                        "MessageRadiationLeak".Translate(),
                        reactor,
                        MessageTypeDefOf.NegativeEvent
                    );
                }
            }

            return true;
        }

        private void AddThoughtsToColonists(Map map, IntVec3 reactorPos)
        {
            foreach (Pawn pawn in map.mapPawns.FreeColonistsAndPrisonersSpawned)
            {
                if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike)
                    continue;

                if (pawn.needs?.mood?.thoughts?.memories == null)
                    continue;

                float distance = pawn.Position.DistanceTo(reactorPos);

                // ИСПРАВЛЕНИЕ: Добавляем мысли в нужды через ThoughtMaker.MakeThought
                // Мысль о взрыве для всех в радиусе 50 клеток
                if (distance < 50f)
                {
                    ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("ThoughtWitnessedReactorExplosion");
                    if (thoughtDef != null)
                    {
                        // Создаем мысль через ThoughtMaker и добавляем в память
                        Thought_Memory memory = (Thought_Memory)ThoughtMaker.MakeThought(thoughtDef);
                        if (memory != null)
                        {
                            pawn.needs.mood.thoughts.memories.TryGainMemory(memory, null);
                        }
                    }
                }

                // Облучение и мысль для всех в радиусе 50 клеток
                if (distance < 50f && Rand.Chance(0.7f))
                {
                    HediffDef radiationDef = DefDatabase<HediffDef>.GetNamedSilentFail("RadiationSickness");
                    if (radiationDef != null && pawn.health?.hediffSet != null)
                    {
                        pawn.health.AddHediff(radiationDef);
                    }

                    ThoughtDef irradiatedThought = DefDatabase<ThoughtDef>.GetNamedSilentFail("ThoughtIrradiated");
                    if (irradiatedThought != null)
                    {
                        // Создаем мысль через ThoughtMaker и добавляем в память
                        Thought_Memory memory = (Thought_Memory)ThoughtMaker.MakeThought(irradiatedThought);
                        if (memory != null)
                        {
                            pawn.needs.mood.thoughts.memories.TryGainMemory(memory, null);
                        }
                    }

                    if (radiationDef != null && pawn.health?.hediffSet != null)
                    {
                        Hediff existingRadiation = pawn.health.hediffSet.GetFirstHediffOfDef(radiationDef);
                        if (existingRadiation != null && existingRadiation.Severity > 0.7f && Rand.Chance(0.1f))
                        {
                            Messages.Message(
                                string.Format("GhoulReactorReaction".Translate(), pawn.Name.ToStringShort),
                                pawn,
                                MessageTypeDefOf.NeutralEvent
                            );
                        }
                    }
                }
            }
        }
    }

    public class DelayedFireStarter : MapComponent
    {
        public new Map map;
        public IntVec3 reactorPos;
        public Thing reactor;
        public int ticksToFire = 120;
        public int fireCheckInterval = 60;
        public int totalFireTicks = 600;
        private int currentFireTicks = 0;

        public DelayedFireStarter(Map map) : base(map)
        {
            this.map = map;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();

            if (ticksToFire > 0)
            {
                ticksToFire--;
                if (ticksToFire <= 0)
                {
                    StartFire();
                }
            }
            else if (currentFireTicks < totalFireTicks)
            {
                currentFireTicks++;

                if (currentFireTicks % fireCheckInterval == 0)
                {
                    MaintainFire();
                }
            }
            else
            {
                map.components.Remove(this);
            }
        }

        private void StartFire()
        {
            if (reactor != null && !reactor.Destroyed)
            {
                FireUtility.TryStartFireIn(reactorPos, map, 1.0f, reactor);

                if (reactorPos.GetFirstThing<Fire>(map) == null)
                {
                    Fire fire = (Fire)ThingMaker.MakeThing(ThingDefOf.Fire);
                    fire.fireSize = 1.0f;
                    GenSpawn.Spawn(fire, reactorPos, map);
                }
            }
        }

        private void MaintainFire()
        {
            if (reactor != null && !reactor.Destroyed)
            {
                Fire existingFire = reactorPos.GetFirstThing<Fire>(map);
                if (existingFire == null || existingFire.fireSize < 0.5f)
                {
                    FireUtility.TryStartFireIn(reactorPos, map, 0.8f, reactor);
                }
                else
                {
                    existingFire.fireSize = Mathf.Min(existingFire.fireSize + 0.1f, 1.5f);
                }
            }
        }
    }
}