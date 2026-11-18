namespace Crystal.Patcher;

public static class PatchManifestFormat
{
    public const int FormatVersion = 1;
    public const int SignaturePayloadVersion = 1;
    public const string SignaturePayloadPrefix = "crystal-client-patch-v1";
    public const string SignatureAlgorithmEcdsaP256Sha256 = "ecdsa-p256-sha256";
    public const string StateDirectoryName = ".crystal-update";
}
