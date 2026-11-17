using System.Buffers.Binary;

namespace Crystal.Content;

public static class MapAssetParser
{
    private delegate bool CellPredicate(ReadOnlySpan<byte> bytes, int offset);

    private const int MaximumDimension = 4096;
    private const long MaximumCells = 16_777_216;

    public static MapAssetMetadata Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4)
        {
            return MapAssetMetadata.Invalid(0, "Map file is too small to contain a header.");
        }

        var format = DetectFormat(bytes);
        return format switch
        {
            0 => ParseV0(bytes),
            1 => ParseV1(bytes),
            2 => ParseV2(bytes),
            3 => ParseV3(bytes),
            4 => ParseV4(bytes),
            5 => ParseV5(bytes),
            6 => ParseV6(bytes),
            7 => ParseV7(bytes),
            100 => ParseV100(bytes),
            _ => MapAssetMetadata.Invalid(format, $"Unsupported map format {format}.")
        };
    }

    public static MapAssetPreview ParsePreview(ReadOnlySpan<byte> bytes, int maxSize = 256)
    {
        maxSize = Math.Clamp(maxSize, 8, 512);
        if (bytes.Length < 4)
        {
            return MapAssetPreview.Invalid(0, "Map file is too small to contain a header.");
        }

        var format = DetectFormat(bytes);
        return format switch
        {
            0 => PreviewV0(bytes, maxSize),
            1 => PreviewV1(bytes, maxSize),
            2 => PreviewV2(bytes, maxSize),
            3 => PreviewV3(bytes, maxSize),
            4 => PreviewV4(bytes, maxSize),
            5 => PreviewV5(bytes, maxSize),
            6 => PreviewV6(bytes, maxSize),
            7 => PreviewV7(bytes, maxSize),
            100 => PreviewV100(bytes, maxSize),
            _ => MapAssetPreview.Invalid(format, $"Unsupported map format {format}.")
        };
    }

    private static int DetectFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4 && bytes[2] == 0x43 && bytes[3] == 0x23) return 100;
        if (bytes[0] == 0) return 5;
        if (bytes.Length >= 15 &&
            bytes[0] == 0x0F && bytes[5] == 0x53 && bytes[14] == 0x33) return 6;
        if (bytes.Length >= 20 &&
            bytes[0] == 0x15 && bytes[4] == 0x32 && bytes[6] == 0x41 && bytes[19] == 0x31) return 4;
        if (bytes.Length >= 15 &&
            bytes[0] == 0x10 && bytes[2] == 0x61 && bytes[7] == 0x31 && bytes[14] == 0x31) return 1;
        if (bytes.Length >= 20 && (bytes[4] == 0x0F || (bytes[4] == 0x03 && bytes[18] == 0x0D && bytes[19] == 0x0A)))
        {
            var width = ReadInt16(bytes, 0);
            var height = ReadInt16(bytes, 2);
            var cellCount = (long)width * height;
            return bytes.Length > 52 + cellCount * 14 ? 3 : 2;
        }
        if (bytes.Length >= 12 &&
            bytes[0] == 0x0D && bytes[1] == 0x4C && bytes[7] == 0x20 && bytes[11] == 0x6D) return 7;
        return 0;
    }

    private static MapAssetMetadata ParseV0(ReadOnlySpan<byte> bytes)
    {
        const int format = 0;
        const int headerSize = 52;
        const int cellStride = 12;
        var width = ReadInt16(bytes, 0);
        var height = ReadInt16(bytes, 2);
        if (!TryValidateDimensions(format, width, height, bytes.Length, headerSize, cellStride, out var error))
        {
            return MapAssetMetadata.Invalid(format, error!);
        }

        return ScanCells(format, width, height, bytes, headerSize, cellStride,
            (span, offset) => IsBlockedV0(span, offset), (span, offset) => HasDoorAt(span, offset, 8));
    }

    private static MapAssetPreview PreviewV0(ReadOnlySpan<byte> bytes, int maxSize)
    {
        var width = ReadInt16(bytes, 0);
        var height = ReadInt16(bytes, 2);
        return PreviewCells(0, width, height, bytes, 52, 12, maxSize,
            (span, offset) => IsBlockedV0(span, offset),
            (span, offset) => HasDoorAt(span, offset, 8));
    }

    private static MapAssetMetadata ParseV1(ReadOnlySpan<byte> bytes)
    {
        const int format = 1;
        const int headerSize = 54;
        const int cellStride = 15;
        if (bytes.Length < 27)
        {
            return MapAssetMetadata.Invalid(format, "Map file is too small to contain a v1 header.");
        }

        var width = ReadInt16(bytes, 21) ^ ReadInt16(bytes, 23);
        var height = ReadInt16(bytes, 25) ^ ReadInt16(bytes, 23);
        if (!TryValidateDimensions(format, width, height, bytes.Length, headerSize, cellStride, out var error))
        {
            return MapAssetMetadata.Invalid(format, error!);
        }

        var xor = ReadInt16(bytes, 23);
        return ScanCells(format, width, height, bytes, headerSize, cellStride,
            (span, offset) =>
            {
                var highWall = (ReadInt32(span, offset) ^ 0xAA38AA38) & 0x20000000;
                var lowWall = (ReadInt16(span, offset + 6) ^ xor) & 0x8000;
                return highWall != 0 || lowWall != 0;
            }, (span, offset) => HasDoorAt(span, offset, 8));
    }

    private static MapAssetPreview PreviewV1(ReadOnlySpan<byte> bytes, int maxSize)
    {
        if (bytes.Length < 27) return MapAssetPreview.Invalid(1, "Map file is too small to contain a v1 header.");
        var xor = ReadInt16(bytes, 23);
        var width = ReadInt16(bytes, 21) ^ xor;
        var height = ReadInt16(bytes, 25) ^ xor;
        return PreviewCells(1, width, height, bytes, 54, 15, maxSize,
            (span, offset) =>
                ((ReadInt32(span, offset) ^ 0xAA38AA38) & 0x20000000) != 0 ||
                ((ReadInt16(span, offset + 6) ^ xor) & 0x8000) != 0,
            (span, offset) => HasDoorAt(span, offset, 8));
    }

    private static MapAssetMetadata ParseV2(ReadOnlySpan<byte> bytes)
    {
        const int format = 2;
        const int headerSize = 52;
        const int cellStride = 14;
        var width = ReadInt16(bytes, 0);
        var height = ReadInt16(bytes, 2);
        if (!TryValidateDimensions(format, width, height, bytes.Length, headerSize, cellStride, out var error))
        {
            return MapAssetMetadata.Invalid(format, error!);
        }

        return ScanCells(format, width, height, bytes, headerSize, cellStride,
            (span, offset) => IsBlockedV0(span, offset), (span, offset) => HasDoorAt(span, offset, 6));
    }

    private static MapAssetPreview PreviewV2(ReadOnlySpan<byte> bytes, int maxSize)
    {
        return PreviewCells(2, ReadInt16(bytes, 0), ReadInt16(bytes, 2), bytes, 52, 14, maxSize,
            (span, offset) => IsBlockedV0(span, offset),
            (span, offset) => HasDoorAt(span, offset, 6));
    }

    private static MapAssetMetadata ParseV3(ReadOnlySpan<byte> bytes)
    {
        const int format = 3;
        const int headerSize = 52;
        const int cellStride = 36;
        var width = ReadInt16(bytes, 0);
        var height = ReadInt16(bytes, 2);
        if (!TryValidateDimensions(format, width, height, bytes.Length, headerSize, cellStride, out var error))
        {
            return MapAssetMetadata.Invalid(format, error!);
        }

        return ScanCells(format, width, height, bytes, headerSize, cellStride,
            (span, offset) => IsBlockedV0(span, offset), (span, offset) => HasDoorAt(span, offset, 6));
    }

    private static MapAssetPreview PreviewV3(ReadOnlySpan<byte> bytes, int maxSize)
    {
        return PreviewCells(3, ReadInt16(bytes, 0), ReadInt16(bytes, 2), bytes, 52, 36, maxSize,
            (span, offset) => IsBlockedV0(span, offset),
            (span, offset) => HasDoorAt(span, offset, 6));
    }

    private static MapAssetMetadata ParseV4(ReadOnlySpan<byte> bytes)
    {
        const int format = 4;
        const int headerSize = 64;
        const int cellStride = 12;
        if (bytes.Length < 37)
        {
            return MapAssetMetadata.Invalid(format, "Map file is too small to contain a v4 header.");
        }

        var width = ReadInt16(bytes, 31) ^ ReadInt16(bytes, 33);
        var height = ReadInt16(bytes, 35) ^ ReadInt16(bytes, 33);
        if (!TryValidateDimensions(format, width, height, bytes.Length, headerSize, cellStride, out var error))
        {
            return MapAssetMetadata.Invalid(format, error!);
        }

        return ScanCells(format, width, height, bytes, headerSize, cellStride,
            (span, offset) => (ReadInt16(span, offset) & 0x8000) != 0 ||
                              (ReadInt16(span, offset + 2) & 0x8000) != 0,
            (span, offset) => HasDoorAt(span, offset, 6));
    }

    private static MapAssetPreview PreviewV4(ReadOnlySpan<byte> bytes, int maxSize)
    {
        if (bytes.Length < 37) return MapAssetPreview.Invalid(4, "Map file is too small to contain a v4 header.");
        var xor = ReadInt16(bytes, 33);
        return PreviewCells(4, ReadInt16(bytes, 31) ^ xor, ReadInt16(bytes, 35) ^ xor,
            bytes, 64, 12, maxSize,
            (span, offset) => (ReadInt16(span, offset) & 0x8000) != 0 ||
                              (ReadInt16(span, offset + 2) & 0x8000) != 0,
            (span, offset) => HasDoorAt(span, offset, 6));
    }

    private static MapAssetMetadata ParseV5(ReadOnlySpan<byte> bytes)
    {
        const int format = 5;
        const int cellStride = 14;
        if (bytes.Length < 26)
        {
            return MapAssetMetadata.Invalid(format, "Map file is too small to contain a v5 header.");
        }

        var width = ReadInt16(bytes, 22);
        var height = ReadInt16(bytes, 24);
        if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension)
        {
            return MapAssetMetadata.Invalid(format, $"Invalid map dimensions {width}x{height}.");
        }

        var collisionLength = 3L * (width / 2 + width % 2) * (height / 2);
        var headerLength = 28L + collisionLength;
        if (headerLength > bytes.Length)
        {
            return MapAssetMetadata.Invalid(format,
                $"Map format {format} header requires at least {headerLength} bytes but the file contains {bytes.Length}.");
        }

        var headerSize = (int)headerLength;
        if (!TryValidateDimensions(format, width, height, bytes.Length, headerSize, cellStride, out var error))
        {
            return MapAssetMetadata.Invalid(format, error!);
        }

        return ScanCells(format, width, height, bytes, headerSize, cellStride,
            (span, offset) => (span[offset] & 0x03) != 0x03,
            (_, _) => false);
    }

    private static MapAssetPreview PreviewV5(ReadOnlySpan<byte> bytes, int maxSize)
    {
        if (bytes.Length < 26) return MapAssetPreview.Invalid(5, "Map file is too small to contain a v5 header.");
        var width = ReadInt16(bytes, 22);
        var height = ReadInt16(bytes, 24);
        if (width <= 0 || height <= 0) return MapAssetPreview.Invalid(5, $"Invalid map dimensions {width}x{height}.");
        var collisionLength = 3L * (width / 2 + width % 2) * (height / 2);
        var headerLength = 28L + collisionLength;
        if (headerLength > bytes.Length || headerLength > int.MaxValue)
        {
            return MapAssetPreview.Invalid(5, "Map v5 header exceeds the file length.");
        }

        return PreviewCells(5, width, height, bytes, (int)headerLength, 14, maxSize,
            (span, offset) => (span[offset] & 0x03) != 0x03,
            (_, _) => false);
    }

    private static MapAssetMetadata ParseV6(ReadOnlySpan<byte> bytes)
    {
        const int format = 6;
        const int headerSize = 40;
        const int cellStride = 20;
        if (bytes.Length < 20)
        {
            return MapAssetMetadata.Invalid(format, "Map file is too small to contain a v6 header.");
        }

        var width = ReadInt16(bytes, 16);
        var height = ReadInt16(bytes, 18);
        if (!TryValidateDimensions(format, width, height, bytes.Length, headerSize, cellStride, out var error))
        {
            return MapAssetMetadata.Invalid(format, error!);
        }

        return ScanCells(format, width, height, bytes, headerSize, cellStride,
            (span, offset) => (span[offset] & 0x03) != 0x03,
            (_, _) => false);
    }

    private static MapAssetPreview PreviewV6(ReadOnlySpan<byte> bytes, int maxSize)
    {
        if (bytes.Length < 20) return MapAssetPreview.Invalid(6, "Map file is too small to contain a v6 header.");
        return PreviewCells(6, ReadInt16(bytes, 16), ReadInt16(bytes, 18), bytes, 40, 20, maxSize,
            (span, offset) => (span[offset] & 0x03) != 0x03,
            (_, _) => false);
    }

    private static MapAssetMetadata ParseV7(ReadOnlySpan<byte> bytes)
    {
        const int format = 7;
        const int headerSize = 54;
        const int cellStride = 15;
        if (bytes.Length < 27)
        {
            return MapAssetMetadata.Invalid(format, "Map file is too small to contain a v7 header.");
        }

        var width = ReadInt16(bytes, 21);
        var height = ReadInt16(bytes, 25);
        if (!TryValidateDimensions(format, width, height, bytes.Length, headerSize, cellStride, out var error))
        {
            return MapAssetMetadata.Invalid(format, error!);
        }

        return ScanCells(format, width, height, bytes, headerSize, cellStride,
            (span, offset) => (ReadInt16(span, offset) & 0x8000) != 0 ||
                              (ReadInt16(span, offset + 6) & 0x8000) != 0,
            (span, offset) => HasDoorAt(span, offset, 8));
    }

    private static MapAssetPreview PreviewV7(ReadOnlySpan<byte> bytes, int maxSize)
    {
        if (bytes.Length < 27) return MapAssetPreview.Invalid(7, "Map file is too small to contain a v7 header.");
        return PreviewCells(7, ReadInt16(bytes, 21), ReadInt16(bytes, 25), bytes, 54, 15, maxSize,
            (span, offset) => (ReadInt16(span, offset) & 0x8000) != 0 ||
                              (ReadInt16(span, offset + 6) & 0x8000) != 0,
            (span, offset) => HasDoorAt(span, offset, 8));
    }

    private static MapAssetMetadata ParseV100(ReadOnlySpan<byte> bytes)
    {
        const int format = 100;
        const int headerSize = 8;
        const int cellStride = 26;
        if (bytes.Length < headerSize || bytes[0] != 1 || bytes[1] != 0)
        {
            return MapAssetMetadata.Invalid(format, "Unsupported crystal map version; expected version 1.");
        }

        var width = ReadInt16(bytes, 4);
        var height = ReadInt16(bytes, 6);
        if (!TryValidateDimensions(format, width, height, bytes.Length, headerSize, cellStride, out var error))
        {
            return MapAssetMetadata.Invalid(format, error!);
        }

        return ScanCells(format, width, height, bytes, headerSize, cellStride,
            (span, offset) => (ReadInt32(span, offset + 2) & 0x20000000) != 0 ||
                              (ReadInt16(span, offset + 12) & 0x8000) != 0,
            (span, offset) => HasDoorAt(span, offset, 14));
    }

    private static MapAssetPreview PreviewV100(ReadOnlySpan<byte> bytes, int maxSize)
    {
        if (bytes.Length < 8 || bytes[0] != 1 || bytes[1] != 0)
        {
            return MapAssetPreview.Invalid(100, "Unsupported crystal map version; expected version 1.");
        }

        return PreviewCells(100, ReadInt16(bytes, 4), ReadInt16(bytes, 6), bytes, 8, 26, maxSize,
            (span, offset) => (ReadInt32(span, offset + 2) & 0x20000000) != 0 ||
                              (ReadInt16(span, offset + 12) & 0x8000) != 0,
            (span, offset) => HasDoorAt(span, offset, 14));
    }

    private static MapAssetPreview PreviewCells(
        int format,
        int width,
        int height,
        ReadOnlySpan<byte> bytes,
        int offset,
        int cellStride,
        int maxSize,
        CellPredicate isBlocked,
        CellPredicate hasDoor)
    {
        if (!TryValidateDimensions(format, width, height, bytes.Length, offset, cellStride, out var error) ||
            error is not null)
        {
            return MapAssetPreview.Invalid(format, error ?? "Invalid map dimensions.");
        }

        var sampleWidth = Math.Min(width, maxSize);
        var sampleHeight = Math.Min(height, maxSize);
        var blockedCounts = new long[sampleWidth * sampleHeight];
        var doorCounts = new long[sampleWidth * sampleHeight];
        var totals = new long[sampleWidth * sampleHeight];
        var walkable = 0L;
        var blocked = 0L;
        var doors = 0L;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sampleX = Math.Min(sampleWidth - 1, x * sampleWidth / width);
                var sampleY = Math.Min(sampleHeight - 1, y * sampleHeight / height);
                var sampleIndex = sampleY * sampleWidth + sampleX;
                totals[sampleIndex]++;
                if (isBlocked(bytes, offset))
                {
                    blocked++;
                    blockedCounts[sampleIndex]++;
                }
                else
                {
                    walkable++;
                }

                if (hasDoor(bytes, offset))
                {
                    doors++;
                    doorCounts[sampleIndex]++;
                }

                offset += cellStride;
            }
        }

        var rows = new string[sampleHeight];
        for (var y = 0; y < sampleHeight; y++)
        {
            var characters = new char[sampleWidth];
            for (var x = 0; x < sampleWidth; x++)
            {
                var index = y * sampleWidth + x;
                characters[x] = blockedCounts[index] * 2 >= totals[index]
                    ? '#'
                    : doorCounts[index] > 0 ? 'D' : '.';
            }

            rows[y] = new string(characters);
        }

        return new MapAssetPreview(
            true,
            format,
            MapAssetMetadata.FormatNameFor(format),
            width,
            height,
            sampleWidth,
            sampleHeight,
            walkable,
            blocked,
            doors,
            rows,
            null);
    }

    private static MapAssetMetadata ScanCells(
        int format,
        int width,
        int height,
        ReadOnlySpan<byte> bytes,
        int offset,
        int cellStride,
        CellPredicate isBlocked,
        CellPredicate hasDoor)
    {
        var cellCount = (long)width * height;
        var walkable = 0L;
        var blocked = 0L;
        var doors = 0L;
        for (var cell = 0L; cell < cellCount; cell++)
        {
            if (isBlocked(bytes, offset))
            {
                blocked++;
            }
            else
            {
                walkable++;
            }

            if (hasDoor(bytes, offset)) doors++;
            offset += cellStride;
        }

        return new MapAssetMetadata(
            true,
            format,
            MapAssetMetadata.FormatNameFor(format),
            width,
            height,
            cellCount,
            walkable,
            blocked,
            doors,
            null);
    }

    private static bool TryValidateDimensions(
        int format,
        int width,
        int height,
        int fileLength,
        int headerSize,
        int cellStride,
        out string? error)
    {
        error = null;
        if (width <= 0 || height <= 0)
        {
            error = $"Invalid map dimensions {width}x{height}.";
            return false;
        }

        if (width > MaximumDimension || height > MaximumDimension)
        {
            error = $"Map dimensions {width}x{height} exceed the {MaximumDimension} cell safety limit.";
            return false;
        }

        var cellCount = (long)width * height;
        if (cellCount > MaximumCells)
        {
            error = $"Map contains {cellCount} cells, exceeding the {MaximumCells} safety limit.";
            return false;
        }

        var required = checked((long)headerSize + cellCount * cellStride);
        if (required > fileLength)
        {
            error = $"Map format {format} requires at least {required} bytes but the file contains {fileLength}.";
            return false;
        }

        return true;
    }

    private static bool IsBlockedV0(ReadOnlySpan<byte> bytes, int offset)
    {
        return (ReadInt16(bytes, offset) & 0x8000) != 0 ||
               (ReadInt16(bytes, offset + 2) & 0x8000) != 0 ||
               (ReadInt16(bytes, offset + 4) & 0x8000) != 0;
    }

    private static bool HasDoorAt(ReadOnlySpan<byte> bytes, int offset, int relativeOffset)
    {
        return bytes[offset + relativeOffset] > 0;
    }

    private static short ReadInt16(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset, 2));
    }

    private static int ReadInt32(ReadOnlySpan<byte> bytes, int offset)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4));
    }
}
