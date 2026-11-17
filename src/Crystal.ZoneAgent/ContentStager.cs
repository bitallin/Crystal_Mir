using System.IO.Compression;
using Crystal.Content;

namespace Crystal.ZoneAgent;

internal sealed record StagedRelease(
    long ReleaseId,
    string Version,
    string ContentHash,
    int SchemaVersion,
    string Directory);

internal sealed class ContentStager
{
    private readonly AgentOptions _options;
    private readonly ControlApiClient _client;
    private readonly string _publicKeyPem;

    public ContentStager(AgentOptions options, ControlApiClient client)
    {
        _options = options;
        _client = client;
        _publicKeyPem = File.ReadAllText(options.ContentPublicKeyPath);
    }

    public async Task<StagedRelease> StageAsync(
        RemoteRelease release,
        CancellationToken cancellationToken)
    {
        if (release.Status is not ("published" or "active"))
        {
            throw new InvalidDataException(
                $"Release {release.Id} is '{release.Status}', expected published or active.");
        }

        if (release.ContentHash.Length != 64 || !release.ContentHash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException(
                $"Release {release.Id} has an invalid content hash.");
        }

        string finalDirectory = Path.Combine(
            _options.ReleasesDirectory,
            $"{release.Id}-{release.ContentHash}");
        if (Directory.Exists(finalDirectory))
        {
            return await VerifyDirectoryAsync(
                finalDirectory,
                release.Id,
                release.Version,
                release.ContentHash,
                release.SchemaVersion,
                cancellationToken);
        }

        string temporaryDirectory = Path.Combine(
            _options.StagingDirectory,
            $"{release.Id}-{Guid.NewGuid():N}");
        string archivePath = Path.Combine(
            _options.StagingDirectory,
            $"{release.Id}-{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            await _client.DownloadArtifactAsync(release.Id, archivePath, cancellationToken);
            ExtractArchive(archivePath, temporaryDirectory);
            var staged = await VerifyDirectoryAsync(
                temporaryDirectory,
                release.Id,
                release.Version,
                release.ContentHash,
                release.SchemaVersion,
                cancellationToken);

            if (Directory.Exists(finalDirectory))
            {
                return await VerifyDirectoryAsync(
                    finalDirectory,
                    release.Id,
                    release.Version,
                    release.ContentHash,
                    release.SchemaVersion,
                    cancellationToken);
            }

            try
            {
                Directory.Move(temporaryDirectory, finalDirectory);
            }
            catch (IOException) when (Directory.Exists(finalDirectory))
            {
                return await VerifyDirectoryAsync(
                    finalDirectory,
                    release.Id,
                    release.Version,
                    release.ContentHash,
                    release.SchemaVersion,
                    cancellationToken);
            }

            return staged with { Directory = finalDirectory };
        }
        finally
        {
            TryDeleteDirectory(temporaryDirectory);
            try
            {
                if (File.Exists(archivePath)) File.Delete(archivePath);
            }
            catch
            {
            }
        }
    }

    public Task<StagedRelease> VerifyAsync(
        ActiveReleaseState state,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(state.ReleaseDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Active release directory is missing: {state.ReleaseDirectory}");
        }

        return VerifyDirectoryAsync(
            state.ReleaseDirectory,
            state.ReleaseId,
            state.Version,
            state.ContentHash,
            0,
            cancellationToken,
            requireSchemaVersion: false);
    }

    private async Task<StagedRelease> VerifyDirectoryAsync(
        string directory,
        long releaseId,
        string expectedVersion,
        string expectedContentHash,
        int expectedSchemaVersion,
        CancellationToken cancellationToken,
        bool requireSchemaVersion = true)
    {
        var snapshot = await Task.Run(() => ContentSnapshotReader.Read(directory), cancellationToken);
        if (!string.Equals(snapshot.Manifest.ContentHash, expectedContentHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Release content hash mismatch: expected {expectedContentHash}, actual {snapshot.Manifest.ContentHash}.");
        }

        if (!string.Equals(snapshot.Manifest.ReleaseVersion, expectedVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Release version mismatch: expected {expectedVersion}, actual {snapshot.Manifest.ReleaseVersion}.");
        }

        if (requireSchemaVersion && snapshot.Manifest.SchemaVersion != expectedSchemaVersion)
        {
            throw new InvalidDataException(
                $"Release schema version mismatch: expected {expectedSchemaVersion}, actual {snapshot.Manifest.SchemaVersion}.");
        }

        var issues = ContentSnapshotValidator.Validate(snapshot);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(
                "Release validation failed: " + string.Join("; ", issues.Select(issue => issue.Message)));
        }

        var signature = ContentSnapshotSigner.Verify(
            snapshot.Manifest,
            _publicKeyPem,
            _options.ContentKeyId);
        if (!signature.IsValid)
        {
            throw new InvalidDataException($"Release signature verification failed: {signature.Error}");
        }

        return new StagedRelease(
            releaseId,
            snapshot.Manifest.ReleaseVersion,
            snapshot.Manifest.ContentHash,
            snapshot.Manifest.SchemaVersion,
            Path.GetFullPath(directory));
    }

    private static void ExtractArchive(string archivePath, string destinationDirectory)
    {
        string root = Path.GetFullPath(destinationDirectory);
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        int count = 0;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (++count > 1024)
            {
                throw new InvalidDataException("Release archive contains too many files.");
            }

            string relative = entry.FullName.Replace('\\', '/');
            string[] segments = relative.Split('/');
            if (Path.IsPathRooted(relative) ||
                segments.Any(segment => segment is "" or "." or "..") ||
                (relative != "manifest.json" &&
                 relative != "summary.json" &&
                 !relative.StartsWith("entities/", StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"Release archive contains an unsafe path: {entry.FullName}");
            }

            string targetPath = Path.GetFullPath(
                Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!targetPath.StartsWith(rootPrefix, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Release archive path escapes staging root: {entry.FullName}");
            }

            string? parent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            entry.ExtractToFile(targetPath, overwrite: false);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
