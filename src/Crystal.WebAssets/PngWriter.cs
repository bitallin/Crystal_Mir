using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Crystal.WebAssets;

public static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void WriteRgba(int width, int height, ReadOnlySpan<byte> rgba, Stream output)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("RGBA buffer is smaller than the requested image.", nameof(rgba));

        output.Write(Signature);

        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        WriteChunk(output, "IHDR", ihdr);

        using MemoryStream raw = new();
        using (MemoryStream compressed = new())
        {
            for (int y = 0; y < height; y++)
            {
                raw.WriteByte(0); // filter: none
                raw.Write(rgba.Slice(y * width * 4, width * 4));
            }

            raw.Position = 0;
            using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                raw.CopyTo(zlib);
            }

            WriteChunk(output, "IDAT", compressed.ToArray());
        }

        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc(crc, typeBytes);
        crc = UpdateCrc(crc, data);
        byte[] crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xFFFFFFFF);
        output.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
