using System.Reflection;
using System.Runtime.InteropServices;
using log4net;
using log4net.Config;
using log4net.Repository;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Crystal.ZoneServer;

internal static class Program
{
    private const int ShutdownTimeoutSeconds = 30;

    private static ILog _log = LogManager.GetLogger(typeof(Program));

    public static async Task<int> Main(string[] args)
    {
        ZoneHostOptions options;

        try
        {
            options = ZoneHostOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        if (!Directory.Exists(options.ContentRoot))
        {
            Console.Error.WriteLine($"Content root does not exist: {options.ContentRoot}");
            return 2;
        }

        Directory.SetCurrentDirectory(options.ContentRoot);
        ConfigureLogging(options);

        try
        {
            Packet.IsServer = true;
            Settings.Load();
            ApplySettingsOverrides(_log);

            string databasePath = Path.Combine(Environment.CurrentDirectory, "Server.MirDB");
            if (!File.Exists(databasePath))
            {
                _log.Error($"Database not found: {databasePath}");
                return 2;
            }

            _log.Info($"Content root: {Environment.CurrentDirectory}");
            _log.Info($"Database: {databasePath}");
            _log.Info($"Listen: {Settings.IPAddress}:{Settings.Port}; status port: {Settings.StatusPort}");

            if (options.CheckOnly)
                return RunContentCheck();

            using var shutdown = new CancellationTokenSource();
            using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                shutdown.Cancel();
            });
            using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
            {
                context.Cancel = true;
                shutdown.Cancel();
            });

            _log.Info("Starting zone server...");
            Envir.Main.Start();

            if (!await WaitForReadyAsync(options.StartupTimeout, shutdown.Token))
            {
                _log.Error("Zone server did not become ready before the startup timeout.");
                return 1;
            }

            _log.Info(
                $"Zone ready: maps={Envir.Main.MapInfoList.Count}, mapsLoaded={Envir.Main.MapList.Count}, " +
                $"items={Envir.Main.ItemInfoList.Count}, monsters={Envir.Main.MonsterInfoList.Count}, " +
                $"npcs={Envir.Main.NPCInfoList.Count}, quests={Envir.Main.QuestInfoList.Count}");

            return await WaitForShutdownAsync(shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _log.Fatal("Zone server terminated unexpectedly.", ex);
            return 1;
        }
        finally
        {
            StopZone();
        }
    }

    private static async Task<bool> WaitForReadyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (Envir.Main.Ready) return true;
            if (!Envir.Main.Running) return false;

            try
            {
                await Task.Delay(200, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return Envir.Main.Ready;
    }

    private static async Task<int> WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!Envir.Main.Running)
                {
                    _log.Error("Zone server work loop stopped unexpectedly.");
                    return 1;
                }

                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }

        _log.Info("Shutdown requested.");
        return 0;
    }

    private static void StopZone()
    {
        if (!Envir.Main.Running && Envir.Main.Ready == false)
            return;

        try
        {
            _log.Info("Stopping zone server...");
            Task stopTask = Task.Run(Envir.Main.Stop);

            if (!stopTask.Wait(TimeSpan.FromSeconds(ShutdownTimeoutSeconds)))
                _log.Warn($"Zone server did not stop within {ShutdownTimeoutSeconds} seconds.");
            else
                _log.Info("Zone server stopped.");
        }
        catch (Exception ex)
        {
            _log.Error("Zone server shutdown failed.", ex);
        }
    }

    private static int RunContentCheck()
    {
        _log.Info("Checking database and content paths...");

        if (!Envir.Edit.LoadDB())
        {
            _log.Error("Database could not be loaded.");
            return 1;
        }

        List<string> missingMaps = new();
        foreach (MapInfo map in Envir.Edit.MapInfoList)
        {
            string path = ContentPath.Resolve(Path.Combine(Settings.MapPath, map.FileName + ".map"));
            if (!File.Exists(path)) missingMaps.Add(map.FileName);
        }

        List<string> missingNpcScripts = Envir.Edit.NPCInfoList
            .Select(npc => npc.FileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !File.Exists(ContentPath.Resolve(Path.Combine(Settings.NPCPath, name + ".txt"))))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<string> missingQuests = Envir.Edit.QuestInfoList
            .Select(quest => quest.FileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !File.Exists(ContentPath.Resolve(Path.Combine(Settings.QuestPath, name + ".txt"))))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _log.Info(
            $"Content check: maps={Envir.Edit.MapInfoList.Count}, items={Envir.Edit.ItemInfoList.Count}, " +
            $"monsters={Envir.Edit.MonsterInfoList.Count}, npcScripts={Envir.Edit.NPCInfoList.Count}, " +
            $"quests={Envir.Edit.QuestInfoList.Count}");

        LogMissing("map", missingMaps);
        LogMissing("NPC script", missingNpcScripts);
        LogMissing("quest", missingQuests);

        return missingMaps.Count == 0 && missingNpcScripts.Count == 0 && missingQuests.Count == 0 ? 0 : 1;
    }

    private static void LogMissing(string kind, List<string> missing)
    {
        if (missing.Count == 0)
        {
            _log.Info($"Content check: all {kind} references resolved.");
            return;
        }

        _log.Error($"Content check: {missing.Count} missing {kind} reference(s): {string.Join(", ", missing.Take(20))}");
    }

    private static void ApplySettingsOverrides(ILog log)
    {
        Settings.IPAddress = GetString("CRYSTAL_ZONE_IP", Settings.IPAddress);
        Settings.Port = GetUShort("CRYSTAL_ZONE_PORT", Settings.Port);
        Settings.StatusPort = GetUShort("CRYSTAL_ZONE_STATUS_PORT", Settings.StatusPort);
        Settings.MaxUser = GetUShort("CRYSTAL_ZONE_MAX_USER", Settings.MaxUser);
        Settings.MaxIP = GetUShort("CRYSTAL_ZONE_MAX_IP", Settings.MaxIP);
        Settings.ThreadLimit = Math.Max(1, GetInt("CRYSTAL_ZONE_THREADS", Settings.ThreadLimit));
        Settings.SaveDelay = Math.Max(1, GetInt("CRYSTAL_ZONE_SAVE_DELAY_MINUTES", Settings.SaveDelay));

        if (!System.Net.IPAddress.TryParse(Settings.IPAddress, out _))
            throw new ArgumentException($"CRYSTAL_ZONE_IP is not a valid IP address: {Settings.IPAddress}");

        if (Settings.Port == 0)
            throw new ArgumentException("CRYSTAL_ZONE_PORT must be between 1 and 65535.");

        if (Settings.StatusPort == 0)
            throw new ArgumentException("CRYSTAL_ZONE_STATUS_PORT must be between 1 and 65535.");

        if (Settings.Port == Settings.StatusPort)
            throw new ArgumentException("CRYSTAL_ZONE_PORT and CRYSTAL_ZONE_STATUS_PORT must be different.");

        log.Info($"Runtime overrides: threads={Settings.ThreadLimit}, maxUsers={Settings.MaxUser}");
    }

    private static string GetString(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int GetInt(string name, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }

    private static ushort GetUShort(string name, ushort fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return ushort.TryParse(value, out ushort parsed) ? parsed : fallback;
    }

    private static void ConfigureLogging(ZoneHostOptions options)
    {
        Assembly entryAssembly = Assembly.GetEntryAssembly() ?? typeof(Program).Assembly;
        ILoggerRepository repository = LogManager.GetRepository(entryAssembly);
        GlobalContext.Properties["zone_id"] = GetString("CRYSTAL_ZONE_ID", "default");

        string configPath = options.LogConfigPath
            ?? new[]
            {
                Path.Combine(AppContext.BaseDirectory, "log4net.config"),
                Path.Combine(options.ContentRoot, "log4net.config")
            }.FirstOrDefault(File.Exists)
            ?? string.Empty;

        if (configPath.Length > 0)
            XmlConfigurator.Configure(repository, new FileInfo(configPath));
        else
            BasicConfigurator.Configure(repository);

        _log = LogManager.GetLogger(typeof(Program));
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            "Crystal Zone Server\n\n" +
            "Usage: Crystal.ZoneServer [options]\n\n" +
            "Options:\n" +
            "  --content-root <path>       Content/runtime directory (default: CRYSTAL_ZONE_HOME or cwd)\n" +
            "  --startup-timeout <seconds> Maximum time to wait for the zone to become ready (default: 300)\n" +
            "  --log-config <path>         Override log4net.config path\n" +
            "  --check                     Validate database and content file references, then exit\n" +
            "  -h, --help                  Show this help\n\n" +
            "Environment:\n" +
            "  CRYSTAL_ZONE_HOME, CRYSTAL_ZONE_ID, CRYSTAL_ZONE_IP, CRYSTAL_ZONE_PORT,\n" +
            "  CRYSTAL_ZONE_STATUS_PORT, CRYSTAL_ZONE_MAX_USER, CRYSTAL_ZONE_MAX_IP,\n" +
            "  CRYSTAL_ZONE_THREADS, CRYSTAL_ZONE_SAVE_DELAY_MINUTES");
    }
}

internal sealed class ZoneHostOptions
{
    public string ContentRoot { get; private init; } = string.Empty;
    public TimeSpan StartupTimeout { get; private init; } = TimeSpan.FromMinutes(5);
    public string? LogConfigPath { get; private init; }
    public bool CheckOnly { get; private init; }
    public bool ShowHelp { get; private init; }

    public static ZoneHostOptions Parse(string[] args)
    {
        string contentRoot = Environment.GetEnvironmentVariable("CRYSTAL_ZONE_HOME")?.Trim() ?? string.Empty;
        int timeoutSeconds = GetInt("CRYSTAL_ZONE_STARTUP_TIMEOUT_SECONDS", 300);
        string? logConfig = null;
        bool checkOnly = false;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                case "--check":
                    checkOnly = true;
                    break;
                case "--content-root":
                    contentRoot = RequireValue(args, ref i, arg);
                    break;
                case "--startup-timeout":
                    timeoutSeconds = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "--log-config":
                    logConfig = RequireValue(args, ref i, arg);
                    break;
                default:
                    if (arg.StartsWith("--content-root=", StringComparison.Ordinal))
                        contentRoot = arg.Substring("--content-root=".Length);
                    else if (arg.StartsWith("--startup-timeout=", StringComparison.Ordinal))
                        timeoutSeconds = ParsePositiveInt(arg.Substring("--startup-timeout=".Length), "--startup-timeout");
                    else if (arg.StartsWith("--log-config=", StringComparison.Ordinal))
                        logConfig = arg.Substring("--log-config=".Length);
                    else
                        throw new ArgumentException($"Unknown argument: {arg}");
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(contentRoot))
            contentRoot = Environment.CurrentDirectory;

        if (timeoutSeconds <= 0 || timeoutSeconds > 3600)
            throw new ArgumentException("Startup timeout must be between 1 and 3600 seconds.");

        if (logConfig != null && !File.Exists(logConfig))
            throw new ArgumentException($"Log config does not exist: {logConfig}");

        return new ZoneHostOptions
        {
            ContentRoot = Path.GetFullPath(contentRoot),
            StartupTimeout = TimeSpan.FromSeconds(timeoutSeconds),
            LogConfigPath = logConfig == null ? null : Path.GetFullPath(logConfig),
            CheckOnly = checkOnly,
            ShowHelp = showHelp
        };
    }

    private static string RequireValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{name} requires a value.");

        return args[++index];
    }

    private static int ParsePositiveInt(string value, string name)
    {
        if (!int.TryParse(value, out int parsed) || parsed <= 0)
            throw new ArgumentException($"{name} requires a positive integer.");

        return parsed;
    }

    private static int GetInt(string name, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }
}
