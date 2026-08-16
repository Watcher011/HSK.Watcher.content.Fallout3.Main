using RimWorld;
using Verse;

namespace Watcher
{
    [DefOf]
    public static class FalloutDamageDefOf
    {
        public static DamageDef FalloutBomb;
    }

    [DefOf]
    public static class FactionDefMY
    {
        public static FactionDef Enclave;

        public static FactionDef Vaultec;

        static FactionDefMY()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(FactionDefMY));
        }
    }


    [DefOf]
    public class ThingDefOfMYLocal
    {
        public static ThingDef WatcherArt;
        public static ThingDef VaultTecPortableGenerator;
        public static ThingDef NuclearLandmine;
        public static ThingDef Brain;
        public static ThingDef OilWell;
        public static ThingDef Apparel_VaultX01PowerArmor;
        public static ThingDef Apparel_VaultX01ArmorHelmet;
        public static ThingDef Silver;
    }

}
