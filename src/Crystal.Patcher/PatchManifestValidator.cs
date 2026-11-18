namespace Crystal.Patcher;

public static class PatchManifestValidator
{
    public static PatchValidationResult Validate(
        ClientPatchManifest manifest,
        string publicKeyPem,
        PatchValidationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        options ??= new PatchValidationOptions();
        var errors = new List<string>();

        if (manifest.FormatVersion != PatchManifestFormat.FormatVersion)
        {
            errors.Add(
                $"Unsupported patch manifest format version {manifest.FormatVersion}; expected {PatchManifestFormat.FormatVersion}.");
        }

        var signature = PatchManifestSigner.Verify(manifest, publicKeyPem, options.ExpectedKeyId);
        if (!signature.IsValid)
        {
            errors.Add(signature.Error ?? "Signature verification failed.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Channel))
        {
            errors.Add("Manifest channel is empty.");
        }
        else if (options.ExpectedChannel is not null &&
                 !string.Equals(manifest.Channel, options.ExpectedChannel, StringComparison.Ordinal))
        {
            errors.Add($"Manifest channel '{manifest.Channel}' does not match expected channel '{options.ExpectedChannel}'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Release))
        {
            errors.Add("Manifest release is empty.");
        }

        ValidateMinimumVersion(
            errors,
            "launcher",
            options.LauncherVersion,
            manifest.MinimumLauncherVersion);
        ValidateMinimumVersion(
            errors,
            "client",
            options.ClientVersion,
            manifest.MinimumClientVersion);

        if (manifest.ProtocolVersion != options.ProtocolVersion)
        {
            errors.Add(
                $"Patch protocol version {manifest.ProtocolVersion} is incompatible with local protocol version {options.ProtocolVersion}.");
        }

        if (manifest.ContentSchemaVersion != options.ContentSchemaVersion)
        {
            errors.Add(
                $"Patch content schema version {manifest.ContentSchemaVersion} is incompatible with local content schema version {options.ContentSchemaVersion}.");
        }

        var now = (options.Now ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var generatedAt = manifest.GeneratedAt.ToUniversalTime();
        var expiresAt = manifest.ExpiresAt.ToUniversalTime();
        if (expiresAt <= now)
        {
            errors.Add($"Patch manifest expired at {expiresAt:O}.");
        }

        if (generatedAt > now + options.AllowedClockSkew)
        {
            errors.Add($"Patch manifest generation time {generatedAt:O} is in the future.");
        }

        if (expiresAt <= generatedAt)
        {
            errors.Add("Patch manifest expiration time must be later than its generation time.");
        }

        if (options.MinimumGeneratedAt is { } minimumGeneratedAt &&
            generatedAt < minimumGeneratedAt.ToUniversalTime())
        {
            errors.Add(
                $"Patch manifest generation time {generatedAt:O} is older than the last applied manifest {minimumGeneratedAt.ToUniversalTime():O}; downgrade rejected.");
        }

        if (manifest.Files is null)
        {
            errors.Add("Manifest files list is missing.");
        }

        if (manifest.Deleted is null)
        {
            errors.Add("Manifest deleted list is missing.");
        }

        var files = manifest.Files ?? new List<ClientPatchFile>();
        var deleted = manifest.Deleted ?? new List<string>();
        ValidatePaths(errors, files, deleted);
        ValidateFiles(errors, files);

        return errors.Count == 0
            ? PatchValidationResult.Success
            : new PatchValidationResult(errors);
    }

    private static void ValidateMinimumVersion(
        List<string> errors,
        string name,
        string localVersionText,
        string minimumVersionText)
    {
        if (!Version.TryParse(localVersionText, out var localVersion))
        {
            errors.Add($"Local {name} version '{localVersionText}' is invalid.");
            return;
        }

        if (!Version.TryParse(minimumVersionText, out var minimumVersion))
        {
            errors.Add($"Manifest minimum {name} version '{minimumVersionText}' is invalid.");
            return;
        }

        if (localVersion < minimumVersion)
        {
            errors.Add(
                $"Local {name} version {localVersion} is below the required minimum {minimumVersion}.");
        }
    }

    private static void ValidatePaths(
        List<string> errors,
        IReadOnlyList<ClientPatchFile> files,
        IReadOnlyList<string> deleted)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (!PatchPath.TryNormalize(file.Path, out var normalized, out var error))
            {
                errors.Add(error);
                continue;
            }

            if (!paths.Add(normalized))
            {
                errors.Add($"Duplicate manifest path '{normalized}'.");
            }
        }

        foreach (var deletedPath in deleted)
        {
            if (!PatchPath.TryNormalize(deletedPath, out var normalized, out var error))
            {
                errors.Add(error);
                continue;
            }

            if (!paths.Add(normalized))
            {
                errors.Add($"Duplicate manifest path '{normalized}'.");
            }
        }
    }

    private static void ValidateFiles(List<string> errors, IReadOnlyList<ClientPatchFile> files)
    {
        foreach (var file in files)
        {
            if (file.Size < 0)
            {
                errors.Add($"File '{file.Path}' has a negative size.");
            }

            if (!PatchJson.IsSha256Hex(file.Sha256))
            {
                errors.Add($"File '{file.Path}' has an invalid SHA256 value.");
            }

            if (string.IsNullOrWhiteSpace(file.Url))
            {
                errors.Add($"File '{file.Path}' has an empty download URL.");
                continue;
            }

            if (Uri.TryCreate(file.Url, UriKind.Absolute, out var absolute) &&
                absolute.Scheme is not ("http" or "https"))
            {
                errors.Add($"File '{file.Path}' uses unsupported download scheme '{absolute.Scheme}'.");
            }
        }
    }
}
