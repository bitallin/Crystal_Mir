using System.Security.Cryptography;
using System.Text.Json;

namespace Crystal.Patcher;

public static class PatchManifestSigner
{
    public static ClientPatchManifest Sign(
        ClientPatchManifest manifest,
        string privateKeyPem,
        string keyId,
        DateTimeOffset? signedAt = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Sign(manifest, key, keyId, signedAt);
    }

    public static ClientPatchManifest Sign(
        ClientPatchManifest manifest,
        ECDsa key,
        string keyId,
        DateTimeOffset? signedAt = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(key);
        EnsureP256Key(key);
        ValidateKeyId(keyId);

        var metadata = new ClientPatchSignature
        {
            Algorithm = PatchManifestFormat.SignatureAlgorithmEcdsaP256Sha256,
            KeyId = keyId.Trim(),
            SignedAt = (signedAt ?? DateTimeOffset.UtcNow).ToUniversalTime(),
            Value = string.Empty
        };
        var payload = BuildPayload(manifest, metadata);
        var signatureBytes = key.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var signed = manifest with
        {
            Signature = new ClientPatchSignature
            {
                Algorithm = metadata.Algorithm,
                KeyId = metadata.KeyId,
                SignedAt = metadata.SignedAt,
                Value = Convert.ToBase64String(signatureBytes)
            }
        };

        var verification = Verify(signed, key.ExportSubjectPublicKeyInfoPem(), signed.Signature!.KeyId);
        if (!verification.IsValid)
        {
            throw new CryptographicException(
                "Generated patch manifest signature could not be verified: " + verification.Error);
        }

        return signed;
    }

    public static PatchSignatureVerification Verify(
        ClientPatchManifest manifest,
        string publicKeyPem,
        string? expectedKeyId = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var signature = manifest.Signature;
        if (signature is null)
        {
            return PatchSignatureVerification.Failure("Patch manifest is not signed.");
        }

        if (!string.Equals(
                signature.Algorithm,
                PatchManifestFormat.SignatureAlgorithmEcdsaP256Sha256,
                StringComparison.Ordinal))
        {
            return PatchSignatureVerification.Failure(
                $"Unsupported signature algorithm '{signature.Algorithm}'.");
        }

        if (string.IsNullOrWhiteSpace(signature.KeyId))
        {
            return PatchSignatureVerification.Failure("Signature key id is empty.");
        }

        if (expectedKeyId is not null &&
            !string.Equals(signature.KeyId, expectedKeyId, StringComparison.Ordinal))
        {
            return PatchSignatureVerification.Failure(
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
                ? PatchSignatureVerification.Success
                : PatchSignatureVerification.Failure("Signature verification failed.");
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or ArgumentException)
        {
            return PatchSignatureVerification.Failure(
                $"Signature verification failed: {exception.Message}");
        }
    }

    private static byte[] BuildPayload(
        ClientPatchManifest manifest,
        ClientPatchSignature signature)
    {
        var envelope = new SignatureEnvelope(
            PatchManifestFormat.SignaturePayloadPrefix,
            PatchManifestFormat.SignaturePayloadVersion,
            manifest with { Signature = null },
            new SignatureMetadata(signature.Algorithm, signature.KeyId, signature.SignedAt));
        var element = JsonSerializer.SerializeToElement(envelope, PatchJson.CompactOptions);
        return PatchJson.CanonicalBytes(element);
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
                "Patch manifest signatures require an ECDSA NIST P-256 key.",
                nameof(key));
        }
    }

    private sealed record SignatureEnvelope(
        string Payload,
        int Version,
        ClientPatchManifest Manifest,
        SignatureMetadata Signature);

    private sealed record SignatureMetadata(string Algorithm, string KeyId, DateTimeOffset SignedAt);
}
