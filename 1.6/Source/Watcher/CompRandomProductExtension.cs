using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;

//Этот код добавляет в игру механику случайного результата при крафте - при создании предмета есть шанс получить неожиданный результат:
//от обычных вещей до гранат, животных и врагов.


namespace Watcher.Comps
{
    public class RandomProductExtension : DefModExtension
    {
        public List<RandomProductOption> randomProducts = new List<RandomProductOption>();
        public HediffDef crafterHediff;
        public float crafterHediffSeverity = 0.5f;
        public BodyPartDef crafterBodyPart;
        public bool onlyHumanCrafters = true;
        public bool applyHediff = true;
    }

    public class RandomProductOption
    {
        public ThingDef thingDef;
        public int count = 1;
        public float weight = 1f;
        public bool spawnAsPawn = false;
        public bool tameIfAnimal = false;
        public bool spawnAsActiveGrenade = false;
        public int fuseTicks = 60;
        public string factionType = "Player";
    }

    public class RecipeWorker_RandomProduct : RecipeWorker
    {
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);

            var extension = this.recipe.GetModExtension<RandomProductExtension>();

            if (extension == null || extension.randomProducts.NullOrEmpty())
            {
                //Log.Error($"Recipe {this.recipe.defName} uses RecipeWorker_RandomProduct but has no <li Class=\"Watcher.Comps.RandomProductExtension\"> in modExtensions!");
                return;
            }

            if (extension.applyHediff && extension.crafterHediff != null && IsHumanCrafter(billDoer, extension))
            {
                ApplyCrafterHediff(billDoer, extension);
            }

            float totalWeight = extension.randomProducts.Sum(opt => opt.weight);
            float roll = Rand.Range(0f, totalWeight);

            float currentWeight = 0f;
            RandomProductOption selected = null;

            foreach (var option in extension.randomProducts)
            {
                currentWeight += option.weight;
                if (roll <= currentWeight)
                {
                    selected = option;
                    break;
                }
            }

            if (selected == null)
                selected = extension.randomProducts.First();

            if (selected.thingDef.race != null && selected.spawnAsPawn)
            {
                SpawnPawn(selected, billDoer, extension);
            }
            else if (selected.spawnAsActiveGrenade || IsProjectile(selected.thingDef))
            {
                SpawnActiveGrenade(selected, billDoer);
            }
            else
            {
                SpawnThing(selected, billDoer);
            }
        }

        private bool IsHumanCrafter(Pawn crafter, RandomProductExtension extension)
        {
            if (!extension.onlyHumanCrafters)
                return true;

            if (crafter.RaceProps.IsMechanoid)
                return false;

            if (crafter.def.defName != "Human")
                return false;

            return true;
        }

        private bool IsProjectile(ThingDef def)
        {
            return def.projectile != null;
        }

        private void ApplyCrafterHediff(Pawn crafter, RandomProductExtension extension)
        {
            BodyPartRecord part = null;
            if (extension.crafterBodyPart != null)
            {
                part = crafter.RaceProps.body.GetPartsWithDef(extension.crafterBodyPart).FirstOrDefault();
            }

            Hediff hediff = crafter.health.hediffSet.GetFirstHediffOfDef(extension.crafterHediff);

            if (hediff != null)
            {
                hediff.Severity += extension.crafterHediffSeverity;
            }
            else
            {
                hediff = HediffMaker.MakeHediff(extension.crafterHediff, crafter, part);
                hediff.Severity = extension.crafterHediffSeverity;
                crafter.health.AddHediff(hediff, part);
            }

          
        }

        private void SpawnThing(RandomProductOption option, Pawn billDoer)
        {
            Thing product = ThingMaker.MakeThing(option.thingDef);
            product.stackCount = option.count;

            CompQuality compQuality = product.TryGetComp<CompQuality>();
            if (compQuality != null)
            {
                compQuality.SetQuality(QualityUtility.GenerateQualityCreatedByPawn(billDoer, this.recipe.workSkill), ArtGenerationContext.Colony);
            }

            GenPlace.TryPlaceThing(product, billDoer.Position, billDoer.Map, ThingPlaceMode.Near);
            Messages.Message($"Produced: {product.def.label} x{option.count}", product, MessageTypeDefOf.PositiveEvent);
        }

        private void SpawnActiveGrenade(RandomProductOption option, Pawn billDoer)
        {
            for (int i = 0; i < option.count; i++)
            {
                Thing grenade = ThingMaker.MakeThing(option.thingDef);
                IntVec3 spawnPos = billDoer.Position;
                Map map = billDoer.Map;

                GenPlace.TryPlaceThing(grenade, spawnPos, map, ThingPlaceMode.Near);
                ActivateExplosive(grenade, option.fuseTicks);

               
            }
        }

        private void ActivateExplosive(Thing explosive, int fuseTicks)
        {
            // Пробуем стандартный CompExplosive
            CompExplosive compExplosive = explosive.TryGetComp<CompExplosive>();
            if (compExplosive != null)
            {
                compExplosive.StartWick(null);
                return;
            }

            // Для ThingWithComps пробуем получить компонент напрямую
            if (explosive is ThingWithComps thingWithComps)
            {
                foreach (var comp in thingWithComps.AllComps)
                {
                    if (comp is CompExplosive compExp)
                    {
                        compExp.StartWick(null);
                        return;
                    }
                }
            }

            // Принудительный взрыв через GenExplosion (упрощённая версия)
            if (explosive.def.projectile != null)
            {
                ProjectileProperties proj = explosive.def.projectile;

                try
                {
                    // Базовый взрыв без дополнительных параметров
                    GenExplosion.DoExplosion(
                        center: explosive.Position,
                        map: explosive.Map,
                        radius: proj.explosionRadius,
                        damType: proj.damageDef,
                        instigator: null
                    );
                }
                catch
                {
                    // Fallback: пробуем с бóльшим количеством параметров
                    try
                    {
                        GenExplosion.DoExplosion(
                            center: explosive.Position,
                            map: explosive.Map,
                            radius: proj.explosionRadius,
                            damType: proj.damageDef,
                            instigator: null,
                            damAmount: -1,
                            armorPenetration: -1f
                        );
                    }
                    catch
                    {
                        //Log.Warning($"[Watcher] Could not explode {explosive.def.defName}, destroying...");
                    }
                }

                explosive.Destroy();
            }
        }

        private void SpawnPawn(RandomProductOption option, Pawn billDoer, RandomProductExtension extension)
        {
            Faction spawnFaction = GetFaction(option.factionType, billDoer);

            for (int i = 0; i < option.count; i++)
            {
                PawnGenerationRequest request = new PawnGenerationRequest(
                    kind: PawnKindDef.Named(option.thingDef.defName) ?? PawnKindDefOf.Colonist,
                    faction: spawnFaction,
                    tile: billDoer.Map.Tile,
                    forceGenerateNewPawn: true,
                    allowDead: false,
                    allowDowned: false,
                    canGeneratePawnRelations: true,
                    mustBeCapableOfViolence: false,
                    colonistRelationChanceFactor: 1f
                );

                Pawn pawn = PawnGenerator.GeneratePawn(request);
                GenPlace.TryPlaceThing(pawn, billDoer.Position, billDoer.Map, ThingPlaceMode.Near);

                if (option.tameIfAnimal && pawn.RaceProps.Animal && spawnFaction != null && !spawnFaction.HostileTo(Faction.OfPlayer))
                {
                    pawn.training.Train(TrainableDefOf.Tameness, billDoer, true);
                }

                string factionText = (spawnFaction != null && spawnFaction.HostileTo(Faction.OfPlayer)) ? "Hostile" : "Friendly";
                Messages.Message($"Spawned {factionText}: {pawn.Label}", pawn,
                    (spawnFaction != null && spawnFaction.HostileTo(Faction.OfPlayer)) ? MessageTypeDefOf.ThreatBig : MessageTypeDefOf.PositiveEvent);
            }
        }

        private Faction GetFaction(string factionType, Pawn billDoer)
        {
            switch (factionType.ToLower())
            {
                case "player":
                    return Faction.OfPlayer;

                case "enemy":
                case "hostile":
                    return Find.FactionManager.AllFactions
                        .Where(f => f.HostileTo(Faction.OfPlayer) && !f.IsPlayer && !f.defeated && !f.Hidden)
                        .RandomElementWithFallback(Faction.OfAncientsHostile);

                case "neutral":
                    return Find.FactionManager.AllFactions
                        .Where(f => !f.HostileTo(Faction.OfPlayer) && !f.IsPlayer && !f.defeated)
                        .RandomElementWithFallback(Faction.OfAncients);

                case "wild":
                case "null":
                    return null;

                default:
                    return Faction.OfPlayer;
            }
        }
    }
}