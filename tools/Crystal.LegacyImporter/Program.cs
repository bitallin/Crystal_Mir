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
        var exportPath = Path.GetFullPath(options.ExportPath);
        if (exportPath == Path.GetFullPath(options.DatabasePath))
        {
            throw new InvalidOperationException("Export target must differ from the source database.");
        }

        LegacyDatabaseIdentity sourceIdentity;
        LegacyBinaryExportResult exportResult;
        string sourcePath;
        string sourceHash;
        int loadVersion;
        IReadOnlyList<ContentEntity> sourceEntities;

        using (var exportSession = LegacyDatabaseSession.Open(options.DatabasePath))
        {
            var exportMapped = LegacyContentMapper.Map(exportSession.Envir, exportSession);
            sourceEntities = exportMapped.Entities;
            sourceIdentity = LegacyBinaryExporter.Capture(exportSession.Envir, exportMapped.Entities);
            exportResult = LegacyBinaryExporter.Write(exportSession.Envir, exportPath, options.Force);
            sourcePath = exportSession.SourcePath;
            sourceHash = exportSession.SourceHash;
            loadVersion = Envir.LoadVersion;
        }

        LegacyDatabaseIdentity? reloadedIdentity = null;
        if (options.VerifyExport)
        {
            using var reloaded = LegacyDatabaseSession.Open(exportResult.Path);
            var reloadedMapped = LegacyContentMapper.Map(reloaded.Envir, reloaded);
            reloadedIdentity = LegacyBinaryExporter.Capture(reloaded.Envir, reloadedMapped.Entities);
            if (!sourceIdentity.SemanticallyEquals(reloadedIdentity))
            {
                var entityDifference = LegacyBinaryExporter.FindFirstEntityDifference(
                    sourceEntities,
                    reloadedMapped.Entities);
                throw new InvalidDataException(
                    $"Exported database failed semantic reload validation: source={sourceIdentity.SemanticHash}, " +
                    $"reloaded={reloadedIdentity.SemanticHash}, " +
                    $"entityComparison={entityDifference ?? "none"}, " +
                    $"sourceIndices={JsonSerializer.Serialize(sourceIdentity.Indices)}, " +
                    $"reloadedIndices={JsonSerializer.Serialize(reloadedIdentity.Indices)}, " +
                    $"sourceListCounts={JsonSerializer.Serialize(sourceIdentity.ListCounts)}, " +
                    $"reloadedListCounts={JsonSerializer.Serialize(reloadedIdentity.ListCounts)}.");
            }
        }

        var exportResultJson = new
        {
            mode = "export",
            database = sourcePath,
            databaseSha256 = sourceHash,
            loadVersion,
            output = exportResult.Path,
            outputSha256 = exportResult.Sha256,
            outputSizeBytes = exportResult.SizeBytes,
            outputVersion = Envir.Version,
            semanticHash = sourceIdentity.SemanticHash,
            counts = sourceIdentity.Counts,
            indices = sourceIdentity.Indices,
            listCounts = sourceIdentity.ListCounts,
            verified = options.VerifyExport ? true : (bool?)null,
            reloadedSemanticHash = reloadedIdentity?.SemanticHash
        };
        Console.WriteLine(JsonSerializer.Serialize(exportResultJson, ContentJson.IndentedOptions));
        return 0;
    }

    var outputDirectory = Path.GetFullPath(options.OutputDirectory);
    using var session = LegacyDatabaseSession.Open(options.DatabasePath);
    var mapped = LegacyContentMapper.Map(session.Envir, session);

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

internal sealed class ImportOptions
{
    public required string DatabasePath { get; init; }

    public required string OutputDirectory { get; init; }

    public required string ReleaseVersion { get; init; }

    public string? Source { get; init; }

    public string? SchemaPath { get; init; }

    public string? DatabaseConnectionString { get; init; }

    public string? ExportPath { get; init; }

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
          --dsn <connection>   PostgreSQL connection string or postgres:// URI
          --schema <path>      Apply one migration SQL instead of db/migrations/*.sql
          --export <path>      Export the loaded legacy database instead of writing a snapshot
          --no-verify           Skip reloading and comparing an exported database
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
        string? dsn = null;
        string? schema = null;
        string? export = null;
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
                case "--dsn":
                    dsn = NextValue(args, ref index, argument);
                    break;
                case "--schema":
                    schema = NextValue(args, ref index, argument);
                    break;
                case "--export":
                    export = NextValue(args, ref index, argument);
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
            (outputWasSpecified || releaseWasSpecified || source is not null || dsn is not null || schema is not null))
        {
            throw new ArgumentException(
                "--export cannot be combined with --out, --release, --source, --dsn, or --schema.");
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
            SchemaPath = schema,
            DatabaseConnectionString = dsn,
            ExportPath = export,
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
