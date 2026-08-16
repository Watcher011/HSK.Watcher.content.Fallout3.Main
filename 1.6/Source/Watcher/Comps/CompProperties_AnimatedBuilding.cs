using RimWorld;
using Verse;
using UnityEngine;

namespace Watcher.Comps
{
    public class CompProperties_Animated : CompProperties
    {
        public int tickInterval = 60;        // Тики между кадрами (60 = 1 сек)
        public string texPath;               // Базовый путь без _1, _2, _3
        public int frameCount = 3;           // Количество кадров
        public Vector3 drawOffset = Vector3.zero;
        public bool drawAbove = true;        // Рисовать выше базовой текстуры

        public CompProperties_Animated()
        {
            compClass = typeof(CompAnimated);
        }
    }

    public class CompAnimated : ThingComp
    {
        private int currentFrame = 0;
        private int tickCounter = 0;

        public CompProperties_Animated Props => (CompProperties_Animated)props;

        public override void CompTick()
        {
            base.CompTick();
            tickCounter++;
            if (tickCounter >= Props.tickInterval)
            {
                tickCounter = 0;
                currentFrame = (currentFrame + 1) % Props.frameCount;
            }
        }

        public override void PostDraw()
        {
            // Рисуем поверх стандартной графики
            base.PostDraw();

            if (parent == null || parent.Destroyed || !parent.Spawned)
                return;

            // Формируем путь к текущему кадру
            string framePath = $"{Props.texPath}_{currentFrame + 1}";

            // Позиция — совпадает с позицией родителя
            Vector3 drawPos = parent.DrawPos;

            // Поднимаем выше, чтобы перекрыть базовую текстуру
            if (Props.drawAbove)
            {
                drawPos.y += Altitudes.AltInc;
            }

            drawPos += Props.drawOffset;

            // Создаём графику КАЖДЫЙ РАЗ — как в примере с Firefly
            Graphic frameGraphic = GraphicDatabase.Get<Graphic_Single>(
                framePath,
                ShaderDatabase.Cutout,
                parent.def.graphicData.drawSize,  // Тот же размер, что у родителя
                Color.white);

            // Рисуем
            frameGraphic.Draw(drawPos, parent.Rotation, parent);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentFrame, "currentFrame", 0);
            Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
        }
    }
}