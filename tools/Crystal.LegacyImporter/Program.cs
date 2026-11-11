using Server.MirEnvir;
using System.Text.Json;
using Crystal.Content;
using Crystal.LegacyImporter;

var options = ImportOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine(ImportOptions.HelpText);
    return 0;
}

try
{
    if (options.ExportPath is not null)
    {
        if (options.SnapshotPath is not null)
        {
            return ExportSnapshot(options);
        }

        if (options.PostgresSourceConnectionString is not null)
        {
            return await ExportPostgres(options);
        }

        return ExportDatabase(options);
    }

    if (options.SnapshotPath is not null || options.PostgresSourceConnectionString is not null)
    {
        throw new ArgumentException("--snapshot and --from-postgres can only be used together with --export.");
    }

    var outputDirectory = Path.GetFullPath(options.OutputDirectory);
    var contentRoot = options.ContentRoot is null ? null : Path.GetFullPath(options.ContentRoot);
    using var session = LegacyDatabaseSession.Open(options.DatabasePath);
    var mapped = LegacyContentMapper.Map(session.Envir, session, contentRoot);

    var actualCounts = mapped.Entities
        .GroupBy(entity => entity.EntityType, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    foreach (var (entityType, expected) in mapped.SourceCounts)
    {
        var actual = actualCounts.GetValueOrDefault(entityType);
        if (actual != expected)
        {
            throw new InvalidDataException(
                $"Imported entity count mismatch for '{entityType}': source={expected}, mapped={actual}.");
        }
    }

    var manifest = ContentSnapshotWriter.Write(
        new ContentSnapshotWriteOptions
        {
            OutputDirectory = outputDirectory,
            ReleaseVersion = options.ReleaseVersion,
            Source = options.Source ?? session.SourcePath,
            SourceHash = session.SourceHash,
            Generator = "Crystal.LegacyImporter/1",
            Legacy = mapped.Legacy,
            Force = options.Force
        },
        mapped.Entities);

    var snapshot = ContentSnapshotReader.Read(outputDirectory);
    var issues = ContentSnapshotValidator.Validate(snapshot);
    if (issues.Count > 0)
    {
        throw new InvalidDataException(
            "Snapshot validation failed: " + string.Join("; ", issues.Select(issue => issue.Message)));
    }

    long? postgresRunId = null;
    if (options.DatabaseConnectionString is not null)
    {
        var migrationPaths = options.SchemaPath is null
            ? ImportOptions.FindMigrationPaths()
            : [options.SchemaPath];
        postgresRunId = await PostgresContentStore.ImportAsync(
            options.DatabaseConnectionString,
            migrationPaths,
            snapshot);
    }

    var result = new
    {
        database = session.SourcePath,
        databaseSha256 = session.SourceHash,
        headerVersion = session.HeaderVersion,
        loadVersion = Envir.LoadVersion,
        output = outputDirectory,
        contentRoot,
        releaseVersion = manifest.ReleaseVersion,
        contentHash = manifest.ContentHash,
        counts = manifest.Counts,
        postgres = postgresRunId is null
            ? null
            : new { importRunId = postgresRunId.Value }
    };
    Console.WriteLine(JsonSerializer.Serialize(result, ContentJson.IndentedOptions));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}

static int ExportDatabase(ImportOptions options)
{
    var exportPath = Path.GetFullPath(options.ExportPath!);
    if (exportPath == Path.GetFullPath(options.DatabasePath))
    {
        throw new InvalidOperationException("Export target must differ from the source database.");
    }

    using var session = LegacyDatabaseSession.Open(options.DatabasePath);
    var mapped = LegacyContentMapper.Map(session.Envir, session);
    var verification = LegacyContentExportService.Export(
        session.Envir,
        mapped.Entities,
        exportPath,
        options.VerifyExport,
        options.Force);

    PrintExportReport(
        "export-db",
        options,
        verification,
        new Dictionary<string, object?>
        {
            ["database"] = session.SourcePath,
            ["databaseSha256"] = session.SourceHash,
            ["loadVersion"] = Envir.LoadVersion
        });
    return 0;
}

static int ExportSnapshot(ImportOptions options)
{
    var snapshotPath = Path.GetFullPath(options.SnapshotPath!);
    var snapshot = ContentSnapshotReader.Read(snapshotPath);
    using var environment = LegacyRebuildEnvironment.Open();
    var envir = LegacyContentRebuilder.Build(snapshot);
    var verification = LegacyContentExportService.Export(
        envir,
        snapshot.Entities,
        Path.GetFullPath(options.ExportPath!),
        options.VerifyExport,
        options.Force);

    PrintExportReport(
        "export-snapshot",
        options,
        verification,
        new Dictionary<string, object?>
        {
            ["snapshot"] = snapshotPath,
            ["releaseVersion"] = snapshot.Manifest.ReleaseVersion,
            ["contentHash"] = snapshot.Manifest.ContentHash,
            ["loadVersion"] = Envir.LoadVersion
        });
    return 0;
}

static async Task<int> ExportPostgres(ImportOptions options)
{
    var source = await PostgresContentReader.ReadLatestImportedAsync(
        options.PostgresSourceConnectionString!);
    using var environment = LegacyRebuildEnvironment.Open();
    var envir = LegacyContentRebuilder.Build(source.Snapshot);
    var verification = LegacyContentExportService.Export(
        envir,
        source.Snapshot.Entities,
        Path.GetFullPath(options.ExportPath!),
        options.VerifyExport,
        options.Force);

    PrintExportReport(
        "export-postgres",
        options,
        verification,
        new Dictionary<string, object?>
        {
            ["postgresImportRunId"] = source.ImportRunId,
            ["releaseVersion"] = source.Snapshot.Manifest.ReleaseVersion,
            ["contentHash"] = source.Snapshot.Manifest.ContentHash,
            ["loadVersion"] = Envir.LoadVersion
        });
    return 0;
}

static void PrintExportReport(
    string mode,
    ImportOptions options,
    LegacyContentExportVerification verification,
    IReadOnlyDictionary<string, object?> source)
{
    var report = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["mode"] = mode
    };
    foreach (var (key, value) in source)
    {
        report[key] = value;
    }

    report["output"] = verification.Output.Path;
    report["outputSha256"] = verification.Output.Sha256;
    report["outputSizeBytes"] = verification.Output.SizeBytes;
    report["outputVersion"] = Envir.Version;
    report["semanticHash"] = verification.Identity.SemanticHash;
    report["counts"] = verification.Identity.Counts;
    report["indices"] = verification.Identity.Indices;
    report["listCounts"] = verification.Identity.ListCounts;
    report["verified"] = options.VerifyExport ? true : null;
    report["reloadedSemanticHash"] = verification.ReloadedIdentity?.SemanticHash;
    Console.WriteLine(JsonSerializer.Serialize(report, ContentJson.IndentedOptions));
}

internal sealed class ImportOptions
{
    public required string DatabasePath { get; init; }

    public required string OutputDirectory { get; init; }

    public required string ReleaseVersion { get; init; }

    public string? Source { get; init; }

    public string? ContentRoot { get; init; }

    public string? SchemaPath { get; init; }

    public string? DatabaseConnectionString { get; init; }

    public string? ExportPath { get; init; }

    public string? SnapshotPath { get; init; }

    public string? PostgresSourceConnectionString { get; init; }

    public bool VerifyExport { get; init; } = true;

    public bool Force { get; init; }

    public bool ShowHelp { get; init; }

    public static string HelpText =>
        """
        Usage: Crystal.LegacyImporter [options]

          --db <path>          Legacy Server.MirDB (default: tests/fixtures/golden/Server.MirDB)
          --out <dir>          Snapshot output directory (default: artifacts/content/<UTC timestamp>)
          --release <version>  Content release version (default: UTC timestamp)
          --source <text>      Override manifest source label
          --content-root <dir> Import Envir/Drops from a legacy content root
          --dsn <connection>   PostgreSQL connection string or postgres:// URI
          --schema <path>      Apply one migration SQL instead of db/migrations/*.sql
          --export <path>      Export a legacy database instead of writing a snapshot
          --snapshot <dir>     Rebuild and export from a JSONL content snapshot
          --from-postgres <connection>
                               Rebuild and export from the latest imported PostgreSQL snapshot
          --no-verify          Skip reloading and comparing an exported database
          --force              Replace snapshot files in a non-empty output directory
                               or replace an existing export file
          --help               Show this help
        """;

    public static ImportOptions Parse(string[] args)
    {
        string? database = null;
        string? output = null;
        string? release = null;
        string? source = null;
        string? contentRoot = null;
        string? dsn = null;
        string? schema = null;
        string? export = null;
        string? snapshot = null;
        string? postgresSource = null;
        var databaseWasSpecified = false;
        var outputWasSpecified = false;
        var releaseWasSpecified = false;
        var verifyExport = true;
        var force = false;
        var help = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--db":
                    database = NextValue(args, ref index, argument);
                    databaseWasSpecified = true;
                    break;
                case "--out":
                    output = NextValue(args, ref index, argument);
                    outputWasSpecified = true;
                    break;
                case "--release":
                    release = NextValue(args, ref index, argument);
                    releaseWasSpecified = true;
                    break;
                case "--source":
                    source = NextValue(args, ref index, argument);
                    break;
                case "--content-root":
                    contentRoot = NextValue(args, ref index, argument);
                    break;
                case "--dsn":
                    dsn = NextValue(args, ref index, argument);
                    break;
                case "--schema":
                    schema = NextValue(args, ref index, argument);
                    break;
                case "--export":
                    export = NextValue(args, ref index, argument);
                    break;
                case "--snapshot":
                    snapshot = NextValue(args, ref index, argument);
                    break;
                case "--from-postgres":
                    postgresSource = NextValue(args, ref index, argument);
                    break;
                case "--no-verify":
                    verifyExport = false;
                    break;
                case "--force":
                    force = true;
                    break;
                case "--help":
                case "-h":
                    help = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {argument}");
            }
        }

        if (export is not null &&
            (outputWasSpecified || releaseWasSpecified || source is not null || contentRoot is not null ||
             dsn is not null || schema is not null))
        {
            throw new ArgumentException(
                "--export cannot be combined with --out, --release, --source, --content-root, --dsn, or --schema.");
        }

        if (snapshot is not null && postgresSource is not null)
        {
            throw new ArgumentException("--snapshot and --from-postgres are mutually exclusive.");
        }

        if ((snapshot is not null || postgresSource is not null) && export is null)
        {
            throw new ArgumentException(
                "--snapshot and --from-postgres require --export.");
        }

        if ((snapshot is not null || postgresSource is not null) && databaseWasSpecified)
        {
            throw new ArgumentException(
                "--snapshot/--from-postgres cannot be combined with --db.");
        }

        if (export is null && !verifyExport)
        {
            throw new ArgumentException("--no-verify can only be used with --export.");
        }

        var timestamp = DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss");
        var defaultDatabase = File.Exists("tests/fixtures/golden/Server.MirDB")
            ? "tests/fixtures/golden/Server.MirDB"
            : "Server.MirDB";

        return new ImportOptions
        {
            DatabasePath = database ?? defaultDatabase,
            OutputDirectory = output ?? Path.Combine("artifacts", "content", timestamp),
            ReleaseVersion = release ?? timestamp,
            Source = source,
            ContentRoot = contentRoot,
            SchemaPath = schema,
            DatabaseConnectionString = dsn,
            ExportPath = export,
            SnapshotPath = snapshot,
            PostgresSourceConnectionString = postgresSource,
            VerifyExport = verifyExport,
            Force = force,
            ShowHelp = help
        };
    }

    public static IReadOnlyList<string> FindMigrationPaths()
    {
        const string relativePath = "db/migrations";
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (Directory.Exists(candidate))
                {
                    var paths = Directory.GetFiles(candidate, "*.sql")
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    if (paths.Length > 0) return paths;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException(
            $"Schema migrations not found. Expected SQL files in '{relativePath}' under the repository or current directory.");
    }

    private static string NextValue(string[] args, ref int index, string argument)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {argument}.");
        }

        index++;
        return args[index];
    }
}
