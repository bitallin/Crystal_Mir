using System.Security.Cryptography;
using Server;
using Server.MirEnvir;
using Shared;

namespace Crystal.LegacyImporter;

public sealed class LegacyDatabaseSession : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly string _originalDirectory;
    private bool _disposed;

    private LegacyDatabaseSession(
        string sourcePath,
        string temporaryDirectory,
        string originalDirectory,
        int headerVersion,
        int headerCustomVersion,
        string sha256)
    {
        SourcePath = sourcePath;
        _temporaryDirectory = temporaryDirectory;
        _originalDirectory = originalDirectory;
        HeaderVersion = headerVersion;
        HeaderCustomVersion = headerCustomVersion;
        SourceHash = sha256;
    }

    public string SourcePath { get; }

    public string SourceHash { get; }

    public int HeaderVersion { get; }

    public int HeaderCustomVersion { get; }

    public Envir Envir { get; private set; } = null!;

    public static LegacyDatabaseSession Open(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Legacy database not found.", fullPath);
        }

        int headerVersion;
        int headerCustomVersion;
        using (var stream = File.OpenRead(fullPath))
        using (var reader = new BinaryReader(stream))
        {
            headerVersion = reader.ReadInt32();
            headerCustomVersion = reader.ReadInt32();
        }

        string sha256;
        using (var stream = File.OpenRead(fullPath))
        {
            sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"crystal-legacy-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var originalDirectory = Environment.CurrentDirectory;
        try
        {
            File.Copy(fullPath, Path.Combine(temporaryDirectory, "Server.MirDB"), overwrite: true);
            Environment.CurrentDirectory = temporaryDirectory;
            Packet.IsServer = true;
            Settings.RandomItemStatsList.Clear();
            Settings.Load();

            var envir = Envir.Edit;
            if (!envir.LoadDB())
            {
                throw new InvalidDataException(
                    $"LoadDB returned false for {fullPath} (header={headerVersion}, supported={Envir.MinVersion}..{Envir.Version}).");
            }

            return new LegacyDatabaseSession(
                fullPath,
                temporaryDirectory,
                originalDirectory,
                headerVersion,
                headerCustomVersion,
                sha256)
            {
                Envir = envir
            };
        }
        catch
        {
            Environment.CurrentDirectory = originalDirectory;
            try { Directory.Delete(temporaryDirectory, recursive: true); } catch { }
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Environment.CurrentDirectory = _originalDirectory;
        try { Directory.Delete(_temporaryDirectory, recursive: true); } catch { }
    }
}
