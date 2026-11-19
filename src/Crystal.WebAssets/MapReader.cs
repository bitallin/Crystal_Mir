namespace Crystal.WebAssets;

public sealed class MapCell
{
    public short BackIndex;
    public int BackImage;
    public short MiddleIndex;
    public int MiddleImage;
    public short FrontIndex;
    public int FrontImage;
    public byte DoorIndex;
    public byte DoorOffset;
    public byte FrontAnimationFrame;
    public byte FrontAnimationTick;
    public byte MiddleAnimationFrame;
    public byte MiddleAnimationTick;
    public short TileAnimationImage;
    public short TileAnimationOffset;
    public byte TileAnimationFrames;
    public byte Light;
    public bool FishingCell;

    public bool Walkable => (BackImage & 0x20000000) == 0 && (FrontImage & 0x8000) == 0;
}

public sealed class MapDocument
{
    public required string FileName { get; init; }
    public required string SourceHash { get; init; }
    public required int Format { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required IReadOnlyList<MapCell> Cells { get; init; }
    public required IReadOnlyList<int> LibraryIds { get; init; }
}

public static class MapReader
{
    public const int CellStride = 32;
    public const string CellOrder = "x-major";

    public static MapDocument Read(string fileName)
    {
        byte[] bytes = File.ReadAllBytes(fileName);
        if (bytes.Length < 4) throw new InvalidDataException($"Map {fileName} is too small.");

        int format = DetectFormat(bytes);
        ParsedMap map;
        try
        {
            map = format switch
            {
                0 => ReadV0(bytes),
                1 => ReadV1(bytes),
                2 => ReadV2OrV3(bytes, 2),
                3 => ReadV2OrV3(bytes, 3),
                4 => ReadV4(bytes),
                5 => ReadV5(bytes),
                6 => ReadV6(bytes),
                7 => ReadV7(bytes),
                100 => ReadV100(bytes),
                _ => throw new InvalidDataException($"Unsupported map format {format} in {fileName}."),
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or IndexOutOfRangeException or EndOfStreamException)
        {
            throw new InvalidDataException($"Map {fileName} is malformed (format {format}): {exception.Message}", exception);
        }

        int[] libraryIds = map.Cells
            .SelectMany(static cell => new[] { (int)cell.BackIndex, (int)cell.MiddleIndex, (int)cell.FrontIndex })
            .Where(static value => value >= 0)
            .Distinct()
            .OrderBy(static value => value)
            .ToArray();

        return new MapDocument
        {
            FileName = fileName,
            SourceHash = HashUtil.Sha256(bytes),
            Format = format,
            Width = map.Width,
            Height = map.Height,
            Cells = map.Cells,
            LibraryIds = libraryIds,
        };
    }

    public static byte[] WriteCells(MapDocument document)
    {
        using MemoryStream stream = new(document.Cells.Count * CellStride);
        using BinaryWriter writer = new(stream);
        foreach (MapCell cell in document.Cells)
        {
            writer.Write(cell.BackIndex);
            writer.Write(cell.BackImage);
            writer.Write(cell.MiddleIndex);
            writer.Write(cell.MiddleImage);
            writer.Write(cell.FrontIndex);
            writer.Write(cell.FrontImage);
            writer.Write(cell.DoorIndex);
            writer.Write(cell.DoorOffset);
            writer.Write(cell.FrontAnimationFrame);
            writer.Write(cell.FrontAnimationTick);
            writer.Write(cell.MiddleAnimationFrame);
            writer.Write(cell.MiddleAnimationTick);
            writer.Write(cell.TileAnimationImage);
            writer.Write(cell.TileAnimationOffset);
            writer.Write(cell.TileAnimationFrames);
            writer.Write(cell.Light);
            writer.Write(cell.Walkable ? (byte)1 : (byte)0);
            writer.Write(cell.FishingCell ? (byte)1 : (byte)0);
        }
        return stream.ToArray();
    }

    private static int DetectFormat(byte[] bytes)
    {
        if (bytes.Length > 3 && bytes[2] == 0x43 && bytes[3] == 0x23) return 100;
        if (bytes[0] == 0) return 5;
        if (bytes.Length > 14 && bytes[0] == 0x0F && bytes[5] == 0x53 && bytes[14] == 0x33) return 6;
        if (bytes.Length > 19 && bytes[0] == 0x15 && bytes[4] == 0x32 && bytes[6] == 0x41 && bytes[19] == 0x31) return 4;
        if (bytes.Length > 14 && bytes[0] == 0x10 && bytes[2] == 0x61 && bytes[7] == 0x31 && bytes[14] == 0x31) return 1;
        if (bytes.Length > 19 && (bytes[4] == 0x0F || (bytes[4] == 0x03 && bytes[18] == 0x0D && bytes[19] == 0x0A)))
        {
            int width = bytes[0] + (bytes[1] << 8);
            int height = bytes[2] + (bytes[3] << 8);
            return bytes.Length > 52 + (long)width * height * 14 ? 3 : 2;
        }
        if (bytes.Length > 11 && bytes[0] == 0x0D && bytes[1] == 0x4C && bytes[7] == 0x20 && bytes[11] == 0x6D) return 7;
        return 0;
    }

    private static ParsedMap ReadV0(byte[] bytes)
    {
        ByteView view = new(bytes);
        int width = view.Int16(0);
        int height = view.Int16(2);
        MapCell[] cells = NewCells(width, height);
        int offset = 52;
        Fill(cells, width, height, (cell, x, y) =>
        {
            cell.BackIndex = 0;
            cell.MiddleIndex = 1;
            cell.BackImage = view.UInt16(offset);
            cell.MiddleImage = view.UInt16(offset + 2);
            cell.FrontImage = view.UInt16(offset + 4);
            cell.DoorIndex = (byte)(bytes[offset + 6] & 0x7F);
            cell.DoorOffset = bytes[offset + 7];
            cell.FrontAnimationFrame = bytes[offset + 8];
            cell.FrontAnimationTick = bytes[offset + 9];
            cell.FrontIndex = (short)(bytes[offset + 10] + 2);
            cell.Light = bytes[offset + 11];
            NormalizeBackBits(cell);
            cell.FishingCell = cell.Light is >= 100 and <= 119;
            offset += 12;
        });
        return new ParsedMap(width, height, cells);
    }

    private static ParsedMap ReadV1(byte[] bytes)
    {
        ByteView view = new(bytes);
        int width = view.Int16(21) ^ view.Int16(23);
        int height = view.Int16(25) ^ view.Int16(23);
        MapCell[] cells = NewCells(width, height);
        long expectedLength = 54L + (long)width * height * 15;
        if (expectedLength > bytes.Length)
            throw new InvalidDataException($"Map V1 is truncated: expected at least {expectedLength} bytes for {width}x{height}, got {bytes.Length}.");
        int offset = 54;
        int xor = view.Int16(23);
        Fill(cells, width, height, (cell, x, y) =>
        {
            cell.BackIndex = 0;
            cell.MiddleIndex = 1;
            cell.BackImage = view.Int32(offset) ^ unchecked((int)0xAA38AA38);
            cell.MiddleImage = (short)(view.Int16(offset + 4) ^ xor);
            cell.FrontImage = (short)(view.Int16(offset + 6) ^ xor);
            cell.DoorIndex = (byte)(bytes[offset + 8] & 0x7F);
            cell.DoorOffset = bytes[offset + 9];
            cell.FrontAnimationFrame = bytes[offset + 10];
            cell.FrontAnimationTick = bytes[offset + 11];
            cell.FrontIndex = (short)(bytes[offset + 12] + 2);
            if (cell.FrontIndex == 102) cell.FrontIndex = 90;
            if (cell.FrontIndex >= 255) cell.FrontIndex = -1;
            cell.Light = bytes[offset + 13];
            NormalizeBackBits(cell);
            cell.FishingCell = cell.Light is >= 100 and <= 119;
            offset += 15;
        });
        return new ParsedMap(width, height, cells);
    }

    private static ParsedMap ReadV2OrV3(byte[] bytes, int version)
    {
        ByteView view = new(bytes);
        int width = view.Int16(0);
        int height = view.Int16(2);
        MapCell[] cells = NewCells(width, height);
        int offset = 52;
        Fill(cells, width, height, (cell, x, y) =>
        {
            cell.BackImage = view.UInt16(offset);
            cell.MiddleImage = view.UInt16(offset + 2);
            cell.FrontImage = view.UInt16(offset + 4);
            cell.DoorIndex = (byte)(bytes[offset + 6] & 0x7F);
            cell.DoorOffset = bytes[offset + 7];
            cell.FrontAnimationFrame = bytes[offset + 8];
            cell.FrontAnimationTick = bytes[offset + 9];
            cell.FrontIndex = (short)(bytes[offset + 10] + 120);
            cell.Light = bytes[offset + 11];
            cell.BackIndex = (short)(bytes[offset + 12] + 100);
            cell.MiddleIndex = (short)(bytes[offset + 13] + 110);
            if (version == 3)
            {
                cell.TileAnimationImage = view.Int16(offset + 14);
                cell.TileAnimationFrames = bytes[offset + 21];
                cell.TileAnimationOffset = view.Int16(offset + 22);
                offset += 36;
            }
            else
            {
                offset += 14;
            }
            NormalizeBackBits(cell);
            cell.FishingCell = cell.Light is >= 100 and <= 119;
        });
        return new ParsedMap(width, height, cells);
    }

    private static ParsedMap ReadV4(byte[] bytes)
    {
        ByteView view = new(bytes);
        int xor = view.Int16(33);
        int width = view.Int16(31) ^ xor;
        int height = view.Int16(35) ^ xor;
        MapCell[] cells = NewCells(width, height);
        int offset = 64;
        Fill(cells, width, height, (cell, x, y) =>
        {
            cell.BackIndex = 0;
            cell.MiddleIndex = 1;
            cell.BackImage = (short)(view.Int16(offset) ^ xor);
            cell.MiddleImage = (short)(view.Int16(offset + 2) ^ xor);
            cell.FrontImage = (short)(view.Int16(offset + 4) ^ xor);
            cell.DoorIndex = (byte)(bytes[offset + 6] & 0x7F);
            cell.DoorOffset = bytes[offset + 7];
            cell.FrontAnimationFrame = bytes[offset + 8];
            cell.FrontAnimationTick = bytes[offset + 9];
            cell.FrontIndex = (short)(bytes[offset + 10] + 2);
            cell.Light = bytes[offset + 11];
            NormalizeBackBits(cell);
            cell.FishingCell = cell.Light is >= 100 and <= 119;
            offset += 12;
        });
        return new ParsedMap(width, height, cells);
    }

    private static ParsedMap ReadV5(byte[] bytes)
    {
        ByteView view = new(bytes);
        int width = view.Int16(22);
        int height = view.Int16(24);
        MapCell[] cells = NewCells(width, height);
        int offset = 28;

        for (int x = 0; x < width / 2; x++)
            for (int y = 0; y < height / 2; y++)
            {
                for (int i = 0; i < 4; i++)
                {
                    int cx = x * 2 + i % 2;
                    int cy = y * 2 + i / 2;
                    MapCell cell = cells[cx * height + cy];
                    cell.BackIndex = (short)(bytes[offset] != 255 ? bytes[offset] + 200 : -1);
                    cell.BackImage = view.UInt16(offset + 1) + 1;
                }
                offset += 3;
            }

        offset = 28 + 3 * (width / 2 + width % 2) * (height / 2);
        Fill(cells, width, height, (cell, x, y) =>
        {
            byte flag = bytes[offset++];
            cell.MiddleAnimationFrame = bytes[offset++];
            cell.FrontAnimationFrame = bytes[offset] == 255 ? (byte)0 : bytes[offset];
            cell.FrontAnimationFrame &= 0x8F;
            offset++;
            cell.FrontIndex = (short)(bytes[offset] != 255 ? bytes[offset] + 200 : -1);
            offset++;
            cell.MiddleIndex = (short)(bytes[offset] != 255 ? bytes[offset] + 200 : -1);
            offset++;
            cell.MiddleImage = view.UInt16(offset) + 1;
            offset += 2;
            cell.FrontImage = view.UInt16(offset) + 1;
            if (cell.FrontImage == 1 && cell.FrontIndex == 200) cell.FrontIndex = -1;
            offset += 5;
            cell.Light = (byte)(bytes[offset] & 0x0F);
            offset += 2;
            if ((flag & 0x01) != 1) cell.BackImage |= 0x20000000;
            if ((flag & 0x02) != 2) cell.FrontImage |= 0x8000;
            cell.FishingCell = cell.Light is >= 100 and <= 119;
            if (!cell.FishingCell) cell.Light *= 2;
        });
        return new ParsedMap(width, height, cells);
    }

    private static ParsedMap ReadV6(byte[] bytes)
    {
        ByteView view = new(bytes);
        int width = view.Int16(16);
        int height = view.Int16(18);
        MapCell[] cells = NewCells(width, height);
        int offset = 40;
        Fill(cells, width, height, (cell, x, y) =>
        {
            byte flag = bytes[offset++];
            cell.BackIndex = (short)(bytes[offset] != 255 ? bytes[offset] + 300 : -1);
            offset++;
            cell.MiddleIndex = (short)(bytes[offset] != 255 ? bytes[offset] + 300 : -1);
            offset++;
            cell.FrontIndex = (short)(bytes[offset] != 255 ? bytes[offset] + 300 : -1);
            offset++;
            cell.BackImage = view.Int16(offset) + 1;
            offset += 2;
            cell.MiddleImage = view.Int16(offset) + 1;
            offset += 2;
            cell.FrontImage = view.Int16(offset) + 1;
            offset += 2;
            cell.MiddleAnimationFrame = bytes[offset++];
            cell.FrontAnimationFrame = bytes[offset] == 255 ? (byte)0 : (byte)(bytes[offset] & 0x0F);
            offset++;
            cell.MiddleAnimationTick = 1;
            cell.FrontAnimationTick = 1;
            cell.Light = (byte)(bytes[offset] & 0x0F);
            cell.Light *= 4;
            offset += 9;
            if ((flag & 0x01) != 1) cell.BackImage |= 0x20000000;
            if ((flag & 0x02) != 2) cell.FrontImage |= 0x8000;
        });
        return new ParsedMap(width, height, cells);
    }

    private static ParsedMap ReadV7(byte[] bytes)
    {
        ByteView view = new(bytes);
        int width = view.Int16(21);
        int height = view.Int16(25);
        MapCell[] cells = NewCells(width, height);
        int offset = 54;
        Fill(cells, width, height, (cell, x, y) =>
        {
            cell.BackIndex = 0;
            cell.BackImage = view.Int32(offset);
            cell.MiddleIndex = 1;
            cell.MiddleImage = view.Int16(offset + 4);
            cell.FrontImage = view.Int16(offset + 6);
            cell.DoorIndex = (byte)(bytes[offset + 8] & 0x7F);
            cell.DoorOffset = bytes[offset + 9];
            cell.FrontAnimationFrame = bytes[offset + 10];
            cell.FrontAnimationTick = bytes[offset + 11];
            cell.FrontIndex = (short)(bytes[offset + 12] + 2);
            cell.Light = bytes[offset + 13];
            NormalizeBackBits(cell);
            cell.FishingCell = cell.Light is >= 100 and <= 119;
            offset += 15;
        });
        return new ParsedMap(width, height, cells);
    }

    private static ParsedMap ReadV100(byte[] bytes)
    {
        ByteView view = new(bytes);
        if (bytes[0] != 1 || bytes[1] != 0) throw new InvalidDataException("Only custom map version 1 is supported.");
        int width = view.Int16(4);
        int height = view.Int16(6);
        MapCell[] cells = NewCells(width, height);
        int offset = 8;
        Fill(cells, width, height, (cell, x, y) =>
        {
            cell.BackIndex = view.Int16(offset);
            offset += 2;
            cell.BackImage = view.Int32(offset);
            offset += 4;
            cell.MiddleIndex = view.Int16(offset);
            offset += 2;
            cell.MiddleImage = view.Int16(offset);
            offset += 2;
            cell.FrontIndex = view.Int16(offset);
            offset += 2;
            cell.FrontImage = view.Int16(offset);
            offset += 2;
            cell.DoorIndex = (byte)(bytes[offset++] & 0x7F);
            cell.DoorOffset = bytes[offset++];
            cell.FrontAnimationFrame = bytes[offset++];
            cell.FrontAnimationTick = bytes[offset++];
            cell.MiddleAnimationFrame = bytes[offset++];
            cell.MiddleAnimationTick = bytes[offset++];
            cell.TileAnimationImage = view.Int16(offset);
            offset += 2;
            cell.TileAnimationOffset = view.Int16(offset);
            offset += 2;
            cell.TileAnimationFrames = bytes[offset++];
            cell.Light = bytes[offset++];
            cell.FishingCell = cell.Light is >= 100 and <= 119;
        });
        return new ParsedMap(width, height, cells);
    }

    private static MapCell[] NewCells(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            throw new InvalidDataException($"Invalid map dimensions {width}x{height}.");
        long count = (long)width * height;
        if (count > 16_000_000) throw new InvalidDataException($"Map is too large: {width}x{height}.");
        MapCell[] cells = new MapCell[(int)count];
        for (int i = 0; i < cells.Length; i++) cells[i] = new MapCell();
        return cells;
    }

    private static void Fill(MapCell[] cells, int width, int height, Action<MapCell, int, int> action)
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                action(cells[x * height + y], x, y);
    }

    private static void NormalizeBackBits(MapCell cell)
    {
        if ((cell.BackImage & 0x8000) != 0)
            cell.BackImage = (cell.BackImage & 0x7FFF) | 0x20000000;
    }

    private sealed record ParsedMap(int Width, int Height, MapCell[] Cells);

    private readonly struct ByteView(byte[] bytes)
    {
        public short Int16(int offset) => BitConverter.ToInt16(bytes, offset);
        public ushort UInt16(int offset) => BitConverter.ToUInt16(bytes, offset);
        public int Int32(int offset) => BitConverter.ToInt32(bytes, offset);
    }
}
