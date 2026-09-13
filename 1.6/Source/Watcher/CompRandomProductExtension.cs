using RimWorld;
using Verse;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// =====================================================================================
// МЕХАНИКА "СЛУЧАЙНЫЙ РЕЗУЛЬТАТ КРАФТА"
// =====================================================================================
//
// Этот код добавляет в игру рецепт, у которого при завершении каждой итерации крафта
// есть шанс получить один из нескольких заранее заданных в XML результатов:
// от обычного предмета до активной гранаты, дружественного/враждебного/дикого существа.
//
// СИСТЕМА ВЕСОВ ОТ НАВЫКА (0-20, как в игре):
// Каждый вариант результата (RandomProductOption) задаёт свой вес НЕ одним числом,
// а двумя: weightAtSkill0 (вес при навыке 0) и weightAtSkill20 (вес при навыке 20).
// Между этими двумя точками вес линейно интерполируется под текущий уровень навыка
// крафтера (Mathf.Lerp). Так можно без единого отрицательного числа сделать:
//   - "мусорные" варианты реже выпадающими у опытных мастеров (weightAtSkill0 > weightAtSkill20)
//   - "ценные" варианты чаще выпадающими у опытных мастеров (weightAtSkill0 < weightAtSkill20)
//   - варианты, вообще не зависящие от навыка (weightAtSkill0 == weightAtSkill20)
//
// Дополнительно у каждого варианта есть minSkillLevel - жёсткий порог: если текущий
// навык крафтера ниже этого значения, вариант полностью исключается из розыгрыша
// (а не просто получает маленький вес).
//
// КАЧЕСТВО РЕЗУЛЬТАТА (для предметов, у которых оно вообще бывает - оружие, броня и т.п.):
// У каждого варианта можно задать fixedQuality (строго заданное качество, например
// "эта вещь всегда Awful") либо оставить стандартную игровую генерацию качества по навыку
// крафтера, но ограничить её диапазоном minQuality/maxQuality (например "не хуже Good,
// но и не выше Excellent"). Если ничего не задавать - работает как в ванильной игре.
//
// Какой именно навык проверять - задаётся один раз на весь рецепт в XML через
// <relevantSkill> (например Crafting, Medicine, Intellectual, Construction и т.д.).
//
// ПОЛНОЕ ОПИСАНИЕ ВСЕХ ПОЛЕЙ И ПРИМЕР НАСТРОЙКИ - см. комментарии в XML файле рецепта
// (например MakeWastepack.xml), там расписано подробно с примерами по каждому тегу.


namespace Watcher.Comps
{
    /// <summary>
    /// Настройки уровня всего рецепта. Вешается на RecipeDef через modExtensions.
    /// </summary>
    public class RandomProductExtension : DefModExtension
    {
        // Список всех возможных результатов крафта с их настройками (см. RandomProductOption ниже).
        public List<RandomProductOption> randomProducts = new List<RandomProductOption>();

        // --- Настройки "профвредности" (хеддиф крафтеру за саму работу, не связано с весами) ---

        // Нужно ли вообще накладывать хеддиф на крафтера за итерацию работы.
        public bool applyHediff = true;

        // defName хеддифа, который накладывается на крафтера (например ToxicBuildup).
        public HediffDef crafterHediff;

        // На сколько увеличивается тяжесть хеддифа за одну итерацию крафта.
        public float crafterHediffSeverity = 0.5f;

        // На какую часть тела накладывать хеддиф. Если не указано - хеддиф общий (whole body).
        public BodyPartDef crafterBodyPart;

        // true - хеддиф накладывается только на людей. false - на любого крафтера (включая механоидов/животных).
        public bool onlyHumanCrafters = true;

        // --- Настройки системы весов от навыка ---

        // Какой навык проверяем у крафтера, чтобы посчитать веса вариантов (задаётся в XML,
        // например: Crafting, Medicine, Intellectual, Construction, Cooking, Plants, Mining и т.д.).
        // Если оставить пустым - навык не проверяется, используется fallbackSkillLevel для всех.
        public SkillDef relevantSkill;

        // Уровень навыка "по умолчанию" (0-20), который используется, если у крафтера
        // нет нужного навыка (например немеханический крафтер без skills).
        public int fallbackSkillLevel = 0;
    }

    /// <summary>
    /// Один возможный результат крафта - предмет, активная граната или живое существо,
    /// со своим весом, зависящим от навыка крафтера.
    /// </summary>
    public class RandomProductOption
    {
        // defName предмета/существа, которое может быть создано (например Rubber, Gun_Revolver, Cockroach).
        public ThingDef thingDef;

        // Сколько единиц/голов создаётся за один "выпад" этого варианта.
        public int count = 1;

        // --- Система весов (шкала навыка крафтера 0-20, как в самой игре) ---

        // Вес этого варианта, когда у крафтера навык = 0. Чем больше число - тем чаще
        // вариант выпадает относительно других вариантов НА ЭТОМ уровне навыка.
        public float weightAtSkill0 = 1f;

        // Вес этого варианта, когда у крафтера навык = 20 (максимум в игре).
        // Между 0 и 20 вес меняется линейно. Если весAtSkill0 == weightAtSkill20 -
        // вес не будет зависеть от навыка вообще (одинаковый шанс на любом уровне).
        public float weightAtSkill20 = 1f;

        // Минимальный уровень навыка, при котором этот результат вообще может выпасть.
        // Если у крафтера навык ниже - вариант полностью исключается из розыгрыша
        // (в отличие от weightAtSkill0/20, которые только уменьшают шанс, но не убирают его).
        // Полезно, чтобы редкие/ценные/опасные вещи не могли выпасть у новичка совсем.
        public int minSkillLevel = 0;

        // --- Качество результата (только для предметов, у которых вообще есть качество -
        //     оружие, броня, мебель и т.п.; на предметы без CompQuality эти поля не влияют) ---

        // Если задано - у предмета будет СТРОГО это качество, игровая генерация по навыку
        // не используется вообще. Допустимые значения: Awful, Poor, Normal, Good, Excellent,
        // Masterwork, Legendary. Оставьте пустым (не указывайте тег в XML), чтобы качество
        // генерировалось обычным игровым способом (см. minQuality/maxQuality ниже).
        public QualityCategory? fixedQuality = null;

        // Работают, только если fixedQuality НЕ задано. Качество сначала генерируется как
        // обычно - через стандартную игровую формулу (зависит от workSkill рецепта, а не
        // обязательно от relevantSkill, и от вдохновения пешки), а затем результат
        // "зажимается" в диапазон [minQuality; maxQuality]. Например, если игра насчитала
        // Legendary, а maxQuality = Good - предмет всё равно будет не выше Good.
        // По умолчанию диапазон от Awful до Legendary, то есть никак не ограничивает игру.
        public QualityCategory minQuality = QualityCategory.Awful;
        public QualityCategory maxQuality = QualityCategory.Legendary;

        // --- Куда и как спавнить результат ---

        // true - результат создаётся как живое существо/пешка (использует PawnKindDef
        // с тем же defName, что и thingDef). false - обычный предмет на карте.
        public bool spawnAsPawn = false;

        // Работает только если spawnAsPawn = true и результат - животное.
        // true = существо сразу считается прирученным (если оно не враждебной фракции).
        public bool tameIfAnimal = false;

        // true = результат создаётся как предмет и сразу активируется (взводится взрыватель),
        // как будто его кто-то поджёг. Также автоматически включается для снарядов (def.projectile != null).
        public bool spawnAsActiveGrenade = false;

        // Через сколько игровых тиков взорвётся активная граната (60 тиков = 1 секунда).
        // Используется только вместе со spawnAsActiveGrenade = true.
        public int fuseTicks = 60;

        // Работает только при spawnAsPawn = true. Какой фракции будет принадлежать пешка:
        // "Player" - в колонию игрока; "Enemy"/"Hostile" - случайная враждебная фракция;
        // "Neutral" - случайная нейтральная фракция; "Wild"/"null" - без фракции.
        public string factionType = "Player";
    }

    public class RecipeWorker_RandomProduct : RecipeWorker
    {
        /// <summary>
        /// Вызывается движком игры по завершении КАЖДОЙ итерации крафта по этому рецепту.
        /// Порядок действий:
        ///   1) наложить/усилить хеддиф крафтеру (если настроено);
        ///   2) узнать уровень нужного навыка у крафтера (0-20);
        ///   3) отфильтровать варианты по minSkillLevel;
        ///   4) взвешенным рандомом выбрать один вариант (вес зависит от навыка);
        ///   5) заспавнить результат: предмет / активную гранату / живое существо.
        /// </summary>
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);

            var extension = this.recipe.GetModExtension<RandomProductExtension>();

            if (extension == null || extension.randomProducts.NullOrEmpty())
            {
                //Log.Error($"Recipe {this.recipe.defName} uses RecipeWorker_RandomProduct but has no <li Class=\"Watcher.Comps.RandomProductExtension\"> in modExtensions!");
                return;
            }

            // Шаг 1: профвредность крафтеру (не связана с системой весов).
            if (extension.applyHediff && extension.crafterHediff != null && IsHumanCrafter(billDoer, extension))
            {
                ApplyCrafterHediff(billDoer, extension);
            }

            // Шаг 2: узнаём уровень нужного навыка (0-20) у крафтера.
            int skillLevel = GetRelevantSkillLevel(billDoer, extension);

            // Шаг 3: отбираем только те варианты, для которых уровень навыка уже достаточен
            // (minSkillLevel <= skillLevel). Это "жёсткий" фильтр - недостаточный навык
            // полностью убирает вариант из розыгрыша, а не просто уменьшает его вес.
            var availableOptions = extension.randomProducts
                .Where(opt => skillLevel >= opt.minSkillLevel)
                .ToList();

            if (availableOptions.NullOrEmpty())
            {
                // На всякий случай, если из-за minSkillLevel не осталось ни одного варианта -
                // берём весь список без фильтра, чтобы не сломать рецепт (крафт не должен зависать без результата).
                availableOptions = extension.randomProducts;
            }

            // Шаг 4: взвешенный случайный выбор среди оставшихся вариантов.
            RandomProductOption selected = PickWeightedOption(availableOptions, skillLevel);

            if (selected == null)
                selected = availableOptions.First();

            // Шаг 5: спавн результата нужным способом.
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

        /// <summary>
        /// Возвращает уровень навыка, указанного в extension.relevantSkill (0-20), у данного крафтера.
        /// Используется тремя проверками "запасного варианта" (fallback):
        ///   - если relevantSkill вообще не задан в XML - возвращаем fallbackSkillLevel;
        ///   - если у пешки нет компонента skills (например это не гуманоид) - тоже fallbackSkillLevel;
        ///   - если конкретный навык у пешки полностью отключён (TotallyDisabled, например нет рук
        ///     для Crafting) - тоже fallbackSkillLevel.
        /// В остальных случаях - реальный уровень навыка пешки (record.Level, диапазон 0-20).
        /// </summary>
        private int GetRelevantSkillLevel(Pawn billDoer, RandomProductExtension extension)
        {
            if (extension.relevantSkill == null)
                return extension.fallbackSkillLevel;

            if (billDoer?.skills == null)
                return extension.fallbackSkillLevel;

            SkillRecord record = billDoer.skills.GetSkill(extension.relevantSkill);
            if (record == null || record.TotallyDisabled)
                return extension.fallbackSkillLevel;

            return record.Level;
        }

        /// <summary>
        /// Считает эффективный вес одного варианта на заданном уровне навыка (шкала 0-20, как в игре).
        /// Простая линейная интерполяция между weightAtSkill0 и weightAtSkill20:
        ///   t = skillLevel / 20  (0.0 при навыке 0, 1.0 при навыке 20)
        ///   вес = weightAtSkill0 + (weightAtSkill20 - weightAtSkill0) * t
        /// Итоговый вес никогда не опускается ниже 0.01, чтобы вариант не "исчезал" совсем
        /// из-за округления или случайно введённого нуля/отрицательного числа в XML.
        /// </summary>
        private float GetEffectiveWeight(RandomProductOption option, int skillLevel)
        {
            float t = Mathf.Clamp(skillLevel, 0, 20) / 20f;
            float w = Mathf.Lerp(option.weightAtSkill0, option.weightAtSkill20, t);

            return Mathf.Max(0.01f, w);
        }

        /// <summary>
        /// Взвешенный случайный выбор варианта. Для каждого варианта считаем его эффективный вес
        /// на текущем уровне навыка (GetEffectiveWeight), суммируем все веса, бросаем "кубик"
        /// от 0 до суммы весов и идём по списку, пока не наберём выпавшее число - это и есть выбор.
        /// Вариант с бОльшим весом занимает бОльший "отрезок" на этой шкале и потому выпадает чаще.
        /// </summary>
        private RandomProductOption PickWeightedOption(List<RandomProductOption> options, int skillLevel)
        {
            float totalWeight = 0f;
            var effectiveWeights = new List<float>(options.Count);

            foreach (var option in options)
            {
                float w = GetEffectiveWeight(option, skillLevel);
                effectiveWeights.Add(w);
                totalWeight += w;
            }

            float roll = Rand.Range(0f, totalWeight);
            float currentWeight = 0f;

            for (int i = 0; i < options.Count; i++)
            {
                currentWeight += effectiveWeights[i];
                if (roll <= currentWeight)
                    return options[i];
            }

            return options.LastOrDefault();
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

        /// <summary>
        /// Создаёт обычный предмет на карте рядом с крафтером и настраивает его качество (если применимо).
        /// Приоритет качества: fixedQuality (если задано) -> иначе стандартная игровая генерация,
        /// зажатая в диапазон [minQuality; maxQuality].
        /// </summary>
        private void SpawnThing(RandomProductOption option, Pawn billDoer)
        {
            Thing product = ThingMaker.MakeThing(option.thingDef);
            product.stackCount = option.count;

            CompQuality compQuality = product.TryGetComp<CompQuality>();
            if (compQuality != null)
            {
                QualityCategory quality = GetResultQuality(option, billDoer);
                compQuality.SetQuality(quality, ArtGenerationContext.Colony);
            }

            GenPlace.TryPlaceThing(product, billDoer.Position, billDoer.Map, ThingPlaceMode.Near);
            Messages.Message($"Produced: {product.def.label} x{option.count}", product, MessageTypeDefOf.PositiveEvent);
        }

        /// <summary>
        /// Определяет итоговое качество предмета для данного варианта.
        ///   - Если у option задано fixedQuality - возвращаем его как есть.
        ///   - Иначе генерируем качество стандартным игровым способом (как и раньше,
        ///     через QualityUtility.GenerateQualityCreatedByPawn по workSkill рецепта
        ///     и вдохновению пешки), а затем зажимаем результат в [minQuality; maxQuality].
        /// </summary>
        private QualityCategory GetResultQuality(RandomProductOption option, Pawn billDoer)
        {
            if (option.fixedQuality.HasValue)
                return option.fixedQuality.Value;

            QualityCategory quality = QualityUtility.GenerateQualityCreatedByPawn(billDoer, this.recipe.workSkill);

            if (quality < option.minQuality)
                quality = option.minQuality;
            if (quality > option.maxQuality)
                quality = option.maxQuality;

            return quality;
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