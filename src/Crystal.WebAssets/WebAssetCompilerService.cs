using System.Text.Json;

namespace Crystal.WebAssets;

public sealed record AssetLibrarySource(string Id, string FilePath);
public sealed record AssetMapSource(string Id, string FilePath);

public sealed class WebAssetCompileRequest
{
    public required string ClientRoot { get; init; }
    public required string OutRoot { get; init; }
    public required string AssetVersion { get; init; }
    public required string ClientBuildHash { get; init; }
    public required IReadOnlyList<AssetLibrarySource> Libraries { get; init; }
    public required IReadOnlyList<AssetMapSource> Maps { get; init; }
    public bool IncludeMapLibraries { get; init; }
}

public sealed class WebAssetCompileResult
{
    public required WebAssetManifest Manifest { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required IReadOnlyList<string> Files { get; init; }
}

public static class WebAssetCompilerService
{
    public static WebAssetCompileResult Compile(WebAssetCompileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientBuildHash))
            throw new ArgumentException("ClientBuildHash is required so Web assets and the version gate share one build identity.", nameof(request));

        string outRoot = Path.GetFullPath(request.OutRoot);
        Directory.CreateDirectory(outRoot);

        Dictionary<string, WebAssetLibrary> libraries = new(StringComparer.Ordinal);
        Dictionary<string, WebAssetMap> maps = new(StringComparer.Ordinal);
        Dictionary<string, string> mapLibraries = new(StringComparer.Ordinal);
        List<string> warnings = [];
        List<string> generatedFiles = [];

        foreach (AssetLibrarySource source in request.Libraries.OrderBy(static value => value.Id, StringComparer.Ordinal))
        {
            string libraryId = AssetPath.SafeSegment(source.Id);
            if (libraries.ContainsKey(libraryId))
                throw new InvalidDataException($"Duplicate web library id '{libraryId}'.");
            CompileLibrary(source, outRoot, libraries, generatedFiles);
        }

        foreach (AssetMapSource source in request.Maps.OrderBy(static value => value.Id, StringComparer.Ordinal))
        {
            MapDocument document = MapReader.Read(source.FilePath);
            string mapId = AssetPath.SafeSegment(source.Id);
            if (maps.ContainsKey(mapId))
                throw new InvalidDataException($"Duplicate web map id '{mapId}'.");
            string mapDir = Path.Combine(outRoot, "map", mapId);
            Directory.CreateDirectory(mapDir);

            string cellsRelative = $"map/{mapId}/cells.bin";
            string metaRelative = $"map/{mapId}/meta.json";
            byte[] cells = MapReader.WriteCells(document);
            string cellsPath = Path.Combine(outRoot, cellsRelative);
            File.WriteAllBytes(cellsPath, cells);
            generatedFiles.Add(cellsRelative);

            object meta = new
            {
                schemaVersion = 1,
                file = Path.GetFileNameWithoutExtension(document.FileName),
                sourceHash = document.SourceHash,
                format = document.Format,
                width = document.Width,
                height = document.Height,
                cellOrder = MapReader.CellOrder,
                cellStride = MapReader.CellStride,
                cellsUrl = cellsRelative,
                libraries = document.LibraryIds,
            };
            byte[] metaBytes = JsonSerializer.SerializeToUtf8Bytes(meta, WebAssetManifestJson.Options);
            File.WriteAllBytes(Path.Combine(outRoot, metaRelative), metaBytes);
            generatedFiles.Add(metaRelative);

            maps[mapId] = new WebAssetMap
            {
                SourceHash = document.SourceHash,
                Format = document.Format,
                Width = document.Width,
                Height = document.Height,
                CellOrder = MapReader.CellOrder,
                CellStride = MapReader.CellStride,
                MetaUrl = metaRelative,
                CellsUrl = cellsRelative,
                LibraryIds = document.LibraryIds,
                Hash = HashUtil.Sha256Files(
                [
                    (metaRelative, metaBytes),
                    (cellsRelative, cells),
                ]),
            };

            foreach (int libraryId in document.LibraryIds)
            {
                if (mapLibraries.ContainsKey(libraryId.ToString())) continue;
                string? relative = MapLibraryCatalog.Resolve(libraryId);
                if (relative is null)
                {
                    if (request.IncludeMapLibraries)
                        throw new InvalidDataException($"Map library {libraryId} is not present in MapLibraryCatalog.");
                    warnings.Add($"Map library {libraryId} is not present in MapLibraryCatalog; runtime will use placeholder terrain.");
                    continue;
                }
                mapLibraries[libraryId.ToString()] = $"maplib-{libraryId}";

                if (!request.IncludeMapLibraries) continue;
                string expectedLibraryPath = Path.Combine(request.ClientRoot, "Data", relative.Replace('/', Path.DirectorySeparatorChar) + ".lib");
                string? libraryPath = ClientAssetPath.ResolveDataFile(request.ClientRoot, relative + ".lib");
                if (libraryPath is null)
                    throw new FileNotFoundException($"Map library {libraryId} is referenced but missing.", expectedLibraryPath);

                string libraryIdText = $"maplib-{libraryId}";
                if (!libraries.ContainsKey(libraryIdText))
                    CompileLibrary(new AssetLibrarySource(libraryIdText, libraryPath), outRoot, libraries, generatedFiles);
            }
        }

        WebAssetManifest manifest = new()
        {
            AssetVersion = request.AssetVersion,
            Libraries = libraries,
            Maps = maps,
            MapLibraries = mapLibraries,
        };

        manifest.ClientBuildHash = request.ClientBuildHash;

        string manifestRelative = "manifest.json";
        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, WebAssetManifestJson.Options);
        File.WriteAllBytes(Path.Combine(outRoot, manifestRelative), manifestBytes);
        generatedFiles.Add(manifestRelative);

        return new WebAssetCompileResult
        {
            Manifest = manifest,
            Warnings = warnings,
            Files = generatedFiles.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
        };
    }

    private static void CompileLibrary(
        AssetLibrarySource source,
        string outRoot,
        Dictionary<string, WebAssetLibrary> libraries,
        List<string> generatedFiles)
    {
        LibDocument document = LibraryReader.Read(source.FilePath);
        string id = AssetPath.SafeSegment(source.Id);
        string libraryDir = Path.Combine(outRoot, "lib", id);
        Directory.CreateDirectory(libraryDir);

        AtlasResult pages = AtlasPacker.Build(document.Images, pagePrefix: "atlas");
        List<LibImage> maskImages = document.Images
            .Where(static image => image.HasMask && image.MaskBgra is { Length: > 0 } && image.MaskWidth > 0 && image.MaskHeight > 0)
            .Select(static image => new LibImage
            {
                Index = image.Index,
                Width = image.MaskWidth,
                Height = image.MaskHeight,
                OffsetX = image.MaskOffsetX,
                OffsetY = image.MaskOffsetY,
                ShadowX = image.ShadowX,
                ShadowY = image.ShadowY,
                Shadow = image.Shadow,
                HasMask = false,
                MaskWidth = 0,
                MaskHeight = 0,
                MaskOffsetX = 0,
                MaskOffsetY = 0,
                MainBgra = image.MaskBgra!,
                MaskBgra = null,
            })
            .ToList();
        AtlasResult maskPages = AtlasPacker.Build(maskImages, pagePrefix: "mask");

        foreach (AtlasPage page in pages.Pages)
        {
            string relative = $"lib/{id}/{page.PageFile}";
            using FileStream stream = File.Create(Path.Combine(outRoot, relative));
            PngWriter.WriteRgba(page.Width, page.Height, page.Rgba, stream);
            generatedFiles.Add(relative);
        }
        foreach (AtlasPage page in maskPages.Pages)
        {
            string relative = $"lib/{id}/{page.PageFile}";
            using FileStream stream = File.Create(Path.Combine(outRoot, relative));
            PngWriter.WriteRgba(page.Width, page.Height, page.Rgba, stream);
            generatedFiles.Add(relative);
        }

        string framesRelative = $"lib/{id}/frames.json";
        byte[] framesBytes = System.Text.Encoding.UTF8.GetBytes(LibraryReader.FramesJson(document, pages.Placements, maskPages.Placements));
        File.WriteAllBytes(Path.Combine(outRoot, framesRelative), framesBytes);
        generatedFiles.Add(framesRelative);

        libraries[id] = new WebAssetLibrary
        {
            SourceHash = HashUtil.Sha256File(source.FilePath),
            FramesUrl = framesRelative,
            Pages = pages.Pages.Select(page => $"lib/{id}/{page.PageFile}").ToArray(),
            MaskPages = maskPages.Pages.Select(page => $"lib/{id}/{page.PageFile}").ToArray(),
            Hash = HashUtil.Sha256Files(
            [
                (framesRelative, framesBytes),
                .. pages.Pages.Select(page => ($"lib/{id}/{page.PageFile}", File.ReadAllBytes(Path.Combine(outRoot, "lib", id, page.PageFile)))),
                .. maskPages.Pages.Select(page => ($"lib/{id}/{page.PageFile}", File.ReadAllBytes(Path.Combine(outRoot, "lib", id, page.PageFile)))),
            ]),
        };
    }
}
