namespace Crystal.Content;

public static class ContentSnapshotFormat
{
    public const int FormatVersion = 1;
    public const int SchemaVersion = 1;
    public const string DefaultGenerator = "Crystal.Content/1";
    public const string ContentHashPrefix = "crystal-content-snapshot-v1";
    public const string SignaturePayloadPrefix = "crystal-content-signature-v1";
    public const string SignatureAlgorithmEcdsaP256Sha256 = "ecdsa-p256-sha256";
    public const int SignaturePayloadVersion = 1;
}
