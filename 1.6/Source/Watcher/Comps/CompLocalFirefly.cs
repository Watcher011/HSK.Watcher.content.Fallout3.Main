
using RimWorld;
using System.Reflection;
using UnityEngine;
using Verse;

namespace Watcher.Comps
{
    public class CompProperties_Firefly : CompProperties_FireOverlay
    {
        public new float fireSize = 0.1f;
        public new float finalFireSize = 0.3f;
        public float speed = 1.0f;

        public CompProperties_Firefly()
        {
            compClass = typeof(CompFirefly);
        }
    }

    public class CompFirefly : CompFireOverlayBase
    {
      
        private float randomPhase;

        public new CompProperties_Firefly Props => (CompProperties_Firefly)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            randomPhase = Rand.Range(0f, Mathf.PI * 2f);
        }

        public override void PostDraw()
        {
            // Всегда рисуем базовый эффект
            base.PostDraw();

            if (parent == null || parent.Destroyed || !parent.Spawned)
                return;

            // Обновляем анимацию на основе тиков
            int tick = Find.TickManager.TicksGame;
            float time = tick * 0.05f * Props.speed + randomPhase;

            // Синусоида от 0 до 1
            float wave = (Mathf.Sin(time) + 1f) * 0.5f;

            // Вычисляем текущий размер
            float currentSize = Mathf.Lerp(Props.fireSize, Props.finalFireSize, wave);

            // Позиция
            Vector3 drawPos = parent.DrawPos;
            drawPos.y += Altitudes.AltInc;
            drawPos += Props.offset;

            // Создаем графику с нужным размером КАЖДЫЙ РАЗ
            Graphic fireflyGraphic = GraphicDatabase.Get<Graphic_Flicker>(
                "Things/Special/Firefly",
                ShaderDatabase.TransparentPostLight,
                new Vector2(currentSize, currentSize), // ВАЖНО: передаем размер здесь
                Color.white);

            // Рисуем
            fireflyGraphic.Draw(drawPos, Rot4.North, parent);
        }
    }
}