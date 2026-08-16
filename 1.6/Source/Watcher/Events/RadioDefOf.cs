using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System.Linq;

//Этот код добавляет в игру функциональное музыкальное радио для колонистов, которое влияет на их настроение в зависимости от черт характера.

namespace Watcher.Events
{
    [DefOf]
    public static class RadioDefOf
    {
        public static ThingDef MusicRadio;
        public static JobDef ListenToRadio;
        public static ThoughtDef MusicRadioListening;
        public static ThoughtDef MusicRadioAnnoying;    // НОВОЕ: для мизофонов
        public static JoyKindDef MusicRadioJoy;
        public static TraitDef MusicLover;              // НОВОЕ
        public static TraitDef MusicHater;              // НОВОЕ

        static RadioDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RadioDefOf));
        }
    }

    public class RadioMod : Mod
    {
        public static RadioMod instance;

        public RadioMod(ModContentPack content) : base(content)
        {
            instance = this;
            var harmony = new Harmony("watcher.events.rimworldradio");
            //Log.Message("[Watcher.Events.MusicRadio] Mod initialized successfully");
        }
    }

    public class JobDriver_ListenToRadio : JobDriver_WatchBuilding
    {
        protected override void WatchTickAction(int delta)
        {
            base.WatchTickAction(delta);

            if (TargetA.Thing is Building radio)
            {
                var powerComp = radio.TryGetComp<CompPowerTrader>();
                if (powerComp == null || !powerComp.PowerOn || radio.IsBrokenDown())
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
            }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);
        }
    }

    // === НОВЫЙ КОД: ThoughtWorker с поддержкой черт ===
    public class ThoughtWorker_MusicRadio : ThoughtWorker
    {
        private const float PASSIVE_RADIUS = 5f;
        private const float ACTIVE_RADIUS = 3f;

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!p.Spawned || p.Dead)
                return ThoughtState.Inactive;

            // Мизофоны не получают положительных мыслей от музыки
            if (p.story?.traits?.HasTrait(RadioDefOf.MusicHater) == true)
                return ThoughtState.Inactive;

            bool isListening = p.CurJob?.def == RadioDefOf.ListenToRadio;
            bool isNearRadio = IsNearWorkingRadio(p, isListening ? ACTIVE_RADIUS : PASSIVE_RADIUS);

            if (!isListening && !isNearRadio)
                return ThoughtState.Inactive;

            // Меломан получает усиленный эффект (stage 1)
            if (p.story?.traits?.HasTrait(RadioDefOf.MusicLover) == true)
                return ThoughtState.ActiveAtStage(1);

            // Обычный колонист (stage 0)
            return ThoughtState.ActiveAtStage(0);
        }

        private bool IsNearWorkingRadio(Pawn p, float radius)
        {
            return p.Map.listerBuildings.AllBuildingsColonistOfDef(RadioDefOf.MusicRadio)
                .OfType<Building>()
                .Any(b =>
                    b.TryGetComp<CompPowerTrader>()?.PowerOn == true &&
                    !b.IsBrokenDown() &&
                    p.Position.DistanceTo(b.Position) <= radius
                );
        }
    }

    // === НОВЫЙ КОД: ThoughtWorker для мизофонов ===
    public class ThoughtWorker_MusicHater : ThoughtWorker
    {
        private const float ANNOY_RADIUS = 7f;

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!p.Spawned || p.Dead)
                return ThoughtState.Inactive;

            // Только для мизофонов
            if (p.story?.traits?.HasTrait(RadioDefOf.MusicHater) != true)
                return ThoughtState.Inactive;

            bool radioNearby = p.Map.listerBuildings.AllBuildingsColonistOfDef(RadioDefOf.MusicRadio)
                .OfType<Building>()
                .Any(b =>
                    b.TryGetComp<CompPowerTrader>()?.PowerOn == true &&
                    !b.IsBrokenDown() &&
                    p.Position.DistanceTo(b.Position) <= ANNOY_RADIUS
                );

            if (radioNearby)
                return ThoughtState.ActiveAtStage(0);

            return ThoughtState.Inactive;
        }
    }
}