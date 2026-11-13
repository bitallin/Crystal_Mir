using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Crystal.Content;

namespace Crystal.Control.Api.Content;

public sealed partial class ContentReleaseService
{
    private static readonly string[] CompiledOrLater =
    [
        "compiled",
        "signed",
        "published",
        "active"
    ];

    private readonly ContentReleaseStore _store;
    private readonly ContentQueryStore _queryStore;
    private readonly ControlApiOptions _options;

    public ContentReleaseService(
        ContentReleaseStore store,
        ContentQueryStore queryStore,
        ControlApiOptions options)
    {
        _store = store;
        _queryStore = queryStore;
        _options = options;
    }

    public static bool IsValidVersion(string? version)
    {
        return version is not null && VersionPattern().IsMatch(version);
    }

    public async Task<IReadOnlyList<ReleaseDto>> ListAsync(
        int limit,
        string? status,
        CancellationToken cancellationToken)
    {
        return await _store.ListAsync(limit, status, cancellationToken);
    }

    public async Task<ReleaseDto> GetAsync(long id, CancellationToken cancellationToken)
    {
        return await _store.GetAsync(id, cancellationToken)
               ?? throw new ReleaseNotFoundException($"Release {id} does not exist.");
    }

    public async Task<ReleaseDto> CreateAsync(
        long? importRunId,
        string version,
        string? notes,
        long? createdBy,
        CancellationToken cancellationToken)
    {
        if (!IsValidVersion(version))
        {
            throw new ReleaseConflictException($"Invalid release version '{version}'.");
        }

        var resolvedRunId = await _queryStore.ResolveImportRunIdAsync(importRunId, cancellationToken);
        if (resolvedRunId is null)
        {
            throw importRunId is null
                ? new ReleaseConflictException("No imported content run is available to create a release.")
                : new ReleaseNotFoundException($"Import run {importRunId} does not exist or is not imported.");
        }

        var run = await _queryStore.GetImportRunAsync(resolvedRunId.Value, cancellationToken)
                  ?? throw new ReleaseNotFoundException($"Import run {resolvedRunId} does not exist.");
        var entities = await _store.GetImportEntitiesAsync(run.Id, cancellationToken);
        var createdAt = DateTimeOffset.UtcNow;
        ContentSnapshotBuild build;
        try
        {
            build = BuildSnapshot(run, entities, version, createdAt);
        }
        catch (InvalidDataException exception)
        {
            throw new ReleaseValidationException(new ReleaseValidationReportDto(
                0,
                version,
                string.Empty,
                entities.Count,
                false,
                [new ReleaseValidationIssueDto("snapshot-build-failed", exception.Message, null)]));
        }

        var expectedEntityCount = build.Manifest.Counts.Values.Sum();

        return await _store.CreateAsync(
            run.Id,
            version,
            notes,
            createdBy,
            createdAt,
            build.Manifest.SchemaVersion,
            build.Manifest.FormatVersion,
            build.Manifest.ContentHash,
            expectedEntityCount,
            cancellationToken);
    }

    public async Task<ReleaseValidationReportDto> ValidateAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var release = await GetAsync(id, cancellationToken);
        var entities = await _store.GetReleaseEntitiesAsync(id, cancellationToken);
        var issues = new List<ReleaseValidationIssueDto>();
        ContentSnapshotBuild? build = null;

        if (entities.Count == 0)
        {
            issues.Add(new ReleaseValidationIssueDto(
                "empty-release",
                "Release contains no entities.",
                null));
        }

        try
        {
            build = await BuildSnapshotAsync(release, entities, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            issues.Add(new ReleaseValidationIssueDto(
                "snapshot-build-failed",
                exception.Message,
                null));
        }

        if (build is not null)
        {
            if (!string.Equals(build.Manifest.ContentHash, release.ContentHash, StringComparison.Ordinal))
            {
                issues.Add(new ReleaseValidationIssueDto(
                    "content-hash-mismatch",
                    $"Stored content hash {release.ContentHash} does not match rebuilt hash {build.Manifest.ContentHash}.",
                    null));
            }

            foreach (var issue in ContentSnapshotValidator.Validate(new ContentSnapshot
                     {
                         Manifest = build.Manifest,
                         Entities = entities
                     }))
            {
                issues.Add(new ReleaseValidationIssueDto(issue.Code, issue.Message, issue.ContentKey));
            }

            issues.AddRange(ValidateEntitySemantics(entities));
        }

        return new ReleaseValidationReportDto(
            release.Id,
            release.Version,
            release.ContentHash,
            entities.Count,
            issues.Count == 0,
            issues);
    }

    public async Task<ReleaseDto> CompileAsync(long id, CancellationToken cancellationToken)
    {
        var release = await GetAsync(id, cancellationToken);
        if (CompiledOrLater.Contains(release.Status, StringComparer.Ordinal))
        {
            await EnsureArtifactAsync(release, cancellationToken);
            return release;
        }

        if (release.Status is not ("draft" or "failed"))
        {
            throw new ReleaseConflictException(
                $"Release {id} is '{release.Status}' and cannot be compiled.");
        }

        var report = await ValidateAsync(id, cancellationToken);
        if (!report.Valid)
        {
            await _store.TransitionAsync(
                id,
                ["draft", "failed"],
                "failed",
                null,
                null,
                false,
                cancellationToken);
            throw new ReleaseValidationException(report);
        }

        var entities = await _store.GetReleaseEntitiesAsync(id, cancellationToken);
        var build = await BuildSnapshotAsync(release, entities, cancellationToken);
        WriteArtifact(release.Version, build);

        var updated = await _store.TransitionAsync(
            id,
            ["draft", "failed"],
            "compiled",
            build.Manifest.ContentHash,
            release.Version,
            false,
            cancellationToken);
        if (updated is not null) return updated;

        var current = await GetAsync(id, cancellationToken);
        if (CompiledOrLater.Contains(current.Status, StringComparer.Ordinal))
        {
            await EnsureArtifactAsync(current, cancellationToken);
            return current;
        }

        throw new ReleaseConflictException(
            $"Release {id} changed to '{current.Status}' while compiling.");
    }

    public async Task<ReleaseDto> SignAsync(long id, CancellationToken cancellationToken)
    {
        var release = await GetAsync(id, cancellationToken);
        if (release.Status is "signed" or "published" or "active")
        {
            await EnsureSignatureAsync(release, cancellationToken);
            return release;
        }

        if (release.Status != "compiled")
        {
            throw new ReleaseConflictException(
                $"Release {id} is '{release.Status}'; compile it before signing.");
        }

        var signing = GetSigningMaterial(requirePrivateKey: true);
        var directory = GetReleaseDirectory(release.Version);
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new ReleaseConflictException($"Release artifact is missing: {manifestPath}.");
        }

        var signed = ContentSnapshotSigner.SignSnapshot(
            directory,
            signing.PrivateKeyPem!,
            signing.KeyId!);
        if (!string.Equals(signed.ContentHash, release.ContentHash, StringComparison.Ordinal))
        {
            throw new ReleaseConflictException("Signed artifact content hash does not match the release record.");
        }

        var verification = ContentSnapshotSigner.Verify(signed, signing.PublicKeyPem, signing.KeyId);
        if (!verification.IsValid)
        {
            throw new ReleaseConflictException(
                $"Signed artifact failed verification: {verification.Error}");
        }

        var updated = await _store.TransitionAsync(
            id,
            ["compiled"],
            "signed",
            null,
            null,
            false,
            cancellationToken);
        if (updated is not null) return updated;

        var current = await GetAsync(id, cancellationToken);
        if (current.Status is "signed" or "published" or "active")
        {
            await EnsureSignatureAsync(current, cancellationToken);
            return current;
        }

        throw new ReleaseConflictException(
            $"Release {id} changed to '{current.Status}' while signing.");
    }

    public async Task<ReleaseDto> PublishAsync(long id, CancellationToken cancellationToken)
    {
        var release = await GetAsync(id, cancellationToken);
        if (release.Status is "published" or "active")
        {
            await EnsureSignatureAsync(release, cancellationToken);
            return release;
        }

        if (release.Status != "signed")
        {
            throw new ReleaseConflictException(
                $"Release {id} is '{release.Status}'; sign it before publishing.");
        }

        await EnsureSignatureAsync(release, cancellationToken);
        var updated = await _store.TransitionAsync(
            id,
            ["signed"],
            "published",
            null,
            null,
            true,
            cancellationToken);
        if (updated is not null) return updated;

        var current = await GetAsync(id, cancellationToken);
        if (current.Status is "published" or "active")
        {
            await EnsureSignatureAsync(current, cancellationToken);
            return current;
        }

        throw new ReleaseConflictException(
            $"Release {id} changed to '{current.Status}' while publishing.");
    }

    public async Task<string> GetManifestJsonAsync(long id, CancellationToken cancellationToken)
    {
        var release = await GetAsync(id, cancellationToken);
        var path = GetManifestPath(release);
        if (!File.Exists(path))
        {
            throw new ReleaseConflictException(
                $"Release {id} has no compiled artifact at {path}.");
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    private ContentSnapshotBuild BuildSnapshot(
        ImportRunDto run,
        IReadOnlyList<ContentEntity> entities,
        string version,
        DateTimeOffset createdAt)
    {
        return ContentSnapshotWriter.Build(new ContentSnapshotWriteOptions
        {
            OutputDirectory = GetReleaseDirectory(version),
            ReleaseVersion = version,
            Source = $"content_import_run:{run.Id}",
            SourceHash = run.SourceHash,
            CreatedAt = createdAt
        }, entities);
    }

    private async Task<ContentSnapshotBuild> BuildSnapshotAsync(
        ReleaseDto release,
        IReadOnlyList<ContentEntity> entities,
        CancellationToken cancellationToken)
    {
        ImportRunDto? run = release.ImportRunId is long importRunId
            ? await _queryStore.GetImportRunAsync(importRunId, cancellationToken)
            : null;
        var createdAt = ToUtcOffset(release.CreatedAt);
        return ContentSnapshotWriter.Build(new ContentSnapshotWriteOptions
        {
            OutputDirectory = GetReleaseDirectory(release.Version),
            ReleaseVersion = release.Version,
            Source = release.ImportRunId is long id ? $"content_import_run:{id}" : $"content_release:{release.Id}",
            SourceHash = run?.SourceHash,
            CreatedAt = createdAt
        }, entities);
    }

    private void WriteArtifact(string version, ContentSnapshotBuild build)
    {
        Directory.CreateDirectory(_options.ReleaseRoot);
        var finalDirectory = GetReleaseDirectory(version);
        if (Directory.Exists(finalDirectory))
        {
            EnsureArtifactHash(finalDirectory, build.Manifest.ContentHash);
            return;
        }

        var temporaryDirectory = Path.Combine(
            _options.ReleaseRoot,
            $".tmp-{version}-{Guid.NewGuid():N}");
        try
        {
            ContentSnapshotWriter.WriteBuild(build, temporaryDirectory);
            try
            {
                Directory.Move(temporaryDirectory, finalDirectory);
            }
            catch (IOException) when (Directory.Exists(finalDirectory))
            {
                EnsureArtifactHash(finalDirectory, build.Manifest.ContentHash);
            }
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    private async Task EnsureArtifactAsync(ReleaseDto release, CancellationToken cancellationToken)
    {
        var manifestPath = GetManifestPath(release);
        if (!File.Exists(manifestPath))
        {
            throw new ReleaseConflictException(
                $"Release {release.Id} artifact is missing at {manifestPath}.");
        }

        var snapshot = await Task.Run(() => ContentSnapshotReader.Read(GetReleaseDirectory(release.Version)), cancellationToken);
        if (!string.Equals(snapshot.Manifest.ContentHash, release.ContentHash, StringComparison.Ordinal))
        {
            throw new ReleaseConflictException(
                $"Release {release.Id} artifact hash does not match the release record.");
        }
    }

    private async Task EnsureSignatureAsync(ReleaseDto release, CancellationToken cancellationToken)
    {
        await EnsureArtifactAsync(release, cancellationToken);
        var snapshot = await Task.Run(() => ContentSnapshotReader.Read(GetReleaseDirectory(release.Version)), cancellationToken);
        if (snapshot.Manifest.Signature is null)
        {
            throw new ReleaseConflictException($"Release {release.Id} artifact is not signed.");
        }

        var signing = GetSigningMaterial(requirePrivateKey: false);
        var verification = ContentSnapshotSigner.Verify(
            snapshot.Manifest,
            signing.PublicKeyPem,
            signing.KeyId);
        if (!verification.IsValid)
        {
            throw new ReleaseConflictException(
                $"Release {release.Id} signature verification failed: {verification.Error}");
        }
    }

    private void EnsureArtifactHash(string directory, string expectedContentHash)
    {
        var snapshot = ContentSnapshotReader.Read(directory);
        if (!string.Equals(snapshot.Manifest.ContentHash, expectedContentHash, StringComparison.Ordinal))
        {
            throw new ReleaseConflictException(
                $"Existing artifact at {directory} has content hash {snapshot.Manifest.ContentHash}, expected {expectedContentHash}.");
        }
    }

    private SigningMaterial GetSigningMaterial(bool requirePrivateKey)
    {
        if (string.IsNullOrWhiteSpace(_options.ReleaseSigningKeyId))
        {
            throw new ReleaseSigningNotConfiguredException(
                "ControlApi:ReleaseSigningKeyId is not configured.");
        }

        var privateKeyPem = ReadOptionalPem(_options.ReleaseSigningPrivateKeyPath);
        var publicKeyPem = ReadOptionalPem(_options.ReleaseSigningPublicKeyPath);
        if (requirePrivateKey && privateKeyPem is null)
        {
            throw new ReleaseSigningNotConfiguredException(
                "ControlApi:ReleaseSigningPrivateKeyPath is not configured.");
        }

        if (publicKeyPem is null && privateKeyPem is not null)
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(privateKeyPem);
            publicKeyPem = key.ExportSubjectPublicKeyInfoPem();
        }

        if (publicKeyPem is null)
        {
            throw new ReleaseSigningNotConfiguredException(
                "A release signing public key or private key must be configured.");
        }

        return new SigningMaterial(
            _options.ReleaseSigningKeyId,
            privateKeyPem,
            publicKeyPem);
    }

    private static string? ReadOptionalPem(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new ReleaseSigningNotConfiguredException(
                $"Configured signing key file does not exist: {fullPath}.");
        }

        return File.ReadAllText(fullPath);
    }

    private string GetReleaseDirectory(string version)
    {
        if (!IsValidVersion(version))
        {
            throw new ReleaseConflictException($"Invalid release version '{version}'.");
        }

        return Path.Combine(_options.ReleaseRoot, version);
    }

    private string GetManifestPath(ReleaseDto release)
    {
        return Path.Combine(GetReleaseDirectory(release.Version), "manifest.json");
    }

    private static IEnumerable<ReleaseValidationIssueDto> ValidateEntitySemantics(
        IReadOnlyList<ContentEntity> entities)
    {
        foreach (var entity in entities)
        {
            if (entity.Data.ValueKind != JsonValueKind.Object) continue;

            if (entity.EntityType == ContentEntityTypes.Script &&
                entity.Data.TryGetProperty("validation", out var validation))
            {
                if (validation.ValueKind == JsonValueKind.Object &&
                    validation.TryGetProperty("errorCount", out var errorCount) &&
                    errorCount.TryGetInt32(out var errors) &&
                    errors > 0)
                {
                    yield return new ReleaseValidationIssueDto(
                        "script-validation-error",
                        $"NPC script has {errors} parse error(s).",
                        entity.ContentKey);
                }
            }

            if (entity.EntityType == ContentEntityTypes.Asset &&
                entity.Data.TryGetProperty("assetType", out var assetType) &&
                assetType.ValueKind == JsonValueKind.String &&
                string.Equals(assetType.GetString(), "map", StringComparison.Ordinal) &&
                entity.Data.TryGetProperty("map", out var map) &&
                map.ValueKind == JsonValueKind.Object &&
                map.TryGetProperty("valid", out var valid) &&
                valid.ValueKind == JsonValueKind.False)
            {
                yield return new ReleaseValidationIssueDto(
                    "map-asset-invalid",
                    "Map asset metadata is marked invalid.",
                    entity.ContentKey);
            }
        }
    }

    private static DateTimeOffset ToUtcOffset(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
            : new DateTimeOffset(value.ToUniversalTime());
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    private sealed record SigningMaterial(
        string KeyId,
        string? PrivateKeyPem,
        string PublicKeyPem);
}
