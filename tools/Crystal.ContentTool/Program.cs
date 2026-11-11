using System.Globalization;
using System.Text.Json;
using Crystal.Content;

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine(HelpText.Value);
    return 0;
}

try
{
    return args[0] switch
    {
        "sign" => Sign(args[1..]),
        "verify" => Verify(args[1..]),
        _ => throw new ArgumentException($"Unknown command: {args[0]}")
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int Sign(string[] args)
{
    var options = ParseOptions(args, requireSnapshot: true, requireKey: true, requireKeyId: true);
    var privateKeyPem = File.ReadAllText(options.Key!);
    var manifest = ContentSnapshotSigner.SignSnapshot(
        options.Snapshot!,
        privateKeyPem,
        options.KeyId!,
        options.SignedAt);

    PrintResult(new
    {
        snapshot = Path.GetFullPath(options.Snapshot!),
        releaseVersion = manifest.ReleaseVersion,
        contentHash = manifest.ContentHash,
        signature = manifest.Signature
    });
    return 0;
}

static int Verify(string[] args)
{
    var options = ParseOptions(args, requireSnapshot: true, requireKey: true, requireKeyId: false);
    var publicKeyPem = File.ReadAllText(options.Key!);
    var snapshot = ContentSnapshotReader.Read(options.Snapshot!);
    var verification = ContentSnapshotSigner.Verify(snapshot.Manifest, publicKeyPem, options.KeyId);

    PrintResult(new
    {
        snapshot = Path.GetFullPath(options.Snapshot!),
        releaseVersion = snapshot.Manifest.ReleaseVersion,
        contentHash = snapshot.Manifest.ContentHash,
        valid = verification.IsValid,
        error = verification.Error,
        signature = snapshot.Manifest.Signature
    });
    return verification.IsValid ? 0 : 1;
}

static ContentToolOptions ParseOptions(
    string[] args,
    bool requireSnapshot,
    bool requireKey,
    bool requireKeyId)
{
    string? snapshot = null;
    string? key = null;
    string? keyId = null;
    DateTimeOffset? signedAt = null;

    for (var index = 0; index < args.Length; index++)
    {
        var argument = args[index];
        switch (argument)
        {
            case "--snapshot":
                snapshot = NextValue(args, ref index, argument);
                break;
            case "--key":
            case "--private-key":
            case "--public-key":
                key = NextValue(args, ref index, argument);
                break;
            case "--key-id":
                keyId = NextValue(args, ref index, argument);
                break;
            case "--signed-at":
                signedAt = DateTimeOffset.Parse(
                    NextValue(args, ref index, argument),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                break;
            default:
                throw new ArgumentException($"Unknown argument: {argument}");
        }
    }

    if (requireSnapshot && snapshot is null)
    {
        throw new ArgumentException("--snapshot is required.");
    }

    if (requireKey && key is null)
    {
        throw new ArgumentException("--key is required.");
    }

    if (requireKeyId && string.IsNullOrWhiteSpace(keyId))
    {
        throw new ArgumentException("--key-id is required for signing.");
    }

    return new ContentToolOptions(snapshot, key, keyId, signedAt);
}

static string NextValue(string[] args, ref int index, string argument)
{
    if (index + 1 >= args.Length)
    {
        throw new ArgumentException($"Missing value for {argument}.");
    }

    index++;
    return args[index];
}

static void PrintResult(object result)
{
    Console.WriteLine(JsonSerializer.Serialize(result, ContentJson.IndentedOptions));
}

internal sealed record ContentToolOptions(
    string? Snapshot,
    string? Key,
    string? KeyId,
    DateTimeOffset? SignedAt);

internal static class HelpText
{
    public const string Value =
        """
        Usage:
          Crystal.ContentTool sign --snapshot <dir> --key <private-key.pem> --key-id <id> [--signed-at <timestamp>]
          Crystal.ContentTool verify --snapshot <dir> --public-key <public-key.pem> [--key-id <id>]

        Commands:
          sign    Sign a content snapshot manifest with an ECDSA P-256 private key.
          verify  Verify a signed snapshot and all entity/content hashes.

        Key format:
          PEM encoded ECDSA P-256 keys. Generate a pair with:
          openssl genpkey -algorithm EC -pkeyopt ec_paramgen_curve:P-256 -out private.pem
          openssl pkey -in private.pem -pubout -out public.pem
        """;
}
