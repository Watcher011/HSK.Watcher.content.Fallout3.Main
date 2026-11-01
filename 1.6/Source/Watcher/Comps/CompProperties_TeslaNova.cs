using RimWorld;
using SK;
using System.Collections.Generic;
using UnityEngine;
using Verse;

// Сбой генератора Tesla Nova:
//  - время от времени работающий генератор ломается (CompBreakdownable.DoBreakdown);
//  - на карте появляется постоянное ЭМИ-поле (как солнечная вспышка: вся электрика отключена);
//  - поле НЕ исчезает само по себе — только когда генератор отремонтирован
//    (или уничтожен / деконструирован / свернут в minified);
//  - пока поле активно, все механоиды на карте постоянно оглушены.
// Код совместим с C# 7.3.

namespace Watcher.Comps
{
    // ---------- Свойства компонента (задаются в XML у генератора) ----------
    public class CompProperties_TeslaMalfunction : CompProperties
    {
        // GameConditionDef ЭМИ-поля (в XML: TeslaEMPField)
        public GameConditionDef conditionDef;

        // Среднее время между сбоями (в игровых днях) у РАБОТАЮЩЕГО генератора
        public float mtbDays = 12f;

        // Минимальная пауза после сбоя (в днях), чтобы не ломался два раза подряд
        public float cooldownDays = 2f;

        // Длительность оглушения механоидов; обновляется каждые 60 тиков
        public int mechStunTicks = 90;

        // Тексты (английские, как и остальные XML-тексты мода)
        public string letterLabel;
        public string letterText;
        public string fieldInspectString;

        public CompProperties_TeslaMalfunction()
        {
            compClass = typeof(CompTeslaMalfunction);
        }
    }

    // ---------- Компонент генератора ----------
    public class CompTeslaMalfunction : ThingComp
    {
        private int lastMalfunctionTick = -999999;

        public CompProperties_TeslaMalfunction Props
        {
            get { return (CompProperties_TeslaMalfunction)props; }
        }

        public override void CompTick()
        {
            base.CompTick();

            if (!parent.IsHashIntervalTick(250)) return;

            Map map = parent.Map;
            if (map == null || Props.conditionDef == null) return;

            // Сломанный генератор не ломается повторно
            if (parent.IsBrokenDown()) return;

            // Поле уже активно
            if (map.gameConditionManager.ConditionIsActive(Props.conditionDef)) return;

            // Пауза после предыдущего сбоя
            int cooldownTicks = (int)(Props.cooldownDays * 60000f);
            if (Find.TickManager.TicksGame < lastMalfunctionTick + cooldownTicks) return;

            // Сбой возможен только когда генератор реально работает
            CompFueled fueled = parent.GetComp<CompFueled>();
            if (fueled == null || !fueled.ReadyForWork) return;

            CompFlickable flickable = parent.GetComp<CompFlickable>();
            if (flickable != null && !flickable.SwitchIsOn) return;

            if (Rand.MTBEventOccurs(Props.mtbDays, 60000f, 250f))
            {
                TriggerMalfunction(map);
            }
        }

        // Публичный — можно вызвать из дев-кнопки, инцидента и т.д.
        public void TriggerMalfunction(Map map)
        {
            if (map == null || Props.conditionDef == null) return;
            if (map.gameConditionManager.ConditionIsActive(Props.conditionDef)) return;

            CompBreakdownable breakdownable = parent.GetComp<CompBreakdownable>();
            if (breakdownable == null)
            {
                Log.Warning("[Watcher] CompTeslaMalfunction: у " + parent.def.defName +
                            " нет CompBreakdownable — сбой невозможен.");
                return;
            }

            lastMalfunctionTick = Find.TickManager.TicksGame;

            // 1. Генератор ломается
            if (!breakdownable.BrokenDown)
            {
                breakdownable.DoBreakdown();
            }

            // 2. Постоянное ЭМИ-поле на всю карту
            GameCondition condition = GameConditionMaker.MakeCondition(Props.conditionDef);
            condition.Permanent = true;

            GameCondition_TeslaEMP emp = condition as GameCondition_TeslaEMP;
            if (emp != null)
            {
                emp.source = parent;
                emp.mechStunTicks = Props.mechStunTicks;
            }
            map.gameConditionManager.RegisterCondition(condition);

            // 3. Письмо
            string label = string.IsNullOrEmpty(Props.letterLabel)
                ? "Tesla generator malfunction"
                : Props.letterLabel;
            string text = string.IsNullOrEmpty(Props.letterText)
                ? "The generator has failed and released a map-wide EMP field. All electrical devices are down and mechanoids are stunned until it is repaired."
                : Props.letterText;
            Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.ThreatBig, new TargetInfo(parent));
        }

        public override string CompInspectStringExtra()
        {
            if (!string.IsNullOrEmpty(Props.fieldInspectString) &&
                Props.conditionDef != null &&
                parent.Map != null &&
                parent.Map.gameConditionManager.ConditionIsActive(Props.conditionDef))
            {
                return Props.fieldInspectString;
            }
            return null;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }

            if (Prefs.DevMode && parent.Spawned)
            {
                Command_Action cmd = new Command_Action();
                cmd.defaultLabel = "DEV: Tesla malfunction";
                cmd.defaultDesc = "Break the generator and start the map-wide EMP field.";
                cmd.action = delegate { TriggerMalfunction(parent.Map); };
                yield return cmd;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastMalfunctionTick, "lastMalfunctionTick", -999999);
        }
    }

    // ---------- ЭМИ-поле (как солнечная вспышка + стан механоидов) ----------
    // Наследуемся от ванильного GameCondition_DisableElectricity (его использует SolarFlare),
    // поэтому отключение электричества работает точно так же, как у вспышки.
    public class GameCondition_TeslaEMP : GameCondition_DisableElectricity
    {
        public Thing source;          // генератор — виновник поля
        public int mechStunTicks = 90;

        private const int StunRefreshInterval = 60;
        private const int SparkInterval = 15;

        public override void GameConditionTick()
        {
            base.GameConditionTick();

            Map map = SingleMap;
            if (map == null) return;

            // Поле держится, пока генератор стоит на карте и остаётся сломанным
            if (source == null || source.Destroyed || !source.Spawned || !source.IsBrokenDown())
            {
                End();
                return;
            }

            int ticks = Find.TickManager.TicksGame;

            if (ticks % StunRefreshInterval == 0)
            {
                StunMechanoids(map);
            }

            if (ticks % SparkInterval == 0)
            {
                FleckMaker.ThrowMicroSparks(source.DrawPos, map);
            }
        }

        private void StunMechanoids(Map map)
        {
            List<Pawn> pawns = (List<Pawn>)map.mapPawns.AllPawnsSpawned;
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                Pawn pawn = pawns[i];
                if (pawn == null || pawn.Dead) continue;
                if (!pawn.RaceProps.IsMechanoid) continue;
                if (pawn.stances == null || pawn.stances.stunner == null) continue;

                pawn.stances.stunner.StunFor(mechStunTicks, null, false, false);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref source, "teslaSource");
            Scribe_Values.Look(ref mechStunTicks, "mechStunTicks", 90);
        }
    }
}