using System.Security.Cryptography;
using System.Text;
using Server;

string root = Path.Combine(Path.GetTempPath(), "crystal-version-gate-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    string legacyClient = Path.Combine(root, "Mir2.Exe");
    byte[] legacyBytes = Encoding.UTF8.GetBytes("legacy-client");
    File.WriteAllBytes(legacyClient, legacyBytes);

    Settings.VersionPath = legacyClient;
    Settings.SetVersionHashValues(" sha256:web-build-a ;sha256:web-build-b,");

    Require(Settings.VersionHashes.Count == 3, "Expected one MD5 and two literal version hashes.");
    Require(Settings.VersionHashes[0].SequenceEqual(MD5.HashData(legacyBytes)), "Legacy MD5 version hash changed.");
    Require(Settings.VersionHashes[1].SequenceEqual(Encoding.UTF8.GetBytes("sha256:web-build-a")), "First Web version hash mismatch.");
    Require(Settings.VersionHashes[2].SequenceEqual(Encoding.UTF8.GetBytes("sha256:web-build-b")), "Second Web version hash mismatch.");

    Settings.SetVersionHashValues(string.Empty);
    Require(Settings.VersionHashes.Count == 1, "Clearing literal hashes must preserve legacy file hashes.");

    Console.WriteLine("Zone version gate checks passed.");
    return 0;
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
