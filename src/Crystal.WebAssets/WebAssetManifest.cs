using System.Text.Json;

namespace Crystal.WebAssets;

public sealed class WebAssetFile
{
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public required long Size { get; init; }
}

public sealed class WebAssetLibrary
{
    public required string SourceHash { get; init; }
    public required string FramesUrl { get; init; }
    public required IReadOnlyList<string> Pages { get; init; }
    public required IReadOnlyList<string> MaskPages { get; init; }
    public required string Hash { get; init; }
}

public sealed class WebAssetMap
{
    public required string SourceHash { get; init; }
    public required int Format { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string CellOrder { get; init; }
    public required int CellStride { get; init; }
    public required string MetaUrl { get; init; }
    public required string CellsUrl { get; init; }
    public required IReadOnlyList<int> LibraryIds { get; init; }
    public required string Hash { get; init; }
}

public sealed class WebAssetManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string AssetVersion { get; init; }
    public string ClientBuildHash { get; set; } = string.Empty;
    public required Dictionary<string, WebAssetLibrary> Libraries { get; init; }
    public required Dictionary<string, WebAssetMap> Maps { get; init; }
    public required Dictionary<string, string> MapLibraries { get; init; }
}

public static class WebAssetManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize(WebAssetManifest manifest) => JsonSerializer.Serialize(manifest, Options);
}
