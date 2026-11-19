using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Crystal.WebAssets;

string repositoryRoot = FindRepositoryRoot();
string tempRoot = Path.Combine(Path.GetTempPath(), "crystal-web-assets-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    TestLibraryV2AndV3(tempRoot);
    TestMapVersions(tempRoot);
    TestMapSourceDiscovery(tempRoot);
    TestClientAssetPathResolution(tempRoot);
    TestPngWriter(tempRoot);
    TestHashStability();
    TestMapLibraryCatalog();
    TestClientMapLibraryCaseResolution(tempRoot, Path.Combine(repositoryRoot, "tests", "fixtures", "content", "Maps", "0.map"));
    TestDeterministicCompile(tempRoot, Path.Combine(repositoryRoot, "tests", "fixtures", "content", "Maps", "0.map"));
    TestMissingBuildHashFails(tempRoot, Path.Combine(repositoryRoot, "tests", "fixtures", "content", "Maps", "0.map"));
    TestMissingMapLibraryFails(tempRoot, Path.Combine(repositoryRoot, "tests", "fixtures", "content", "Maps", "0.map"));
    TestDuplicateMapIdsFail(tempRoot, Path.Combine(repositoryRoot, "tests", "fixtures", "content", "Maps", "0.map"));
    Console.WriteLine("Web asset checks passed.");
    return 0;
}
finally
{
    if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
}

void TestLibraryV2AndV3(string root)
{
    LibImage first = MakeImage(0, 2, 2,
        [
            10, 20, 30, 255, 40, 50, 60, 255,
            70, 80, 90, 255, 100, 110, 120, 255,
        ],
        mask: [200, 1, 2, 255, 3, 4, 5, 255, 6, 7, 8, 255, 9, 10, 11, 255],
        maskWidth: 2,
        maskHeight: 2);
    first = new LibImage
    {
        Index = first.Index,
        Width = first.Width,
        Height = first.Height,
        OffsetX = -3,
        OffsetY = 4,
        ShadowX = 5,
        ShadowY = -6,
        Shadow = 7,
        HasMask = true,
        MaskWidth = first.MaskWidth,
        MaskHeight = first.MaskHeight,
        MaskOffsetX = -1,
        MaskOffsetY = 2,
        MainBgra = first.MainBgra,
        MaskBgra = first.MaskBgra,
    };
    LibImage second = MakeImage(1, 3, 1, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]);

    string v2Path = Path.Combine(root, "synthetic-v2.lib");
    File.WriteAllBytes(v2Path, BuildLibrary(2, [first, second], null));
    LibDocument v2 = LibraryReader.Read(v2Path);
    Check(v2.Version == 2, "Synthetic v2 library version was not parsed.");
    Check(v2.Images.Count == 2, "Synthetic v2 library image count mismatch.");
    Check(v2.Images[0].MainBgra.SequenceEqual(first.MainBgra), "Synthetic v2 main pixels mismatch.");
    Check(v2.Images[0].MaskBgra is not null && v2.Images[0].MaskBgra!.SequenceEqual(first.MaskBgra!), "Synthetic v2 mask pixels mismatch.");
    Check(!v2.Images[1].HasMask && v2.Images[1].MaskBgra is null, "Synthetic v2 image unexpectedly has a mask.");

    string v3Path = Path.Combine(root, "synthetic-v3.lib");
    SortedDictionary<int, LibFrameData> frames = new()
    {
        [0] = new LibFrameData
        {
            Start = 4,
            Count = 6,
            Skip = 2,
            Interval = 120,
            EffectStart = 8,
            EffectCount = 3,
            EffectSkip = 1,
            EffectInterval = 80,
            Reverse = true,
            Blend = true,
        },
    };
    File.WriteAllBytes(v3Path, BuildLibrary(3, [first, second], frames));
    LibDocument v3 = LibraryReader.Read(v3Path);
    Check(v3.Version == 3, "Synthetic v3 library version was not parsed.");
    Check(v3.Frames.TryGetValue(0, out LibFrameData? frame) && frame.Count == 6 && frame.Reverse && frame.Blend,
        "Synthetic v3 frame table mismatch.");
    Check(v3.Images[0].MaskOffsetX == -1 && v3.Images[0].MaskOffsetY == 2, "Synthetic v3 mask offset mismatch.");

    LibImage padded = MakeImage(0, 5, 1, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20]);
    padded = new LibImage
    {
        Index = padded.Index,
        Width = padded.Width,
        Height = padded.Height,
        OffsetX = padded.OffsetX,
        OffsetY = padded.OffsetY,
        ShadowX = padded.ShadowX,
        ShadowY = padded.ShadowY,
        Shadow = padded.Shadow,
        HasMask = false,
        MaskWidth = 0,
        MaskHeight = 0,
        MaskOffsetX = 0,
        MaskOffsetY = 0,
        MainBgra = [.. padded.MainBgra, .. new byte[12]],
        MaskBgra = null,
    };
    string paddedPath = Path.Combine(root, "padded.lib");
    File.WriteAllBytes(paddedPath, BuildLibrary(2, [padded], null));
    LibDocument paddedDocument = LibraryReader.Read(paddedPath);
    Check(paddedDocument.Images[0].MainBgra.SequenceEqual(padded.MainBgra[..20]),
        "Padded library rows were not trimmed to the image dimensions.");

    bool truncatedRejected = false;
    try
    {
        string truncated = Path.Combine(root, "truncated.lib");
        byte[] bytes = BuildLibrary(2, [second], null);
        File.WriteAllBytes(truncated, bytes[..^4]);
        LibraryReader.Read(truncated);
    }
    catch (InvalidDataException)
    {
        truncatedRejected = true;
    }
    Check(truncatedRejected, "Truncated library payload was accepted.");
}

void TestMapVersions(string root)
{
    string v1Path = Path.Combine(root, "synthetic-v1.map");
    File.WriteAllBytes(v1Path, BuildMapV1());
    MapDocument v1 = MapReader.Read(v1Path);
    Check(v1.Format == 1 && v1.Width == 2 && v1.Height == 1, "Synthetic v1 map header mismatch.");
    Check((v1.Cells[0].BackImage & 0x20000000) != 0 && !v1.Cells[0].Walkable, "Synthetic v1 blocking flag mismatch.");
    Check(v1.Cells[0].FrontIndex == 90 && v1.Cells[0].FishingCell, "Synthetic v1 first cell mismatch.");
    Check(v1.Cells[1].MiddleImage == 5 && v1.Cells[1].FrontImage == 6 && v1.Cells[1].Light == 12,
        "Synthetic v1 second cell was not read at the 15-byte stride.");
    Check(v1.Cells[1].FrontIndex == 2 && v1.Cells[1].Walkable, "Synthetic v1 second cell state mismatch.");

    bool truncatedRejected = false;
    try
    {
        string truncatedPath = Path.Combine(root, "truncated-v1.map");
        File.WriteAllBytes(truncatedPath, BuildMapV1()[..^1]);
        MapReader.Read(truncatedPath);
    }
    catch (InvalidDataException)
    {
        truncatedRejected = true;
    }
    Check(truncatedRejected, "Truncated v1 map payload was accepted.");

    string v2Path = Path.Combine(root, "synthetic-v2.map");
    File.WriteAllBytes(v2Path, BuildMapV2());
    MapDocument v2 = MapReader.Read(v2Path);
    Check(v2.Format == 2 && v2.Width == 2 && v2.Height == 1, "Synthetic v2 map header mismatch.");
    Check(v2.Cells[0].DoorIndex == 7 && v2.Cells[0].FishingCell, "Synthetic v2 door/fishing fields mismatch.");
    Check((v2.Cells[0].BackImage & 0x20000000) != 0 && !v2.Cells[0].Walkable, "Synthetic v2 blocking flag mismatch.");
    Check(!v2.Cells[1].FishingCell && v2.Cells[1].Walkable, "Synthetic v2 second cell mismatch.");

    string v3Path = Path.Combine(root, "synthetic-v3.map");
    File.WriteAllBytes(v3Path, BuildMapV3());
    MapDocument v3 = MapReader.Read(v3Path);
    Check(v3.Format == 3 && v3.Width == 2 && v3.Height == 1, "Synthetic v3 map header mismatch.");
    Check(v3.Cells[0].TileAnimationImage == 44 && v3.Cells[0].TileAnimationFrames == 5 && v3.Cells[0].TileAnimationOffset == 6,
        "Synthetic v3 tile animation fields mismatch.");
    Check(v3.Cells[1].Light == 9 && v3.Cells[1].FrontIndex == 128, "Synthetic v3 second cell mismatch.");
}

void TestPngWriter(string root)
{
    string path = Path.Combine(root, "synthetic.png");
    byte[] rgba =
    [
        255, 0, 0, 255, 0, 255, 0, 128,
        0, 0, 255, 255, 10, 20, 30, 40,
    ];
    using (FileStream stream = File.Create(path)) PngWriter.WriteRgba(2, 2, rgba, stream);
    DecodedPng png = ReadPng(File.ReadAllBytes(path));
    Check(png.Width == 2 && png.Height == 2, "PNG dimensions mismatch.");
    Check(png.Rgba.SequenceEqual(rgba), "PNG RGBA pixels mismatch.");
}

void TestMapSourceDiscovery(string root)
{
    string mapRoot = Path.Combine(root, "discovery-map");
    Directory.CreateDirectory(Path.Combine(mapRoot, "nested"));
    File.WriteAllBytes(Path.Combine(mapRoot, "Alpha.MAP"), []);
    File.WriteAllBytes(Path.Combine(mapRoot, "beta.map"), []);
    File.WriteAllBytes(Path.Combine(mapRoot, "nested", "alpha.map"), []);
    File.WriteAllBytes(Path.Combine(mapRoot, "notes.txt"), []);

    IReadOnlyList<AssetMapSource> maps = MapSourceDiscovery.Discover(mapRoot);
    Check(maps.Count == 2, "Mixed-case map discovery returned the wrong count.");
    Check(maps.Any(map => map.Id == "Alpha" && map.FilePath.EndsWith("Alpha.MAP", StringComparison.Ordinal)),
        "Mixed-case uppercase map extension was not discovered.");
    Check(maps.Any(map => map.Id == "beta"), "Lowercase map extension was not discovered.");
}

void TestClientAssetPathResolution(string root)
{
    string clientRoot = Path.Combine(root, "case-client");
    string libraryDir = Path.Combine(clientRoot, "Data", "Map", "WemadeMir2");
    Directory.CreateDirectory(libraryDir);
    File.WriteAllBytes(Path.Combine(libraryDir, "SmTiles.Lib"), [1, 2, 3]);
    File.WriteAllBytes(Path.Combine(libraryDir, "Objects27.lib"), [4, 5, 6]);
    File.WriteAllBytes(Path.Combine(libraryDir, "Objects27.Lib"), [7, 8, 9]);
    Directory.CreateDirectory(Path.Combine(clientRoot, "Map"));
    File.WriteAllBytes(Path.Combine(clientRoot, "Map", "Alpha.MAP"), []);

    string? resolved = ClientAssetPath.ResolveDataFile(clientRoot, "Map/WemadeMir2/Smtiles.lib");
    Check(resolved is not null && Path.GetFileName(resolved) == "SmTiles.Lib",
        "Case-insensitive Data path did not resolve the real library file.");
    Check(ClientAssetPath.ResolveDataFile(clientRoot, "map/wemademir2/smtiles.LIB") == resolved,
        "Case-insensitive Data path is not stable across directory and extension casing.");
    Check(ClientAssetPath.ResolveDataFile(clientRoot, "Map/WemadeMir2/Missing.Lib") is null,
        "Missing Data path unexpectedly resolved.");
    Check(ClientAssetPath.ResolveDataFile(clientRoot, "../outside.lib") is null,
        "Path traversal was accepted by the Data asset resolver.");
    Check(ClientAssetPath.ResolveMapFile(clientRoot, "alpha.map") is not null,
        "Case-insensitive Map path did not resolve the real map file.");
    string? exact = ClientAssetPath.ResolveDataFile(clientRoot, "Map/WemadeMir2/Objects27.lib");
    Check(exact is not null && Path.GetFileName(exact) == "Objects27.lib",
        "Exact Data path did not win over a case-only duplicate.");
}

void TestHashStability()
{
    byte[] alpha = [1, 2, 3];
    byte[] bravo = [4, 5, 6];
    string first = HashUtil.Sha256Files([("a.bin", alpha), ("nested\\b.bin", bravo)]);
    string second = HashUtil.Sha256Files([("nested/b.bin", bravo), ("a.bin", alpha)]);
    Check(first == second, "HashUtil file ordering/path normalization is not deterministic.");
    Check(first.StartsWith("sha256:", StringComparison.Ordinal) && first.Length == 71, "HashUtil output format mismatch.");
}

void TestMapLibraryCatalog()
{
    Check(MapLibraryCatalog.Resolve(0) == "Map/WemadeMir2/Tiles", "Map library 0 mapping mismatch.");
    Check(MapLibraryCatalog.Resolve(2) == "Map/WemadeMir2/Objects", "Map library 2 mapping mismatch.");
    Check(MapLibraryCatalog.Resolve(3) == "Map/WemadeMir2/Objects2", "Map library 3 mapping mismatch.");
    Check(MapLibraryCatalog.Resolve(28) == "Map/WemadeMir2/Objects27", "Map library 28 mapping mismatch.");
    Check(MapLibraryCatalog.Resolve(29) is null, "Unsupported Wemade object index was mapped.");
    Check(MapLibraryCatalog.Resolve(101) == "Map/ShandaMir2/Tiles2", "Map library 101 mapping mismatch.");
    Check(MapLibraryCatalog.Resolve(110) == "Map/ShandaMir2/SmTiles", "Map library 110 mapping mismatch.");
    Check(MapLibraryCatalog.Resolve(121) == "Map/ShandaMir2/Objects2", "Map library 121 mapping mismatch.");
    Check(MapLibraryCatalog.Resolve(200) == "Map/WemadeMir3/Tilesc", "Map library 200 mapping mismatch.");
    Check(MapLibraryCatalog.Resolve(203) == "Map/WemadeMir3/Smtilesc", "Map library 203 mapping mismatch.");
    Check(MapLibraryCatalog.Resolve(300) == "Map/ShandaMir3/Tilesc", "Map library 300 mapping mismatch.");
    Check(MapLibraryCatalog.Resolve(302) == "Map/ShandaMir3/Tiles5c", "Map library 302 mapping mismatch.");
}

void TestDeterministicCompile(string root, string fixtureMap)
{
    string clientRoot = Path.Combine(root, "client");
    Directory.CreateDirectory(Path.Combine(clientRoot, "Data"));
    Directory.CreateDirectory(Path.Combine(clientRoot, "Map"));
    File.Copy(fixtureMap, Path.Combine(clientRoot, "Map", "0.map"));

    LibImage image = MakeImage(0, 2, 2,
        [10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255],
        mask: [20, 30, 40, 255, 50, 60, 70, 255, 80, 90, 100, 255, 110, 120, 130, 255],
        maskWidth: 2,
        maskHeight: 2);
    image = new LibImage
    {
        Index = image.Index,
        Width = image.Width,
        Height = image.Height,
        OffsetX = 1,
        OffsetY = -2,
        ShadowX = 3,
        ShadowY = -4,
        Shadow = 5,
        HasMask = true,
        MaskWidth = 2,
        MaskHeight = 2,
        MaskOffsetX = -3,
        MaskOffsetY = 4,
        MainBgra = image.MainBgra,
        MaskBgra = image.MaskBgra,
    };
    string libraryPath = Path.Combine(root, "compile-hero.lib");
    File.WriteAllBytes(libraryPath, BuildLibrary(3,
    [
        image,
        MakeImage(1, 1, 3, [1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255]),
    ], new SortedDictionary<int, LibFrameData>
    {
        [2] = new LibFrameData { Start = 0, Count = 2, Skip = 0, Interval = 100 },
    }));

    WebAssetCompileRequest request = new()
    {
        ClientRoot = clientRoot,
        OutRoot = Path.Combine(root, "out-a"),
        AssetVersion = "test-1",
        ClientBuildHash = "sha256:web-build",
        Libraries = [new AssetLibrarySource("Hero", libraryPath)],
        Maps = [new AssetMapSource("0", fixtureMap)],
    };
    WebAssetCompileResult first = WebAssetCompilerService.Compile(request);
    WebAssetCompileResult second = WebAssetCompilerService.Compile(new WebAssetCompileRequest
    {
        ClientRoot = request.ClientRoot,
        OutRoot = Path.Combine(root, "out-b"),
        AssetVersion = request.AssetVersion,
        ClientBuildHash = request.ClientBuildHash,
        Libraries = request.Libraries,
        Maps = request.Maps,
    });

    Check(first.Warnings.Count == 0, "Fixture compile emitted warnings: " + string.Join(" | ", first.Warnings));
    Check(first.Files.SequenceEqual(second.Files), "Repeated compile produced a different file set.");
    foreach (string relative in first.Files)
    {
        string left = Path.Combine(request.OutRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        string right = Path.Combine(root, "out-b", relative.Replace('/', Path.DirectorySeparatorChar));
        Check(File.ReadAllBytes(left).SequenceEqual(File.ReadAllBytes(right)), $"Repeated compile is not deterministic: {relative}");
    }

    Check(first.Manifest.ClientBuildHash == "sha256:web-build", "Manifest client build hash mismatch.");
    Check(first.Manifest.Libraries.TryGetValue("Hero", out WebAssetLibrary? library), "Compiled library is missing from manifest.");
    Check(library!.Pages.Count == 1 && library.MaskPages.Count == 1, "Compiled atlas page count mismatch.");
    Check(first.Manifest.Maps.TryGetValue("0", out WebAssetMap? map), "Fixture map is missing from manifest.");
    Check(map!.Width == 10 && map.Height == 10 && map.CellStride == 32, "Fixture map metadata mismatch.");

    byte[] metaBytes = File.ReadAllBytes(Path.Combine(request.OutRoot, map.MetaUrl));
    byte[] cellsBytes = File.ReadAllBytes(Path.Combine(request.OutRoot, map.CellsUrl));
    Check(cellsBytes.Length == 10 * 10 * 32, "Compiled cells.bin length mismatch.");
    Check(HashUtil.Sha256Files([(map.MetaUrl, metaBytes), (map.CellsUrl, cellsBytes)]) == map.Hash,
        "Compiled map hash mismatch.");
    using JsonDocument meta = JsonDocument.Parse(metaBytes);
    Check(meta.RootElement.GetProperty("format").GetInt32() == 0, "Fixture map format mismatch.");
    Check(meta.RootElement.GetProperty("cellOrder").GetString() == "x-major", "Fixture map cell order mismatch.");

    string framesPath = Path.Combine(request.OutRoot, library.FramesUrl);
    using JsonDocument frames = JsonDocument.Parse(File.ReadAllBytes(framesPath));
    JsonElement frameImages = frames.RootElement.GetProperty("images");
    Check(frameImages.GetArrayLength() == 2, "Compiled frames.json image count mismatch.");
    JsonElement firstFrame = frameImages[0];
    Check(firstFrame.GetProperty("mask").GetBoolean(), "Compiled frames.json lost mask flag.");
    Check(firstFrame.GetProperty("maskOffsetX").GetInt32() == -3 && firstFrame.GetProperty("maskOffsetY").GetInt32() == 4,
        "Compiled frames.json lost mask offsets.");
    Check(frames.RootElement.GetProperty("frames").GetProperty("2").GetProperty("count").GetInt32() == 2,
        "Compiled frames.json lost frame table.");

    DecodedPng atlas = ReadPng(File.ReadAllBytes(Path.Combine(request.OutRoot, library.Pages[0])));
    int atlasX = firstFrame.GetProperty("x").GetInt32();
    int atlasY = firstFrame.GetProperty("y").GetInt32();
    int atlasOffset = (atlasY * atlas.Width + atlasX) * 4;
    Check(atlas.Rgba[atlasOffset] == 30 && atlas.Rgba[atlasOffset + 1] == 20 && atlas.Rgba[atlasOffset + 2] == 10 && atlas.Rgba[atlasOffset + 3] == 255,
        "Atlas BGRA-to-RGBA conversion mismatch.");

    DecodedPng mask = ReadPng(File.ReadAllBytes(Path.Combine(request.OutRoot, library.MaskPages[0])));
    int maskX = firstFrame.GetProperty("maskX").GetInt32();
    int maskY = firstFrame.GetProperty("maskY").GetInt32();
    int maskOffset = (maskY * mask.Width + maskX) * 4;
    Check(mask.Rgba[maskOffset] == 40 && mask.Rgba[maskOffset + 1] == 30 && mask.Rgba[maskOffset + 2] == 20 && mask.Rgba[maskOffset + 3] == 255,
        "Mask atlas BGRA-to-RGBA conversion mismatch.");

    WebAssetCompileResult explicitHash = WebAssetCompilerService.Compile(new WebAssetCompileRequest
    {
        ClientRoot = request.ClientRoot,
        OutRoot = Path.Combine(root, "out-c"),
        AssetVersion = request.AssetVersion,
        ClientBuildHash = "sha256:web-build",
        Libraries = request.Libraries,
        Maps = request.Maps,
    });
    Check(explicitHash.Manifest.ClientBuildHash == "sha256:web-build", "Explicit client build hash was not preserved.");
}

void TestMissingMapLibraryFails(string root, string fixtureMap)
{
    bool failed = false;
    try
    {
        WebAssetCompilerService.Compile(new WebAssetCompileRequest
        {
            ClientRoot = Path.Combine(root, "empty-client"),
            OutRoot = Path.Combine(root, "out-missing-map-lib"),
            AssetVersion = "test-missing",
            ClientBuildHash = "sha256:web-build",
            Libraries = [],
            Maps = [new AssetMapSource("0", fixtureMap)],
            IncludeMapLibraries = true,
        });
    }
    catch (FileNotFoundException)
    {
        failed = true;
    }
    Check(failed, "Missing map library was silently accepted with IncludeMapLibraries=true.");
}

void TestClientMapLibraryCaseResolution(string root, string fixtureMap)
{
    string clientRoot = Path.Combine(root, "case-map-client");
    Directory.CreateDirectory(Path.Combine(clientRoot, "Map"));
    string mapPath = Path.Combine(clientRoot, "Map", "0.map");
    File.Copy(fixtureMap, mapPath);

    MapDocument document = MapReader.Read(mapPath);
    byte[] library = BuildLibrary(2, [MakeImage(0, 1, 1, [1, 2, 3, 255])], null);
    foreach (int libraryId in document.LibraryIds)
    {
        string? relative = MapLibraryCatalog.Resolve(libraryId);
        Check(relative is not null, $"Fixture map references unknown library {libraryId}.");
        string lowered = string.Join('/', relative!.Split('/').Select(static part => part.ToLowerInvariant())) + ".lib";
        string path = Path.Combine(clientRoot, "Data", lowered.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, library);
    }

    WebAssetCompileResult result = WebAssetCompilerService.Compile(new WebAssetCompileRequest
    {
        ClientRoot = clientRoot,
        OutRoot = Path.Combine(root, "out-case-map-lib"),
        AssetVersion = "test-case-map-lib",
        ClientBuildHash = "sha256:web-build",
        Libraries = [],
        Maps = [new AssetMapSource("0", mapPath)],
        IncludeMapLibraries = true,
    });
    Check(result.Warnings.Count == 0, "Case-insensitive map library compile emitted warnings: " + string.Join(" | ", result.Warnings));
    Check(result.Manifest.MapLibraries.Count == document.LibraryIds.Count,
        "Case-insensitive map library compile did not register every map library.");
}

void TestMissingBuildHashFails(string root, string fixtureMap)
{
    bool failed = false;
    try
    {
        WebAssetCompilerService.Compile(new WebAssetCompileRequest
        {
            ClientRoot = Path.Combine(root, "empty-client"),
            OutRoot = Path.Combine(root, "out-missing-build-hash"),
            AssetVersion = "test-no-build-hash",
            ClientBuildHash = string.Empty,
            Libraries = [],
            Maps = [new AssetMapSource("0", fixtureMap)],
        });
    }
    catch (ArgumentException)
    {
        failed = true;
    }
    Check(failed, "Web asset compilation accepted an empty client build hash.");
}

void TestDuplicateMapIdsFail(string root, string fixtureMap)
{
    bool failed = false;
    try
    {
        WebAssetCompilerService.Compile(new WebAssetCompileRequest
        {
            ClientRoot = Path.Combine(root, "duplicate-client"),
            OutRoot = Path.Combine(root, "out-duplicate"),
            AssetVersion = "test-duplicate",
            ClientBuildHash = "sha256:web-build",
            Libraries = [],
            Maps =
            [
                new AssetMapSource("a/b", fixtureMap),
                new AssetMapSource("a-b", fixtureMap),
            ],
        });
    }
    catch (InvalidDataException)
    {
        failed = true;
    }
    Check(failed, "Colliding sanitized map ids were silently accepted.");
}

static LibImage MakeImage(
    int index,
    short width,
    short height,
    byte[] main,
    byte[]? mask = null,
    short maskWidth = 0,
    short maskHeight = 0)
{
    return new LibImage
    {
        Index = index,
        Width = width,
        Height = height,
        OffsetX = 0,
        OffsetY = 0,
        ShadowX = 0,
        ShadowY = 0,
        Shadow = 0,
        HasMask = mask is not null,
        MaskWidth = maskWidth,
        MaskHeight = maskHeight,
        MaskOffsetX = 0,
        MaskOffsetY = 0,
        MainBgra = main,
        MaskBgra = mask,
    };
}

static byte[] BuildLibrary(int version, IReadOnlyList<LibImage> images, IReadOnlyDictionary<int, LibFrameData>? frames)
{
    int headerSize = (version >= 3 ? 12 : 8) + images.Count * 4;
    using MemoryStream imageStream = new();
    using (BinaryWriter imageWriter = new(imageStream, Encoding.UTF8, leaveOpen: true))
    {
        foreach (LibImage image in images)
        {
            imageWriter.Write((short)image.Width);
            imageWriter.Write((short)image.Height);
            imageWriter.Write((short)image.OffsetX);
            imageWriter.Write((short)image.OffsetY);
            imageWriter.Write((short)image.ShadowX);
            imageWriter.Write((short)image.ShadowY);
            imageWriter.Write(image.HasMask ? (byte)(image.Shadow | 0x80) : image.Shadow);
            byte[] compressed = Compress(image.MainBgra);
            imageWriter.Write(compressed.Length);
            imageWriter.Write(compressed);
            if (image.HasMask)
            {
                imageWriter.Write((short)image.MaskWidth);
                imageWriter.Write((short)image.MaskHeight);
                imageWriter.Write((short)image.MaskOffsetX);
                imageWriter.Write((short)image.MaskOffsetY);
                byte[] mask = Compress(image.MaskBgra!);
                imageWriter.Write(mask.Length);
                imageWriter.Write(mask);
            }
        }
    }

    byte[] imageBytes = imageStream.ToArray();
    int[] offsets = new int[images.Count];
    int cursor = headerSize;
    for (int i = 0; i < images.Count; i++)
    {
        offsets[i] = cursor;
        cursor += ImageLength(images[i]);
    }

    using MemoryStream output = new();
    using (BinaryWriter writer = new(output, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(version);
        writer.Write(images.Count);
        if (version >= 3) writer.Write(headerSize + imageBytes.Length);
        foreach (int offset in offsets) writer.Write(offset);
        writer.Write(imageBytes);
        if (version >= 3)
        {
            writer.Write(frames?.Count ?? 0);
            if (frames is not null)
            {
                foreach ((int action, LibFrameData frame) in frames.OrderBy(static pair => pair.Key))
                {
                    writer.Write((byte)action);
                    writer.Write(frame.Start);
                    writer.Write(frame.Count);
                    writer.Write(frame.Skip);
                    writer.Write(frame.Interval);
                    writer.Write(frame.EffectStart);
                    writer.Write(frame.EffectCount);
                    writer.Write(frame.EffectSkip);
                    writer.Write(frame.EffectInterval);
                    writer.Write(frame.Reverse);
                    writer.Write(frame.Blend);
                }
            }
        }
    }
    return output.ToArray();
}

static int ImageLength(LibImage image)
{
    int length = 2 + 2 + 2 + 2 + 2 + 2 + 1 + 4 + Compress(image.MainBgra).Length;
    if (image.HasMask) length += 2 + 2 + 2 + 2 + 4 + Compress(image.MaskBgra!).Length;
    return length;
}

static byte[] BuildMapV2()
{
    int width = 2;
    int height = 1;
    byte[] bytes = new byte[52 + width * height * 14];
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0), (short)width);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), (short)height);
    bytes[4] = 0x0F;
    int offset = 52;
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), 0x8000);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2), 3);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 4), 4);
    bytes[offset + 6] = 7;
    bytes[offset + 7] = 9;
    bytes[offset + 8] = 1;
    bytes[offset + 9] = 2;
    bytes[offset + 10] = 3;
    bytes[offset + 11] = 105;
    bytes[offset + 12] = 4;
    bytes[offset + 13] = 5;

    offset += 14;
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 4), 3);
    bytes[offset + 6] = 0;
    bytes[offset + 7] = 0;
    bytes[offset + 8] = 0;
    bytes[offset + 9] = 0;
    bytes[offset + 10] = 1;
    bytes[offset + 11] = 12;
    bytes[offset + 12] = 0;
    bytes[offset + 13] = 1;
    return bytes;
}

static byte[] BuildMapV1()
{
    const int width = 2;
    const int height = 1;
    const int headerSize = 54;
    const int cellStride = 15;
    byte[] bytes = new byte[headerSize + width * height * cellStride];
    bytes[0] = 0x10;
    Encoding.ASCII.GetBytes("Map 2010 Ver 1.0").CopyTo(bytes, 1);
    short xor = 0x1234;
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(21), (short)(width ^ xor));
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(23), xor);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(25), (short)(height ^ xor));

    WriteV1Cell(bytes, headerSize,
        backImage: 0x8000,
        middleImage: 3,
        frontImage: 4,
        doorIndex: 7,
        doorOffset: 9,
        frontAnimationFrame: 1,
        frontAnimationTick: 2,
        frontIndex: 100,
        light: 105);
    WriteV1Cell(bytes, headerSize + cellStride,
        backImage: 0,
        middleImage: 5,
        frontImage: 6,
        doorIndex: 0,
        doorOffset: 0,
        frontAnimationFrame: 0,
        frontAnimationTick: 0,
        frontIndex: 0,
        light: 12);
    return bytes;
}

static void WriteV1Cell(
    byte[] bytes,
    int offset,
    int backImage,
    short middleImage,
    short frontImage,
    byte doorIndex,
    byte doorOffset,
    byte frontAnimationFrame,
    byte frontAnimationTick,
    byte frontIndex,
    byte light)
{
    short xor = 0x1234;
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), backImage ^ unchecked((int)0xAA38AA38));
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 4), (short)(middleImage ^ xor));
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 6), (short)(frontImage ^ xor));
    bytes[offset + 8] = doorIndex;
    bytes[offset + 9] = doorOffset;
    bytes[offset + 10] = frontAnimationFrame;
    bytes[offset + 11] = frontAnimationTick;
    bytes[offset + 12] = frontIndex;
    bytes[offset + 13] = light;
}

static byte[] BuildMapV3()
{
    int width = 2;
    int height = 1;
    byte[] bytes = new byte[52 + width * height * 36];
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(0), (short)width);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), (short)height);
    bytes[4] = 0x0F;
    int offset = 52;
    WriteV3Cell(bytes, offset, backIndex: 2, middleIndex: 4, light: 8, frontIndex: 6, tileImage: 44, tileFrames: 5, tileOffset: 6);
    offset += 36;
    WriteV3Cell(bytes, offset, backIndex: 3, middleIndex: 5, light: 9, frontIndex: 8, tileImage: 0, tileFrames: 0, tileOffset: 0);
    return bytes;
}

static void WriteV3Cell(byte[] bytes, int offset, byte backIndex, byte middleIndex, byte light, byte frontIndex, short tileImage, byte tileFrames, short tileOffset)
{
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 2), 2);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 4), 3);
    bytes[offset + 6] = 0;
    bytes[offset + 7] = 0;
    bytes[offset + 8] = 0;
    bytes[offset + 9] = 0;
    bytes[offset + 10] = frontIndex;
    bytes[offset + 11] = light;
    bytes[offset + 12] = backIndex;
    bytes[offset + 13] = middleIndex;
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 14), tileImage);
    bytes[offset + 21] = tileFrames;
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset + 22), tileOffset);
}

static byte[] Compress(byte[] bytes)
{
    using MemoryStream output = new();
    using (GZipStream gzip = new(output, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(bytes);
    return output.ToArray();
}

static DecodedPng ReadPng(byte[] bytes)
{
    byte[] signature = [137, 80, 78, 71, 13, 10, 26, 10];
    Check(bytes.AsSpan(0, 8).SequenceEqual(signature), "PNG signature mismatch.");
    int offset = 8;
    int width = 0;
    int height = 0;
    using MemoryStream idat = new();
    while (offset + 12 <= bytes.Length)
    {
        int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset));
        string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
        ReadOnlySpan<byte> data = bytes.AsSpan(offset + 8, length);
        if (type == "IHDR")
        {
            width = BinaryPrimitives.ReadInt32BigEndian(data[0..4]);
            height = BinaryPrimitives.ReadInt32BigEndian(data[4..8]);
            Check(data[8] == 8 && data[9] == 6, "PNG is not 8-bit RGBA.");
        }
        else if (type == "IDAT")
        {
            idat.Write(data);
        }

        offset += 12 + length;
        if (type == "IEND") break;
    }
    idat.Position = 0;
    using ZLibStream zlib = new(idat, CompressionMode.Decompress, leaveOpen: true);
    using MemoryStream raw = new();
    zlib.CopyTo(raw);
    byte[] filtered = raw.ToArray();
    int stride = width * 4;
    Check(filtered.Length == height * (stride + 1), "PNG decompressed length mismatch.");
    byte[] rgba = new byte[width * height * 4];
    for (int row = 0; row < height; row++)
    {
        int source = row * (stride + 1);
        Check(filtered[source] == 0, "PNG uses an unsupported filter.");
        Buffer.BlockCopy(filtered, source + 1, rgba, row * stride, stride);
    }
    return new DecodedPng(width, height, rgba);
}

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Legend of Mir.sln"))) return directory.FullName;
        directory = directory.Parent;
    }
    throw new DirectoryNotFoundException("Cannot find the Crystal repository root.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed record DecodedPng(int Width, int Height, byte[] Rgba);
