using System.Globalization;
using System.Text.Json;
using Crystal.Patcher;

if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    Console.WriteLine(HelpText.Value);
    return 0;
}

try
{
    return args[0] switch
    {
        "pack" => Pack(args[1..]),
        "sign" => Sign(args[1..]),
        "verify" => Verify(args[1..]),
        "plan" => Plan(args[1..]),
        "apply" => await ApplyAsync(args[1..]),
        "rollback" => Rollback(args[1..]),
        "state" => ShowState(args[1..]),
        _ => throw new ArgumentException($"Unknown command: {args[0]}")
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int Pack(string[] args)
{
    var options = CliOptions.Parse(args);
    var root = options.Required("--root");
    var output = options.Required("--output");
    var channel = options.Required("--channel");
    var release = options.Required("--release");
    var urlPrefix = options.Required("--url-prefix");
    var generatedAt = options.DateTimeOffset("--generated-at");
    var expiresAt = options.DateTimeOffset("--expires-at");

    var manifest = PatchManifestBuilder.Build(new PatchBuildOptions
    {
        RootDirectory = root,
        Channel = channel,
        Release = release,
        UrlPrefix = urlPrefix,
        MinimumClientVersion = options.Optional("--min-client-version") ?? "0.0.0",
        MinimumLauncherVersion = options.Optional("--min-launcher-version") ?? "0.0.0",
        ProtocolVersion = options.Int32("--protocol-version") ?? 1,
        ContentSchemaVersion = options.Int32("--content-schema-version") ?? 1,
        GeneratedAt = generatedAt,
        ExpiresAt = expiresAt,
        Deleted = options.All("--delete").ToList(),
        ExcludedPrefixes = options.All("--exclude").ToList()
    });
    PatchManifestLoader.Write(output, manifest);
    Print(new
    {
        output = Path.GetFullPath(output),
        manifest.Channel,
        manifest.Release,
        files = manifest.Files.Count,
        deleted = manifest.Deleted.Count,
        manifest.GeneratedAt,
        manifest.ExpiresAt
    });
    return 0;
}

static int Sign(string[] args)
{
    var options = CliOptions.Parse(args);
    var manifestPath = options.Required("--manifest");
    var keyPath = options.Optional("--key") ?? options.Required("--private-key");
    var keyId = options.Required("--key-id");
    var signedAt = options.DateTimeOffset("--signed-at");

    var manifest = PatchManifestLoader.LoadFile(manifestPath).Manifest;
    var signed = PatchManifestSigner.Sign(
        manifest,
        File.ReadAllText(keyPath),
        keyId,
        signedAt);
    PatchManifestLoader.Write(manifestPath, signed);
    Print(new { manifest = Path.GetFullPath(manifestPath), signed.Signature });
    return 0;
}

static int Verify(string[] args)
{
    var options = CliOptions.Parse(args);
    var document = LoadDocument(options.Required("--manifest"));
    var publicKey = File.ReadAllText(options.Required("--public-key"));
    var validation = ValidateOptions(options);
    var result = PatchManifestValidator.Validate(document.Manifest, publicKey, validation);
    Print(new
    {
        manifest = document.Manifest.Release,
        document.Manifest.Channel,
        valid = result.IsValid,
        errors = result.Errors
    });
    return result.IsValid ? 0 : 1;
}

static int Plan(string[] args)
{
    var options = CliOptions.Parse(args);
    var root = Path.GetFullPath(options.Required("--root"));
    var document = LoadDocument(options.Required("--manifest"));
    var publicKey = File.ReadAllText(options.Required("--public-key"));
    var validation = ValidateOptions(options);
    var result = PatchManifestValidator.Validate(document.Manifest, publicKey, validation);
    if (!result.IsValid)
    {
        Print(new { valid = false, errors = result.Errors });
        return 1;
    }

    PatchInstaller.RecoverPending(root);
    var state = PatchStateStore.Read(root);
    var scan = PatchScanner.Scan(root);
    var plan = PatchPlanner.Create(document.Manifest, scan);
    Print(new
    {
        valid = true,
        state,
        plan.AddedCount,
        plan.ReplacedCount,
        plan.DeletedCount,
        plan.DownloadBytes,
        actions = plan.Actions
    });
    return 0;
}

static async Task<int> ApplyAsync(string[] args)
{
    var options = CliOptions.Parse(args);
    var root = Path.GetFullPath(options.Required("--root"));
    var document = await PatchManifestLoader.LoadAsync(options.Required("--manifest"));
    var publicKey = File.ReadAllText(options.Required("--public-key"));
    var baseUri = options.Optional("--manifest-base") is { } baseValue
        ? new Uri(baseValue, UriKind.Absolute)
        : document.BaseUri;
    var result = await PatchUpdateService.UpdateAsync(new PatchUpdateRequest
    {
        Manifest = document.Manifest,
        RootDirectory = root,
        PublicKeyPem = publicKey,
        ManifestBaseUri = baseUri,
        Validation = ValidateOptions(options),
        DryRun = options.Has("--dry-run")
    });
    Print(result);
    return 0;
}

static int Rollback(string[] args)
{
    var options = CliOptions.Parse(args);
    var root = Path.GetFullPath(options.Required("--root"));
    var result = PatchInstaller.Rollback(root);
    Print(result);
    return 0;
}

static int ShowState(string[] args)
{
    var options = CliOptions.Parse(args);
    var root = Path.GetFullPath(options.Required("--root"));
    Print(new
    {
        state = PatchStateStore.Read(root),
        pending = PatchStateStore.ReadPending(root)
    });
    return 0;
}

static PatchManifestDocument LoadDocument(string location)
{
    return PatchManifestLoader.LoadAsync(location).GetAwaiter().GetResult();
}

static PatchValidationOptions ValidateOptions(CliOptions options)
{
    return new PatchValidationOptions
    {
        ExpectedChannel = options.Optional("--channel"),
        ExpectedKeyId = options.Optional("--key-id"),
        ClientVersion = options.Optional("--client-version") ?? "0.0.0",
        LauncherVersion = options.Optional("--launcher-version") ?? "0.0.0",
        ProtocolVersion = options.Int32("--protocol-version") ?? 1,
        ContentSchemaVersion = options.Int32("--content-schema-version") ?? 1,
        Now = options.DateTimeOffset("--now")
    };
}

static void Print(object value)
{
    Console.WriteLine(JsonSerializer.Serialize(value, PatchJson.IndentedOptions));
}

internal sealed class CliOptions
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{argument}'.");
            }

            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options.Add(argument, args[++index]);
            }
            else
            {
                options.Add(argument, "true");
            }
        }

        return options;
    }

    public string Required(string name)
    {
        return Optional(name) ?? throw new ArgumentException($"{name} is required.");
    }

    public string? Optional(string name)
    {
        return _values.TryGetValue(name, out var values) ? values[^1] : null;
    }

    public bool Has(string name)
    {
        return _values.ContainsKey(name);
    }

    public IReadOnlyList<string> All(string name)
    {
        return _values.TryGetValue(name, out var values) ? values : Array.Empty<string>();
    }

    public int? Int32(string name)
    {
        var value = Optional(name);
        return value is null ? null : int.Parse(value, CultureInfo.InvariantCulture);
    }

    public DateTimeOffset? DateTimeOffset(string name)
    {
        var value = Optional(name);
        return value is null
            ? null
            : System.DateTimeOffset.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private void Add(string name, string value)
    {
        if (!_values.TryGetValue(name, out var values))
        {
            values = new List<string>();
            _values[name] = values;
        }

        values.Add(value);
    }
}

internal static class HelpText
{
    public const string Value =
        """
        Usage:
          Crystal.PatcherTool pack --root <dir> --output <manifest.json> --channel <name> --release <id> --url-prefix <url>
              [--min-client-version <v>] [--min-launcher-version <v>] [--protocol-version <n>] [--content-schema-version <n>]
              [--generated-at <timestamp>] [--expires-at <timestamp>] [--delete <path>]... [--exclude <prefix>]...
          Crystal.PatcherTool sign --manifest <manifest.json> --key <private-key.pem> --key-id <id> [--signed-at <timestamp>]
          Crystal.PatcherTool verify --manifest <path-or-url> --public-key <public-key.pem> [--key-id <id>]
              [--channel <name>] [--client-version <v>] [--launcher-version <v>] [--protocol-version <n>]
              [--content-schema-version <n>] [--now <timestamp>]
          Crystal.PatcherTool plan --root <dir> --manifest <path-or-url> --public-key <public-key.pem> [version options]
          Crystal.PatcherTool apply --root <dir> --manifest <path-or-url> --public-key <public-key.pem> [version options]
              [--manifest-base <uri>] [--dry-run]
          Crystal.PatcherTool rollback --root <dir>
          Crystal.PatcherTool state --root <dir>

        Update state is stored under <root>/.crystal-update. Signed manifests are ECDSA P-256/SHA-256 and use camelCase JSON.
        """;
}
