using RimWorld;
using SK;
using UnityEngine;
using Verse;

namespace Watcher.Buildings
{
    [StaticConstructorOnStartup]
    public class Building_FalloutTeslaGenerator : Building
    {
        private CompPowerTrader powerComp;

        private const string FramePath = "Things/Building/Power/Generator/FalloutTeslaGenerator/FalloutTeslaGenerator_anim";
        private const string SwitchOnPath = "Things/Building/Power/Generator/FalloutTeslaGenerator/FalloutTeslaGenerator_FuelOn";
        private const string SwitchOffPath = "Things/Building/Power/Generator/FalloutTeslaGenerator/FalloutTeslaGenerator";
        private const int FrameCount = 8;
        private const int multispeed = 10;

        private int timer;

        private static Graphic[] TexResFrames;
        private static Graphic BuildingTexOn;
        private static Graphic BuildingTexOff;

        private Graphic TexMain;
        private Graphic currentTex;

        protected CompFueled fueledComp;
        protected CompBreakdownable breakdownableComp;
        protected CompFlickable flickableComp;

        // Статический конструктор для загрузки ресурсов в главном потоке
        static Building_FalloutTeslaGenerator()
        {
            // Инициализация графических ресурсов
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                // Загружаем анимационные кадры
                TexResFrames = new Graphic[FrameCount];
                for (int i = 0; i < FrameCount; i++)
                {
                    TexResFrames[i] = GraphicDatabase.Get<Graphic_Single>(
                        FramePath + (i + 1),
                        ShaderDatabase.Cutout,
                        Vector2.one,
                        Color.white
                    );
                }

                // Загружаем статические текстуры
                BuildingTexOn = GraphicDatabase.Get<Graphic_Single>(
                    SwitchOnPath,
                    ShaderDatabase.Cutout,
                    Vector2.one,
                    Color.white
                );

                BuildingTexOff = GraphicDatabase.Get<Graphic_Single>(
                    SwitchOffPath,
                    ShaderDatabase.Cutout,
                    Vector2.one,
                    Color.white
                );
            });
        }

        private bool Powered
        {
            get
            {
                if (fueledComp != null && fueledComp.ReadyForWork &&
                    flickableComp != null && flickableComp.SwitchIsOn &&
                    breakdownableComp != null)
                {
                    return !breakdownableComp.BrokenDown;
                }
                return false;
            }
        }

        public override Graphic Graphic
        {
            get
            {
                if (fueledComp != null && fueledComp.Fuel > 0f)
                {
                    Graphic graphic = GraphicDatabase.Get<Graphic_Single>(
                        def.graphic.path + "_FuelOn",
                        def.graphic.Shader,
                        def.graphic.drawSize,
                        def.graphic.Color
                    );
                    if (graphic != null)
                    {
                        // Если SK_Utility.GraphicColoredFor существует в вашем коде
                        // return SK_Utility.GraphicColoredFor(this, graphic);
                        return graphic;
                    }
                }
                return base.Graphic;
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            powerComp = GetComp<CompPowerTrader>();
            fueledComp = GetComp<CompFueled>();
            breakdownableComp = GetComp<CompBreakdownable>();
            flickableComp = GetComp<CompFlickable>();

            if (!this.IsBrokenDown())
            {
                powerComp.PowerOn = true;
            }

            // Инициализируем текущую текстуру
            currentTex = BuildingTexOff ?? base.Graphic;
        }

        protected override void Tick()
        {
            base.Tick();

            // Редкий тик (каждые 250 тактов)
            if (Find.TickManager.TicksGame % 250 == 0)
                TickRare();

            if (!Powered || TexResFrames == null || TexResFrames.Length == 0)
                return;

            // Обновляем текущую текстуру в зависимости от состояния
            UpdateCurrentTexture();

            // Учитываем масштаб времени для плавной анимации
            if (Find.TickManager.TicksGame % Mathf.Max(1, Mathf.RoundToInt(TimeScale.timescalefloat)) == 0f)
                timer++;

            if (timer >= TexResFrames.Length * multispeed)
                timer = 0;

            HandleAnimation();
        }

        private void UpdateCurrentTexture()
        {
            if (flickableComp != null)
            {
                if (flickableComp.SwitchIsOn && fueledComp != null && fueledComp.Fuel > 0f)
                {
                    currentTex = BuildingTexOn;
                }
                else
                {
                    currentTex = BuildingTexOff;
                }
            }
        }

        private void HandleAnimation()
        {
            if (timer < TexResFrames.Length * multispeed)
            {
                int frameIndex = timer / multispeed;
                if (frameIndex >= 0 && frameIndex < TexResFrames.Length)
                {
                    TexMain = TexResFrames[frameIndex];
                    if (TexMain != null)
                    {
                        TexMain.color = base.Graphic.color;
                    }
                }
            }
        }

        public override void TickRare()
        {
            base.TickRare();
            // Редкие обновления можно добавить здесь
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            // Сначала рисуем основное здание
            base.DrawAt(drawLoc, flip);

            // Затем рисуем анимацию поверх, если генератор работает
            if (TexMain != null && Powered)
            {
                Matrix4x4 matrix = Matrix4x4.TRS(
                    DrawPos + Altitudes.AltIncVect,
                    base.Rotation.AsQuat,
                    new Vector3(2f, 1f, 2f) // Масштаб
                );

                Graphics.DrawMesh(
                    MeshPool.plane10,
                    matrix,
                    TexMain.MatAt(base.Rotation),
                    0
                );
            }
        }

        // Правильный метод для получения сигналов от компонентов
        protected override void ReceiveCompSignal(string signal)
        {
            base.ReceiveCompSignal(signal);

            switch (signal)
            {
                case "FlickedOn":
                case "FlickedOff":
                case "Breakdown":
                case "Repaired":
                case "RanOutOfFuel":
                case "Refueled":
                    // Обновляем состояние при любых изменениях
                    UpdateCurrentTexture();
                    break;
            }
        }

        // Дополнительные методы для особых случаев

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            base.Destroy(mode);
            // Очистка ресурсов при уничтожении
        }

        public override void ExposeData()
        {
            base.ExposeData();
            // Сохранение состояния при загрузке/сохранении
        }
    }
}