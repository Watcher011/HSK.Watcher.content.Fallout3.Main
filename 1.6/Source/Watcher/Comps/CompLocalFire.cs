using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
namespace Watcher.Comps
{
    public class CompProperties_LocalFire : CompProperties_FireOverlay
    {
        public float greenlightHeight = 0.046875f; // Настраиваемая высота в клетках

        public CompProperties_LocalFire()
        {
            compClass = typeof(CompLocalFire);
        }
    }

    [StaticConstructorOnStartup]
    public class CompLocalFire : CompFireOverlayBase
    {
        private static readonly Graphic GreenlightGraphic = GraphicDatabase.Get<Graphic_Flicker>(
            "Things/Special/Greenlight",
            ShaderDatabase.TransparentPostLight,
            Vector2.one,
            Color.white
        );

        private CompGlower cachedGlower;
        private bool glowerChecked = false;

        public new CompProperties_LocalFire Props => (CompProperties_LocalFire)props;

        public override void PostDraw()
        {
            base.PostDraw();
            DrawGreenlight();
        }

        private void DrawGreenlight()
        {
            // Проверяем наличие CompGlower
            if (!glowerChecked || cachedGlower == null)
            {
                cachedGlower = parent.GetComp<CompGlower>();
                glowerChecked = true;
            }

            // Если есть CompGlower и он не светится - не рисуем зеленый свет
            if (cachedGlower != null && !cachedGlower.Glows)
                return;

            Vector3 drawPos = parent.DrawPos;

            // Используем высоту из свойств или стандартное смещение
            float heightOffset = Props?.greenlightHeight ?? 0.046875f;
            drawPos.y += heightOffset;

            // Отрисовываем зеленый свет
            GreenlightGraphic.Draw(
                drawPos + Props.offset,
                parent.Rotation,
                parent,
                0f
            );
        }

        // Корректные методы для переопределения в CompFireOverlayBase
        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            glowerChecked = false;
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            glowerChecked = false;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            // Сохраняем состояние если нужно
        }

        public override void CompTick()
        {
            base.CompTick();

            // Периодически обновляем кэш (например, каждые 60 тиков)
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                glowerChecked = false;
            }
        }

        // Метод для обработки уничтожения объекта
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            // Очистка при уничтожении
        }
    }
}