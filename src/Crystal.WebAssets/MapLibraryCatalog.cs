namespace Crystal.WebAssets;

public static class MapLibraryCatalog
{
    private static readonly Dictionary<int, string> Entries = BuildEntries();

    public static IReadOnlyDictionary<int, string> All => Entries;

    public static string? Resolve(int index) => Entries.TryGetValue(index, out string? value) ? value : null;

    private static Dictionary<int, string> BuildEntries()
    {
        Dictionary<int, string> map = new()
        {
            [0] = "Map/WemadeMir2/Tiles",
            [1] = "Map/WemadeMir2/Smtiles",
            [2] = "Map/WemadeMir2/Objects",
            [90] = "Map/WemadeMir2/Objects_32bit",
            [100] = "Map/ShandaMir2/Tiles",
            [110] = "Map/ShandaMir2/SmTiles",
            [120] = "Map/ShandaMir2/Objects",
            [190] = "Map/ShandaMir2/AniTiles1",
        };

        for (int i = 2; i < 28; i++) map[i + 1] = $"Map/WemadeMir2/Objects{i}";
        for (int i = 1; i <= 9; i++) map[100 + i] = $"Map/ShandaMir2/Tiles{i + 1}";
        for (int i = 1; i <= 9; i++) map[110 + i] = $"Map/ShandaMir2/SmTiles{i + 1}";
        for (int i = 1; i <= 30; i++) map[120 + i] = $"Map/ShandaMir2/Objects{i + 1}";

        string[] wemadeStates = ["", "wood/", "sand/", "snow/", "forest/"];
        for (int i = 0; i < wemadeStates.Length; i++)
        {
            string[] names = ["Tilesc", "Tiles30c", "Tiles5c", "Smtilesc", "Housesc", "Cliffsc", "Dungeonsc", "Innersc", "Furnituresc", "Wallsc", "smObjectsc", "Animationsc", "Object1c", "Object2c"];
            for (int n = 0; n < names.Length; n++)
                map[200 + i * 15 + n] = $"Map/WemadeMir3/{wemadeStates[i]}{names[n]}";

            string[] shandaSuffix = ["", "wood", "sand", "snow", "forest"];
            for (int n = 0; n < names.Length; n++)
                map[300 + i * 15 + n] = $"Map/ShandaMir3/{names[n]}{shandaSuffix[i]}";
        }

        return map;
    }
}
