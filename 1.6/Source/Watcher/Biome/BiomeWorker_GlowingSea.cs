using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Watcher.GlowingSeaBiome
{
    public class BiomeWorker_GlowingSea : BiomeWorker
    {
        public override float GetScore(BiomeDef biomeDef, Tile tile, PlanetTile planetTile)
        {
            if (tile.WaterCovered)
                return -5f;

            if (tile.temperature < -10f)
                return -100f;

            float score = 0f;

            score += GetScoreForRainfall(tile.rainfall);
            score += GetScoreForTemperature(tile.temperature);
            score += GetScoreForElevation(tile.elevation);
            score += GetPollutionBonus(tile);

            return score * Rand.Range(0.9f, 1.1f);
        }

        private float GetScoreForRainfall(float rainfall)
        {
            if (rainfall < 300f) return 20f;
            if (rainfall < 500f) return 10f;
            if (rainfall < 800f) return 0f;
            if (rainfall < 1200f) return -5f;
            return -15f;
        }

        private float GetScoreForTemperature(float temperature)
        {
            if (temperature > 30f) return 15f;
            if (temperature > 20f) return 10f;
            if (temperature > 10f) return 5f;
            if (temperature > 0f) return 0f;
            if (temperature > -10f) return -5f;
            return -15f;
        }

        private float GetScoreForElevation(float elevation)
        {
            if (elevation < 500f) return 10f;
            if (elevation < 1000f) return 5f;
            if (elevation < 2000f) return 0f;
            if (elevation < 3000f) return -5f;
            return -15f;
        }

        private float GetPollutionBonus(Tile tile)
        {
            if (tile.pollution > 15f) return 10f;
            if (tile.pollution > 0.05f) return 5f;
            return 0f;
        }
    }
}