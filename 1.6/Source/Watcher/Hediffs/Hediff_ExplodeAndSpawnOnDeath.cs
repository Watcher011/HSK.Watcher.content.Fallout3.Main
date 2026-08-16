using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
/*namespace Watcher.Hediffs
{
    public class Hediff_ExplosiveDeath : HediffWithComps
    {
        public override string TipStringExtra => "WatcherHediffExplosiveDeath".Translate();

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            CreateExplosionAndEffects();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (pawn.Faction == Faction.OfPlayer && pawn.IsColonistPlayerControlled)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WatcherGizmoDetonateLabel".Translate(),
                    defaultDesc = "WatcherGizmoDetonateDescription".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Detonate", true),
                    action = delegate
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "WatcherSelfDetonationConfirmationDialogText".Translate(pawn.Name.ToStringFull),
                            () => 
                            {
                                CreateExplosionAndEffects();
                                pawn.Kill(null, this);
                            },
                            destructive: true
                        ));
                    }
                };
            }
        }

        private void CreateExplosionAndEffects()
        {
            if (pawn == null || !pawn.Spawned)
                return;

            Map map = pawn.Map;
            IntVec3 position = pawn.Position;

            // Получаем свойства из компов
            var explodeComp = comps?.OfType<HediffComp_ExplodeAndSpawnOnDeath>().FirstOrDefault();
            if (explodeComp != null)
            {
                // Создаем взрыв ДО смерти
                if (explodeComp.Props.explosionRadius > 0)
                {
                    GenExplosion.DoExplosion(
                        center: position,
                        map: map,
                        radius: explodeComp.Props.explosionRadius,
                        damType: explodeComp.Props.explosionDamageDef ?? DamageDefOf.Bomb,
                        instigator: pawn,
                        damAmount: explodeComp.Props.damageAmount,
                        armorPenetration: explodeComp.Props.armorPenetration,
                        explosionSound: explodeComp.Props.explosionSound,
                        chanceToStartFire: explodeComp.Props.chanceToStartFire
                    );
                }

                // Спавним объекты
                if (explodeComp.Props.thingToSpawn != null && explodeComp.Props.spawnCount > 0)
                {
                    for (int i = 0; i < explodeComp.Props.spawnCount; i++)
                    {
                        Thing thing = ThingMaker.MakeThing(explodeComp.Props.thingToSpawn);
                        thing.stackCount = 1;
                        
                        GenPlace.TryPlaceThing(
                            thing: thing,
                            center: position,
                            map: map,
                            mode: ThingPlaceMode.Near
                        );
                    }
                }

                // Создаем визуальные эффекты
                if (explodeComp.Props.mote != null || explodeComp.Props.fleck != null)
                {
                    Vector3 drawPos = pawn.DrawPos;
                    for (int i = 0; i < explodeComp.Props.moteCount; i++)
                    {
                        Vector2 vector = Rand.InsideUnitCircle * explodeComp.Props.moteOffsetRange.RandomInRange * Rand.Sign;
                        Vector3 loc = new Vector3(drawPos.x + vector.x, drawPos.y, drawPos.z + vector.y);
                        
                        if (explodeComp.Props.mote != null)
                        {
                            MoteMaker.MakeStaticMote(loc, map, explodeComp.Props.mote);
                        }
                        else
                        {
                            FleckMaker.Static(loc, map, explodeComp.Props.fleck);
                        }
                    }
                }

                // Создаем грязь
                if (explodeComp.Props.filth != null)
                {
                    FilthMaker.TryMakeFilth(position, map, explodeComp.Props.filth, explodeComp.Props.filthCount);
                }

                // Проигрываем звук
                if (explodeComp.Props.sound != null)
                {
                    explodeComp.Props.sound.PlayOneShot(SoundInfo.InMap(pawn));
                }
            }
        }
    }

    public class HediffCompProperties_ExplodeAndSpawnOnDeath : HediffCompProperties
    {
        public ThingDef thingToSpawn;
        public int spawnCount = 1;
        public FloatRange spawnRadius = new FloatRange(1f, 3f);
        
        // Взрыв
        public float explosionRadius = 3f;
        public DamageDef explosionDamageDef;
        public int damageAmount = 30;
        public float armorPenetration = -1f;
        public float chanceToStartFire = 0f;
        public SoundDef explosionSound = null;
        public ThingDef explosionEffect = null;
        
        // Эффекты
        public FleckDef fleck;
        public ThingDef mote;
        public int moteCount = 3;
        public FloatRange moteOffsetRange = new FloatRange(0.2f, 0.4f);
        public ThingDef filth;
        public int filthCount = 4;
        public HediffDef injuryCreatedOnDeath;
        public IntRange injuryCount;
        public SoundDef sound;
        
        // Дополнительные опции
        public bool destroyEquipment = true;
        public bool destroyApparel = true;
        public bool affectAllies = false;

        public HediffCompProperties_ExplodeAndSpawnOnDeath()
        {
            compClass = typeof(HediffComp_ExplodeAndSpawnOnDeath);
        }
    }

    public class HediffComp_ExplodeAndSpawnOnDeath : HediffComp
    {
        public HediffCompProperties_ExplodeAndSpawnOnDeath Props => 
            (HediffCompProperties_ExplodeAndSpawnOnDeath)props;

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            
            // Создание травм
            if (Props.injuryCreatedOnDeath != null)
            {
                CreateInjuries();
            }
        }

        public override void Notify_PawnKilled()
        {
            base.Notify_PawnKilled();
            
            if (!base.Pawn.Spawned)
                return;

            // Уничтожение снаряжения
            if (Props.destroyEquipment)
                base.Pawn.equipment.DestroyAllEquipment();
            if (Props.destroyApparel)
                base.Pawn.apparel.DestroyAll();
        }

        private void CreateInjuries()
        {
            List<BodyPartRecord> list = new List<BodyPartRecord>(
                from part in base.Pawn.health.hediffSet.GetNotMissingParts()
                where part.coverageAbs > 0f
                select part);
            
            int num = Mathf.Min(Props.injuryCount.RandomInRange, list.Count);
            for (int i = 0; i < num; i++)
            {
                int index = Rand.Range(0, list.Count);
                BodyPartRecord part2 = list[index];
                list.RemoveAt(index);
                base.Pawn.health.AddHediff(Props.injuryCreatedOnDeath, part2);
            }
        }
    }

    public class CompProperties_HediffWhenEquipped : CompProperties
    {
        public HediffDef hediff;
        public BodyPartDef bodyPart;
        public float severity = 1.0f;

        public CompProperties_HediffWhenEquipped()
        {
            compClass = typeof(CompHediffWhenEquipped);
        }
    }

    public class CompHediffWhenEquipped : ThingComp
    {
        public CompProperties_HediffWhenEquipped Props => (CompProperties_HediffWhenEquipped)props;

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            
            // Добавляем хедифф при экипировке
            if (Props.hediff != null && pawn.health != null)
            {
                // Ищем подходящую часть тела
                BodyPartRecord part = null;
                if (Props.bodyPart != null)
                {
                    part = pawn.RaceProps.body.GetPartsWithDef(Props.bodyPart).FirstOrFallback();
                }
                
                // Проверяем, нет ли уже такого хедиффа
                Hediff existingHediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                if (existingHediff == null)
                {
                    Hediff hediff = HediffMaker.MakeHediff(Props.hediff, pawn, part);
                    hediff.Severity = Props.severity;
                    pawn.health.AddHediff(hediff);
                }
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            
            // Удаляем хедифф при снятии
            if (Props.hediff != null && pawn.health != null)
            {
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                if (hediff != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }
    }
}*/


//Некоторые существа или предметы могут быть заминированы или иметь взрывчатые импланты.
//    При смерти или снятии происходит взрыв, который может нанести урон окружающим, создать грязь и спавнить новых существ.


namespace Watcher.Hediffs
{
    public class Hediff_ExplosiveDeath : HediffWithComps
    {
        public override string TipStringExtra => "WatcherHediffExplosiveDeath".Translate();

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            CreateExplosionAndEffects();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            if (pawn.Faction == Faction.OfPlayer && pawn.IsColonistPlayerControlled)
            {
                yield return new Command_Action
                {
                    defaultLabel = "WatcherGizmoDetonateLabel".Translate(),
                    defaultDesc = "WatcherGizmoDetonateDescription".Translate(),
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Detonate", true),
                    action = delegate
                    {
                        Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                            "WatcherSelfDetonationConfirmationDialogText".Translate(pawn.Name.ToStringFull),
                            () =>
                            {
                                CreateExplosionAndEffects();
                                pawn.Kill(null, this);
                            },
                            destructive: true
                        ));
                    }
                };
            }
        }

        private void CreateExplosionAndEffects()
        {
            if (pawn == null || !pawn.Spawned)
                return;

            Map map = pawn.Map;
            IntVec3 position = pawn.Position;

            // Получаем свойства из компов
            var explodeComp = comps?.OfType<HediffComp_ExplodeAndSpawnOnDeath>().FirstOrDefault();
            if (explodeComp != null)
            {
                // Создаем взрыв
                if (explodeComp.Props.explosionRadius > 0)
                {
                    GenExplosion.DoExplosion(
                        center: position,
                        map: map,
                        radius: explodeComp.Props.explosionRadius,
                        damType: explodeComp.Props.explosionDamageDef,
                        instigator: pawn,
                        damAmount: explodeComp.Props.damageAmount,
                        armorPenetration: explodeComp.Props.armorPenetration,
                        explosionSound: explodeComp.Props.explosionSound,
                        chanceToStartFire: explodeComp.Props.chanceToStartFire
                    );
                }

                // Спавним объекты
                if (explodeComp.Props.thingToSpawn != null && explodeComp.Props.spawnCount > 0)
                {
                    for (int i = 0; i < explodeComp.Props.spawnCount; i++)
                    {
                        Thing thing = ThingMaker.MakeThing(explodeComp.Props.thingToSpawn);
                        thing.stackCount = 1;

                        GenPlace.TryPlaceThing(
                            thing: thing,
                            center: position,
                            map: map,
                            mode: ThingPlaceMode.Near
                        );
                    }
                }

                // Создаем визуальные эффекты
                if (explodeComp.Props.mote != null || explodeComp.Props.fleck != null)
                {
                    Vector3 drawPos = pawn.DrawPos;
                    for (int i = 0; i < explodeComp.Props.moteCount; i++)
                    {
                        Vector2 vector = Rand.InsideUnitCircle * explodeComp.Props.moteOffsetRange.RandomInRange * Rand.Sign;
                        Vector3 loc = new Vector3(drawPos.x + vector.x, drawPos.y, drawPos.z + vector.y);

                        if (explodeComp.Props.mote != null)
                        {
                            MoteMaker.MakeStaticMote(loc, map, explodeComp.Props.mote);
                        }
                        else
                        {
                            FleckMaker.Static(loc, map, explodeComp.Props.fleck);
                        }
                    }
                }

                // Создаем грязь
                if (explodeComp.Props.filth != null)
                {
                    FilthMaker.TryMakeFilth(position, map, explodeComp.Props.filth, explodeComp.Props.filthCount);
                }

                // Проигрываем звук
                if (explodeComp.Props.sound != null)
                {
                    explodeComp.Props.sound.PlayOneShot(SoundInfo.InMap(pawn));
                }
            }
        }
    }

    public class HediffCompProperties_ExplodeAndSpawnOnDeath : HediffCompProperties
    {
        public ThingDef thingToSpawn;
        public int spawnCount = 1;
        public FloatRange spawnRadius = new FloatRange(1f, 3f);

        // Взрыв
        public float explosionRadius = 3f;
        public DamageDef explosionDamageDef;
        public int damageAmount = 30;
        public float armorPenetration = -1f;
        public float chanceToStartFire = 0f;
        public SoundDef explosionSound = null;
        public ThingDef explosionEffect = null;

        // Эффекты
        public FleckDef fleck;
        public ThingDef mote;
        public int moteCount = 3;
        public FloatRange moteOffsetRange = new FloatRange(0.2f, 0.4f);
        public ThingDef filth;
        public int filthCount = 4;
        public HediffDef injuryCreatedOnDeath;
        public IntRange injuryCount;
        public SoundDef sound;

        // Дополнительные опции
        public bool destroyEquipment = true;
        public bool destroyApparel = true;
        public bool affectAllies = false;

        public HediffCompProperties_ExplodeAndSpawnOnDeath()
        {
            compClass = typeof(HediffComp_ExplodeAndSpawnOnDeath);
        }

        // ПРАВИЛЬНЫЙ МЕТОД ДЛЯ HediffCompProperties
        public override void ResolveReferences(HediffDef parent)
        {
            base.ResolveReferences(parent);

            // Инициализируем damageDef по умолчанию только если он не задан в XML
            if (explosionDamageDef == null)
            {
                explosionDamageDef = DamageDefOf.Bomb;
            }
        }
    }

    public class HediffComp_ExplodeAndSpawnOnDeath : HediffComp
    {
        public HediffCompProperties_ExplodeAndSpawnOnDeath Props =>
            (HediffCompProperties_ExplodeAndSpawnOnDeath)props;

        public override void Notify_PawnDied(DamageInfo? dinfo, Hediff culprit = null)
        {
            base.Notify_PawnDied(dinfo, culprit);
            CreateExplosionAndEffects();
        }

        public override void Notify_PawnKilled()
        {
            base.Notify_PawnKilled();
            CreateExplosionAndEffects();
        }

        private void CreateExplosionAndEffects()
        {
            if (base.Pawn == null || !base.Pawn.Spawned)
                return;

            Map map = base.Pawn.Map;
            IntVec3 position = base.Pawn.Position;

            // Создаем взрыв
            if (Props.explosionRadius > 0)
            {
                GenExplosion.DoExplosion(
                    center: position,
                    map: map,
                    radius: Props.explosionRadius,
                    damType: Props.explosionDamageDef,
                    instigator: base.Pawn,
                    damAmount: Props.damageAmount,
                    armorPenetration: Props.armorPenetration,
                    explosionSound: Props.explosionSound,
                    chanceToStartFire: Props.chanceToStartFire
                );
            }

            // Спавним объекты
            if (Props.thingToSpawn != null && Props.spawnCount > 0)
            {
                for (int i = 0; i < Props.spawnCount; i++)
                {
                    Thing thing = ThingMaker.MakeThing(Props.thingToSpawn);
                    thing.stackCount = 1;

                    GenPlace.TryPlaceThing(
                        thing: thing,
                        center: position,
                        map: map,
                        mode: ThingPlaceMode.Near
                    );
                }
            }

            // Создаем визуальные эффекты
            if (Props.mote != null || Props.fleck != null)
            {
                Vector3 drawPos = base.Pawn.DrawPos;
                for (int i = 0; i < Props.moteCount; i++)
                {
                    Vector2 vector = Rand.InsideUnitCircle * Props.moteOffsetRange.RandomInRange * Rand.Sign;
                    Vector3 loc = new Vector3(drawPos.x + vector.x, drawPos.y, drawPos.z + vector.y);

                    if (Props.mote != null)
                    {
                        MoteMaker.MakeStaticMote(loc, map, Props.mote);
                    }
                    else
                    {
                        FleckMaker.Static(loc, map, Props.fleck);
                    }
                }
            }

            // Создаем грязь
            if (Props.filth != null)
            {
                FilthMaker.TryMakeFilth(position, map, Props.filth, Props.filthCount);
            }

            // Проигрываем звук
            if (Props.sound != null)
            {
                Props.sound.PlayOneShot(SoundInfo.InMap(base.Pawn));
            }

            // Создание травм
            if (Props.injuryCreatedOnDeath != null)
            {
                CreateInjuries();
            }

            // Уничтожение снаряжения
            if (Props.destroyEquipment)
                base.Pawn.equipment?.DestroyAllEquipment();
            if (Props.destroyApparel)
                base.Pawn.apparel?.DestroyAll();
        }

        private void CreateInjuries()
        {
            List<BodyPartRecord> list = new List<BodyPartRecord>(
                from part in base.Pawn.health.hediffSet.GetNotMissingParts()
                where part.coverageAbs > 0f
                select part);

            int num = Mathf.Min(Props.injuryCount.RandomInRange, list.Count);
            for (int i = 0; i < num; i++)
            {
                int index = Rand.Range(0, list.Count);
                BodyPartRecord part2 = list[index];
                list.RemoveAt(index);
                base.Pawn.health.AddHediff(Props.injuryCreatedOnDeath, part2);
            }
        }
    }

    public class CompProperties_HediffWhenEquipped : CompProperties
    {
        public HediffDef hediff;
        public BodyPartDef bodyPart;
        public float severity = 1.0f;

        public CompProperties_HediffWhenEquipped()
        {
            compClass = typeof(CompHediffWhenEquipped);
        }
    }

    public class CompHediffWhenEquipped : ThingComp
    {
        public CompProperties_HediffWhenEquipped Props => (CompProperties_HediffWhenEquipped)props;

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);

            // Добавляем хедифф при экипировке
            if (Props.hediff != null && pawn.health != null)
            {
                // Ищем подходящую часть тела
                BodyPartRecord part = null;
                if (Props.bodyPart != null)
                {
                    part = pawn.RaceProps.body.GetPartsWithDef(Props.bodyPart).FirstOrFallback();
                }

                // Проверяем, нет ли уже такого хедиффа
                Hediff existingHediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                if (existingHediff == null)
                {
                    Hediff hediff = HediffMaker.MakeHediff(Props.hediff, pawn, part);
                    hediff.Severity = Props.severity;
                    pawn.health.AddHediff(hediff);
                }
            }
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);

            // Удаляем хедифф при снятии
            if (Props.hediff != null && pawn.health != null)
            {
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                if (hediff != null)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }
        }
    }
}