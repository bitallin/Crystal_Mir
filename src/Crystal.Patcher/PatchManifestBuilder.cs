namespace Crystal.Patcher;

public sealed class PatchBuildOptions
{
    public required string RootDirectory { get; init; }
    public required string Channel { get; init; }
    public required string Release { get; init; }
    public required string UrlPrefix { get; init; }
    public string MinimumLauncherVersion { get; init; } = "0.0.0";
    public string MinimumClientVersion { get; init; } = "0.0.0";
    public int ProtocolVersion { get; init; } = 1;
    public int ContentSchemaVersion { get; init; } = 1;
    public DateTimeOffset? GeneratedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public List<string> Deleted { get; init; } = new();
    public List<string> ExcludedPrefixes { get; init; } = new();
}

public static class PatchManifestBuilder
{
    public static ClientPatchManifest Build(PatchBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Channel))
        {
            throw new ArgumentException("A channel is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Release))
        {
            throw new ArgumentException("A release is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.UrlPrefix))
        {
            throw new ArgumentException("A URL prefix is required.", nameof(options));
        }

        var generatedAt = (options.GeneratedAt ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var expiresAt = (options.ExpiresAt ?? generatedAt.AddDays(30)).ToUniversalTime();
        if (expiresAt <= generatedAt)
        {
            throw new ArgumentException("Expiration time must be later than generation time.", nameof(options));
        }

        var scan = PatchScanner.Scan(options.RootDirectory, new PatchScanOptions
        {
            ExcludedPrefixes = options.ExcludedPrefixes
        });
        var prefix = options.UrlPrefix.TrimEnd('/');
        var files = scan.Values
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(file => new ClientPatchFile
            {
                Path = file.Path,
                Size = file.Size,
                Sha256 = file.Sha256,
                Url = prefix + "/" + file.Path
            })
            .ToList();
        var deleted = options.Deleted
            .Select(PatchPath.NormalizeOrThrow)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ClientPatchManifest
        {
            Channel = options.Channel,
            Release = options.Release,
            MinimumLauncherVersion = options.MinimumLauncherVersion,
            MinimumClientVersion = options.MinimumClientVersion,
            ProtocolVersion = options.ProtocolVersion,
            ContentSchemaVersion = options.ContentSchemaVersion,
            GeneratedAt = generatedAt,
            ExpiresAt = expiresAt,
            Files = files,
            Deleted = deleted
        };
    }
}
