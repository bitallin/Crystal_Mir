using System.Text.Json.Serialization;

namespace Crystal.Patcher;

public sealed record ClientPatchManifest
{
    public int FormatVersion { get; init; } = PatchManifestFormat.FormatVersion;
    public string Channel { get; init; } = string.Empty;
    public string Release { get; init; } = string.Empty;
    public string MinimumLauncherVersion { get; init; } = "0.0.0";
    public string MinimumClientVersion { get; init; } = "0.0.0";
    public int ProtocolVersion { get; init; } = 1;
    public int ContentSchemaVersion { get; init; } = 1;
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public List<ClientPatchFile> Files { get; init; } = new();
    public List<string> Deleted { get; init; } = new();
    public ClientPatchSignature? Signature { get; init; }
}

public sealed record ClientPatchFile
{
    public string Path { get; init; } = string.Empty;
    public long Size { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public sealed record ClientPatchSignature
{
    public string Algorithm { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
    public DateTimeOffset SignedAt { get; init; }
    public string Value { get; init; } = string.Empty;
}

public sealed record PatchSignatureVerification(bool IsValid, string? Error)
{
    public static PatchSignatureVerification Success { get; } = new(true, null);

    public static PatchSignatureVerification Failure(string error) => new(false, error);
}

public sealed record PatchValidationResult(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static PatchValidationResult Success { get; } = new(Array.Empty<string>());

    public static PatchValidationResult Failure(params string[] errors) => new(errors);
}

public sealed class PatchValidationOptions
{
    public string? ExpectedChannel { get; init; }
    public string? ExpectedKeyId { get; init; }
    public string ClientVersion { get; init; } = "0.0.0";
    public string LauncherVersion { get; init; } = "0.0.0";
    public int ProtocolVersion { get; init; } = 1;
    public int ContentSchemaVersion { get; init; } = 1;
    public DateTimeOffset? Now { get; init; }
    public DateTimeOffset? MinimumGeneratedAt { get; init; }
    public TimeSpan AllowedClockSkew { get; init; } = TimeSpan.FromMinutes(5);
}

public sealed record PatchManifestDocument(
    ClientPatchManifest Manifest,
    Uri BaseUri,
    string RawJson);
