namespace Crystal.Patcher;

public sealed class PatchUpdateRequest
{
    public required ClientPatchManifest Manifest { get; init; }
    public required string RootDirectory { get; init; }
    public required string PublicKeyPem { get; init; }
    public Uri? ManifestBaseUri { get; init; }
    public PatchValidationOptions Validation { get; init; } = new();
    public IPatchDownloader? Downloader { get; init; }
    public bool DryRun { get; init; }
    public DateTimeOffset? Now { get; init; }
}

public sealed record PatchUpdateResult(
    string Release,
    bool UpToDate,
    bool DryRun,
    int Added,
    int Replaced,
    int Deleted,
    long DownloadBytes,
    IReadOnlyList<PatchAction> Actions,
    string? BackupDirectory);

public static class PatchUpdateService
{
    public static async Task<PatchUpdateResult> UpdateAsync(
        PatchUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var root = Path.GetFullPath(request.RootDirectory);
        Directory.CreateDirectory(root);

        PatchInstaller.RecoverPending(root);
        var state = PatchStateStore.Read(root);
        var now = (request.Now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var validationOptions = new PatchValidationOptions
        {
            ExpectedChannel = request.Validation.ExpectedChannel,
            ExpectedKeyId = request.Validation.ExpectedKeyId,
            ClientVersion = request.Validation.ClientVersion,
            LauncherVersion = request.Validation.LauncherVersion,
            ProtocolVersion = request.Validation.ProtocolVersion,
            ContentSchemaVersion = request.Validation.ContentSchemaVersion,
            Now = request.Validation.Now ?? now,
            MinimumGeneratedAt = request.Validation.MinimumGeneratedAt ?? state?.ManifestGeneratedAt,
            AllowedClockSkew = request.Validation.AllowedClockSkew
        };

        var validation = PatchManifestValidator.Validate(
            request.Manifest,
            request.PublicKeyPem,
            validationOptions);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(
                "Patch manifest validation failed: " + string.Join("; ", validation.Errors));
        }

        var scanOptions = new PatchScanOptions();
        var localFiles = PatchScanner.Scan(root, scanOptions);
        var plan = PatchPlanner.Create(request.Manifest, localFiles);
        if (request.DryRun)
        {
            return new PatchUpdateResult(
                request.Manifest.Release,
                plan.IsEmpty,
                true,
                plan.AddedCount,
                plan.ReplacedCount,
                plan.DeletedCount,
                plan.DownloadBytes,
                plan.Actions,
                null);
        }

        if (plan.IsEmpty)
        {
            if (state is null)
            {
                PatchStateStore.Write(root, new PatchState
                {
                    ActiveRelease = request.Manifest.Release,
                    AppliedAt = now,
                    ManifestGeneratedAt = request.Manifest.GeneratedAt
                });
            }

            return new PatchUpdateResult(
                request.Manifest.Release,
                true,
                false,
                0,
                0,
                0,
                0,
                plan.Actions,
                state?.BackupDirectory);
        }

        var baseUri = request.ManifestBaseUri;
        if (baseUri is null && request.Downloader is null)
        {
            throw new ArgumentException(
                "ManifestBaseUri is required when no patch downloader is supplied.",
                nameof(request));
        }

        var stagingDirectory = Path.Combine(
            PatchStateStore.GetStagingDirectory(root),
            request.Manifest.Release + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        HttpPatchDownloader? ownedHttpDownloader = null;

        try
        {
            var downloader = request.Downloader;
            if (downloader is null)
            {
                if (baseUri!.Scheme == "file")
                {
                    downloader = new FilePatchDownloader(baseUri);
                }
                else
                {
                    ownedHttpDownloader = new HttpPatchDownloader(baseUri);
                    downloader = ownedHttpDownloader;
                }
            }

            foreach (var action in plan.Actions.Where(action =>
                         action.Kind is PatchActionKind.Add or PatchActionKind.Replace))
            {
                if (action.File is null)
                {
                    throw new InvalidDataException($"Patch action '{action.Path}' has no file entry.");
                }

                var stagedPath = PatchPath.ResolveUnder(stagingDirectory, action.Path);
                await downloader.DownloadAsync(action.File.Url, stagedPath, cancellationToken)
                    .ConfigureAwait(false);
                VerifyStagedFile(stagedPath, action.File);
            }

            var result = PatchInstaller.Apply(
                root,
                request.Manifest,
                plan,
                stagingDirectory,
                state,
                now);
            return new PatchUpdateResult(
                request.Manifest.Release,
                false,
                false,
                result.Added,
                result.Replaced,
                result.Deleted,
                plan.DownloadBytes,
                plan.Actions,
                result.BackupDirectory);
        }
        finally
        {
            ownedHttpDownloader?.Dispose();
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static void VerifyStagedFile(string path, ClientPatchFile file)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Downloaded patch file '{file.Path}' is missing.", path);
        }

        var info = new FileInfo(path);
        if (info.Length != file.Size)
        {
            throw new InvalidDataException(
                $"Downloaded patch file '{file.Path}' has size {info.Length}, expected {file.Size}.");
        }

        var hash = PatchJson.Sha256File(path);
        if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Downloaded patch file '{file.Path}' has SHA256 {hash}, expected {file.Sha256}.");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
