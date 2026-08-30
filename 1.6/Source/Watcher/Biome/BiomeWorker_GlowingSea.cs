using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Watcher.GlowingSeaBiome
{
    public class BiomeWorker_GlowingSea : BiomeWorker
    {
        public override float GetScore(BiomeDef biomeDef, Tile tile, PlanetTile planetTile)
        {
            // Запрещаем появление на воде
            if (tile.WaterCovered)
                return -100f;

            // Запрещаем слишком холодные регионы
            if (tile.temperature < -10f)
                return -100f;

            float score = 0f;

            // Добавляем бонусы за различные параметры
            score += GetScoreForRainfall(tile.rainfall);
            score += GetScoreForTemperature(tile.temperature);
            score += GetScoreForElevation(tile.elevation);
            score += GetPollutionBonus(tile);

            // Добавляем небольшой бонус за близость к воде (для прибрежных зон)
            score += GetCoastalBonus(tile);

            // Добавляем случайный фактор для разнообразия
            return score * Rand.Range(0.95f, 1.05f);
        }

        private float GetScoreForRainfall(float rainfall)
        {
            if (rainfall < 300f) return 40f;    // Пустынные условия
            if (rainfall < 500f) return 25f;    // Полупустыня
            if (rainfall < 800f) return 10f;    // Засушливые
            if (rainfall < 1200f) return -5f;   // Умеренные
            return -20f;                        // Влажные - не подходят
        }

        private float GetScoreForTemperature(float temperature)
        {
            if (temperature > 30f) return 25f;  // Очень жарко - идеально
            if (temperature > 20f) return 20f;  // Жарко - хорошо
            if (temperature > 10f) return 15f;  // Тепло - приемлемо
            if (temperature > 0f) return 5f;    // Прохладно - не очень
            if (temperature > -10f) return -10f; // Холодно - плохо
            return -25f;                        // Очень холодно - запрещено
        }

        private float GetScoreForElevation(float elevation)
        {
            if (elevation < 300f) return 15f;   // Низменности - идеально
            if (elevation < 500f) return 10f;   // Низкие холмы - хорошо
            if (elevation < 1000f) return 0f;   // Средняя высота
            if (elevation < 2000f) return -10f; // Высокогорье - плохо
            return -25f;                        // Очень высоко - запрещено
        }

        private float GetPollutionBonus(Tile tile)
        {
            // Биом должен появляться в загрязнённых зонах
            if (tile.pollution > 15f) return 20f;   // Сильное загрязнение - идеально
            if (tile.pollution > 5f) return 15f;    // Среднее загрязнение - хорошо
            if (tile.pollution > 0.5f) return 5f;   // Слабое загрязнение
            return -20f;                            // Чистые зоны - не подходят
        }

        private float GetCoastalBonus(Tile tile)
        {
            // Проверяем соседние тайлы на наличие воды
            if (tile.elevation < 50f) return 10f;
            return 0f;
        }
    }
}