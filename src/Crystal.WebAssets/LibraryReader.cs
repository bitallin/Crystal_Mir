using System.IO.Compression;
using System.Text.Json;

namespace Crystal.WebAssets;

public sealed class LibFrameData
{
    public int Start { get; init; }
    public int Count { get; init; }
    public int Skip { get; init; }
    public int Interval { get; init; }
    public int EffectStart { get; init; }
    public int EffectCount { get; init; }
    public int EffectSkip { get; init; }
    public int EffectInterval { get; init; }
    public bool Reverse { get; init; }
    public bool Blend { get; init; }
}

public sealed class LibImage
{
    public required int Index { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int OffsetX { get; init; }
    public required int OffsetY { get; init; }
    public required int ShadowX { get; init; }
    public required int ShadowY { get; init; }
    public required byte Shadow { get; init; }
    public required bool HasMask { get; init; }
    public required int MaskWidth { get; init; }
    public required int MaskHeight { get; init; }
    public required int MaskOffsetX { get; init; }
    public required int MaskOffsetY { get; init; }
    public required byte[] MainBgra { get; init; }
    public required byte[]? MaskBgra { get; init; }
}

public sealed class LibDocument
{
    public required string FileName { get; init; }
    public required int Version { get; init; }
    public required IReadOnlyList<LibImage> Images { get; init; }
    public required IReadOnlyDictionary<int, LibFrameData> Frames { get; init; }
}

public static class LibraryReader
{
    public static LibDocument Read(string fileName)
    {
        byte[] bytes = File.ReadAllBytes(fileName);
        using MemoryStream stream = new(bytes, writable: false);
        using BinaryReader reader = new(stream);

        int version = reader.ReadInt32();
        if (version < 2 || version > 3)
            throw new InvalidDataException($"Unsupported .lib version {version} in {fileName}.");

        int count = reader.ReadInt32();
        if (count < 0 || count > 1_000_000)
            throw new InvalidDataException($"Invalid .lib image count {count} in {fileName}.");

        int frameSeek = version >= 3 ? reader.ReadInt32() : 0;
        int[] indexList = new int[count];
        for (int i = 0; i < count; i++) indexList[i] = reader.ReadInt32();

        Dictionary<int, LibFrameData> frames = new();
        if (frameSeek > 0 && frameSeek >= bytes.Length)
            throw new InvalidDataException($"Invalid frame table offset {frameSeek} in {fileName}.");

        if (frameSeek > 0)
        {
            stream.Position = frameSeek;
            int frameCount = reader.ReadInt32();
            if (frameCount < 0) throw new InvalidDataException($"Invalid frame count {frameCount} in {fileName}.");
            for (int i = 0; i < frameCount; i++)
            {
                int action = reader.ReadByte();
                frames[action] = new LibFrameData
                {
                    Start = reader.ReadInt32(),
                    Count = reader.ReadInt32(),
                    Skip = reader.ReadInt32(),
                    Interval = reader.ReadInt32(),
                    EffectStart = reader.ReadInt32(),
                    EffectCount = reader.ReadInt32(),
                    EffectSkip = reader.ReadInt32(),
                    EffectInterval = reader.ReadInt32(),
                    Reverse = reader.ReadBoolean(),
                    Blend = reader.ReadBoolean(),
                };
            }
        }

        List<LibImage> images = new(count);
        for (int i = 0; i < count; i++)
        {
            int offset = indexList[i];
            if (offset <= 0) continue;
            if (offset + 17 > bytes.Length)
                throw new InvalidDataException($"Image {i} in {fileName} has a truncated header.");

            stream.Position = offset;
            int width = reader.ReadInt16();
            int height = reader.ReadInt16();
            if (width <= 0 || height <= 0) continue;

            int offsetX = reader.ReadInt16();
            int offsetY = reader.ReadInt16();
            int shadowX = reader.ReadInt16();
            int shadowY = reader.ReadInt16();
            byte shadow = reader.ReadByte();
            int length = reader.ReadInt32();
            if (length <= 0 || length > bytes.Length - stream.Position)
                throw new InvalidDataException($"Image {i} in {fileName} has a truncated or empty payload.");

            bool hasMask = (shadow & 0x80) != 0;
            byte[] compressedMain = reader.ReadBytes(length);
            byte[] main = Decompress(compressedMain);
            main = NormalizePixels(main, width, height, fileName, i, "pixel");
            byte[]? mask = null;
            int maskWidth = 0, maskHeight = 0, maskX = 0, maskY = 0;

            if (hasMask)
            {
                if (stream.Position + 12 > bytes.Length)
                    throw new InvalidDataException($"Image {i} in {fileName} has a truncated mask header.");

                maskWidth = reader.ReadInt16();
                maskHeight = reader.ReadInt16();
                maskX = reader.ReadInt16();
                maskY = reader.ReadInt16();
                int maskLength = reader.ReadInt32();
                if (maskWidth <= 0 || maskHeight <= 0 || maskLength <= 0 || maskLength > bytes.Length - stream.Position)
                    throw new InvalidDataException($"Image {i} in {fileName} has an invalid mask payload.");

                mask = NormalizePixels(Decompress(reader.ReadBytes(maskLength)), maskWidth, maskHeight, fileName, i, "mask pixel");
            }

            images.Add(new LibImage
            {
                Index = i,
                Width = width,
                Height = height,
                OffsetX = offsetX,
                OffsetY = offsetY,
                ShadowX = shadowX,
                ShadowY = shadowY,
                Shadow = shadow,
                HasMask = hasMask,
                MaskWidth = maskWidth,
                MaskHeight = maskHeight,
                MaskOffsetX = maskX,
                MaskOffsetY = maskY,
                MainBgra = main,
                MaskBgra = mask,
            });
        }

        return new LibDocument
        {
            FileName = fileName,
            Version = version,
            Images = images,
            Frames = frames,
        };
    }

    public static string FramesJson(LibDocument document, IReadOnlyList<AtlasPlacement> pages, IReadOnlyList<AtlasPlacement> maskPages)
    {
        object value = new
        {
            version = document.Version,
            pages = pages.Select(static page => page.PageFile).ToArray(),
            maskPages = maskPages.Select(static page => page.PageFile).ToArray(),
            images = document.Images.Select(image =>
            {
                AtlasPlacement? page = pages.FirstOrDefault(value => value.ImageIndex == image.Index);
                AtlasPlacement? mask = maskPages.FirstOrDefault(value => value.ImageIndex == image.Index);
                return new
                {
                    index = image.Index,
                    page = page?.PageIndex ?? -1,
                    x = page?.X ?? 0,
                    y = page?.Y ?? 0,
                    width = image.Width,
                    height = image.Height,
                    offsetX = image.OffsetX,
                    offsetY = image.OffsetY,
                    shadowX = image.ShadowX,
                    shadowY = image.ShadowY,
                    shadow = image.Shadow,
                    mask = image.HasMask,
                    maskPage = mask?.PageIndex ?? -1,
                    maskX = mask?.X ?? 0,
                    maskY = mask?.Y ?? 0,
                    maskWidth = image.MaskWidth,
                    maskHeight = image.MaskHeight,
                    maskOffsetX = image.MaskOffsetX,
                    maskOffsetY = image.MaskOffsetY,
                };
            }).ToArray(),
            frames = document.Frames.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
        };
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static byte[] Decompress(byte[] compressed)
    {
        using GZipStream gzip = new(new MemoryStream(compressed), CompressionMode.Decompress);
        using MemoryStream output = new();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] NormalizePixels(byte[] pixels, int width, int height, string fileName, int imageIndex, string label)
    {
        int stride = width * 4;
        int paddedStride = ((width + 3) / 4) * 4 * 4;
        int paddedHeight = ((height + 3) / 4) * 4;
        long expected = (long)stride * height;

        if (pixels.Length == expected) return pixels;
        if (pixels.Length != (long)paddedStride * paddedHeight && pixels.Length != (long)paddedStride * height && pixels.Length != (long)stride * paddedHeight)
            throw new InvalidDataException($"Image {imageIndex} in {fileName} has an invalid {label} payload.");

        byte[] normalized = new byte[expected];
        for (int row = 0; row < height; row++)
            Buffer.BlockCopy(pixels, row * paddedStride, normalized, row * stride, stride);
        return normalized;
    }
}
