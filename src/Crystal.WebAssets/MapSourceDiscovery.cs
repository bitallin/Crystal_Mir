namespace Crystal.WebAssets;

public static class MapSourceDiscovery
{
    public static IReadOnlyList<AssetMapSource> Discover(string mapRoot)
    {
        if (!Directory.Exists(mapRoot)) return [];

        List<AssetMapSource> maps = [];
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(mapRoot, "*", SearchOption.AllDirectories)
                     .Where(static path => path.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            if (ids.Add(id)) maps.Add(new AssetMapSource(id, path));
        }

        return maps;
    }
}
