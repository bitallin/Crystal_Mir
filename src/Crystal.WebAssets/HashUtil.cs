using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Crystal.WebAssets;

public static class HashUtil
{
    public static string Sha256(ReadOnlySpan<byte> bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

    public static string Sha256File(string path) => Sha256(File.ReadAllBytes(path));

    public static string Sha256Files(IEnumerable<(string Name, byte[] Bytes)> files)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach ((string name, byte[] bytes) in files.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name.Replace('\\', '/'));
            BinaryPrimitives.WriteInt32LittleEndian(length, nameBytes.Length);
            hash.AppendData(length);
            hash.AppendData(nameBytes);
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }

        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
