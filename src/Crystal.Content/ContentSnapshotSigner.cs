using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Crystal.Content;

public sealed record ContentSnapshotSignatureVerification(bool IsValid, string? Error)
{
    public static ContentSnapshotSignatureVerification Success { get; } = new(true, null);

    public static ContentSnapshotSignatureVerification Failure(string error) => new(false, error);
}

public static class ContentSnapshotSigner
{
    public static ContentSnapshotManifest SignSnapshot(
        string snapshotDirectory,
        string privateKeyPem,
        string keyId,
        DateTimeOffset? signedAt = null)
    {
        var directory = Path.GetFullPath(snapshotDirectory);
        var snapshot = ContentSnapshotReader.Read(directory);
        var issues = ContentSnapshotValidator.Validate(snapshot);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(
                "Snapshot validation failed: " + string.Join("; ", issues.Select(issue => issue.Message)));
        }

        var signed = Sign(snapshot.Manifest, privateKeyPem, keyId, signedAt);
        WriteManifestFiles(directory, signed);
        return signed;
    }

    public static ContentSnapshotManifest Sign(
        ContentSnapshotManifest manifest,
        string privateKeyPem,
        string keyId,
        DateTimeOffset? signedAt = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Sign(manifest, key, keyId, signedAt);
    }

    public static ContentSnapshotManifest Sign(
        ContentSnapshotManifest manifest,
        ECDsa key,
        string keyId,
        DateTimeOffset? signedAt = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(key);
        EnsureP256Key(key);
        ValidateKeyId(keyId);

        var signatureMetadata = new ContentSnapshotSignature
        {
            Algorithm = ContentSnapshotFormat.SignatureAlgorithmEcdsaP256Sha256,
            KeyId = keyId.Trim(),
            SignedAt = (signedAt ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            Value = string.Empty
        };
        var payload = BuildPayload(manifest, signatureMetadata);
        var signatureBytes = key.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var signed = CopyManifest(manifest, new ContentSnapshotSignature
        {
            Algorithm = signatureMetadata.Algorithm,
            KeyId = signatureMetadata.KeyId,
            SignedAt = signatureMetadata.SignedAt,
            Value = Convert.ToBase64String(signatureBytes)
        });

        var verification = Verify(signed, key.ExportSubjectPublicKeyInfoPem(), signed.Signature!.KeyId);
        if (!verification.IsValid)
        {
            throw new CryptographicException(
                "Generated content signature could not be verified: " + verification.Error);
        }

        return signed;
    }

    public static ContentSnapshotSignatureVerification VerifySnapshot(
        string snapshotDirectory,
        string publicKeyPem,
        string? expectedKeyId = null)
    {
        var snapshot = ContentSnapshotReader.Read(snapshotDirectory);
        return Verify(snapshot.Manifest, publicKeyPem, expectedKeyId);
    }

    public static ContentSnapshotSignatureVerification Verify(
        ContentSnapshotManifest manifest,
        string publicKeyPem,
        string? expectedKeyId = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var signature = manifest.Signature;
        if (signature is null)
        {
            return ContentSnapshotSignatureVerification.Failure("Manifest is not signed.");
        }

        if (!string.Equals(
                signature.Algorithm,
                ContentSnapshotFormat.SignatureAlgorithmEcdsaP256Sha256,
                StringComparison.Ordinal))
        {
            return ContentSnapshotSignatureVerification.Failure(
                $"Unsupported signature algorithm '{signature.Algorithm}'.");
        }

        if (string.IsNullOrWhiteSpace(signature.KeyId))
        {
            return ContentSnapshotSignatureVerification.Failure("Signature key id is empty.");
        }

        if (expectedKeyId is not null &&
            !string.Equals(signature.KeyId, expectedKeyId, StringComparison.Ordinal))
        {
            return ContentSnapshotSignatureVerification.Failure(
                $"Signature key id '{signature.KeyId}' does not match expected key id '{expectedKeyId}'.");
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            EnsureP256Key(key);
            var signatureBytes = Convert.FromBase64String(signature.Value);
            var payload = BuildPayload(manifest, signature);
            return key.VerifyData(
                payload,
                signatureBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                ? ContentSnapshotSignatureVerification.Success
                : ContentSnapshotSignatureVerification.Failure("Signature verification failed.");
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException or FormatException)
        {
            return ContentSnapshotSignatureVerification.Failure(
                $"Signature verification failed: {exception.Message}");
        }
    }

    private static byte[] BuildPayload(
        ContentSnapshotManifest manifest,
        ContentSnapshotSignature signature)
    {
        var payload = new SignaturePayload(
            ContentSnapshotFormat.SignaturePayloadPrefix,
            ContentSnapshotFormat.SignaturePayloadVersion,
            CopyManifest(manifest, signature: null),
            new SignatureMetadata(
                signature.Algorithm,
                signature.KeyId,
                signature.SignedAt.ToUniversalTime()));
        var element = JsonSerializer.SerializeToElement(payload, ContentJson.CompactOptions);
        var canonical = ContentJson.Canonicalize(element);
        return Encoding.UTF8.GetBytes(canonical.GetRawText());
    }

    private static ContentSnapshotManifest CopyManifest(
        ContentSnapshotManifest manifest,
        ContentSnapshotSignature? signature)
    {
        return new ContentSnapshotManifest
        {
            FormatVersion = manifest.FormatVersion,
            SchemaVersion = manifest.SchemaVersion,
            ReleaseVersion = manifest.ReleaseVersion,
            Source = manifest.Source,
            SourceHash = manifest.SourceHash,
            Generator = manifest.Generator,
            CreatedAt = manifest.CreatedAt,
            ContentHash = manifest.ContentHash,
            Entities = manifest.Entities,
            Counts = manifest.Counts,
            Legacy = manifest.Legacy,
            Signature = signature
        };
    }

    private static void WriteManifestFiles(string directory, ContentSnapshotManifest manifest)
    {
        File.WriteAllText(
            Path.Combine(directory, "manifest.json"),
            JsonSerializer.Serialize(manifest, ContentJson.IndentedOptions) + "\n");
        File.WriteAllText(
            Path.Combine(directory, "summary.json"),
            JsonSerializer.Serialize(new
            {
                manifest.ReleaseVersion,
                manifest.Source,
                manifest.SourceHash,
                manifest.ContentHash,
                manifest.Counts,
                manifest.Signature
            }, ContentJson.IndentedOptions) + "\n");
    }

    private static void ValidateKeyId(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
        {
            throw new ArgumentException("Signature key id is required.", nameof(keyId));
        }

        if (keyId.Length > 128)
        {
            throw new ArgumentException("Signature key id must not exceed 128 characters.", nameof(keyId));
        }
    }

    private static void EnsureP256Key(ECDsa key)
    {
        var curve = key.ExportParameters(includePrivateParameters: false).Curve;
        var curveOid = curve.Oid.Value;
        var curveName = curve.Oid.FriendlyName;
        var isNistP256 =
            string.Equals(curveOid, "1.2.840.10045.3.1.7", StringComparison.Ordinal) ||
            string.Equals(curveName, "nistP256", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(curveName, "ECDSA_P256", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(curveName, "prime256v1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(curveName, "secp256r1", StringComparison.OrdinalIgnoreCase);
        if (key.KeySize != 256 || !isNistP256)
        {
            throw new ArgumentException(
                "Content signatures require an ECDSA NIST P-256 key.",
                nameof(key));
        }
    }

    private sealed record SignaturePayload(
        string Payload,
        int Version,
        ContentSnapshotManifest Manifest,
        SignatureMetadata Signature);

    private sealed record SignatureMetadata(string Algorithm, string KeyId, DateTimeOffset SignedAt);
}
