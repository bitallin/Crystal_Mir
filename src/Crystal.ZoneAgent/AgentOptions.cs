using System.Reflection;

namespace Crystal.ZoneAgent;

internal sealed class AgentOptions
{
    public required string ControlApiUrl { get; init; }
    public string? ApiKey { get; init; }
    public required string ZoneId { get; init; }
    public required string ZoneName { get; init; }
    public required string AgentId { get; init; }
    public required string AgentVersion { get; init; }
    public required string AgentHome { get; init; }
    public required string ZoneExecutable { get; init; }
    public required string ContentRoot { get; init; }
    public required string ContentPublicKeyPath { get; init; }
    public required string ContentKeyId { get; init; }
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ReadyTimeout { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(45);
    public int CommandLeaseSeconds { get; init; } = 60;
    public bool ShowHelp { get; init; }

    public string ReleasesDirectory => Path.Combine(AgentHome, "releases");
    public string StagingDirectory => Path.Combine(AgentHome, "staging");
    public string StateDirectory => Path.Combine(AgentHome, "state");
    public string LogsDirectory => Path.Combine(AgentHome, "logs");
    public string ActiveStatePath => Path.Combine(StateDirectory, "active.json");
    public string ReadyFilePath => Path.Combine(StateDirectory, "ready.json");
    public string MetricsFilePath => Path.Combine(StateDirectory, "metrics.json");
    public string CommandResultsDirectory => Path.Combine(StateDirectory, "command-results");

    public static AgentOptions Parse(string[] args)
    {
        string controlUrl = GetString("CRYSTAL_AGENT_CONTROL_URL", string.Empty);
        string? apiKey = GetString(
            "CRYSTAL_AGENT_API_KEY",
            GetString("CRYSTAL_CONTROL_API_KEY", string.Empty));
        string zoneId = GetString("CRYSTAL_ZONE_ID", string.Empty);
        string zoneName = GetString("CRYSTAL_ZONE_NAME", string.Empty);
        string agentId = GetString("CRYSTAL_AGENT_ID", $"{Environment.MachineName}-agent");
        string agentHome = GetString(
            "CRYSTAL_AGENT_HOME",
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "crystal",
                "zone-agent"));
        string zoneExecutable = GetString("CRYSTAL_AGENT_ZONE_EXECUTABLE", string.Empty);
        string contentRoot = GetString("CRYSTAL_ZONE_HOME", string.Empty);
        string contentPublicKey = GetString("CRYSTAL_ZONE_CONTENT_PUBLIC_KEY", string.Empty);
        string contentKeyId = GetString("CRYSTAL_ZONE_CONTENT_KEY_ID", string.Empty);
        int pollSeconds = GetInt("CRYSTAL_AGENT_POLL_SECONDS", 2);
        int heartbeatSeconds = GetInt("CRYSTAL_AGENT_HEARTBEAT_SECONDS", 5);
        int readyTimeoutSeconds = GetInt("CRYSTAL_AGENT_READY_TIMEOUT_SECONDS", 300);
        int stopTimeoutSeconds = GetInt("CRYSTAL_AGENT_STOP_TIMEOUT_SECONDS", 45);
        int leaseSeconds = GetInt("CRYSTAL_AGENT_LEASE_SECONDS", 60);
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--control-api":
                    controlUrl = RequireValue(args, ref i, arg);
                    break;
                case "--api-key":
                    apiKey = RequireValue(args, ref i, arg);
                    break;
                case "--zone-id":
                    zoneId = RequireValue(args, ref i, arg);
                    break;
                case "--zone-name":
                    zoneName = RequireValue(args, ref i, arg);
                    break;
                case "--agent-id":
                    agentId = RequireValue(args, ref i, arg);
                    break;
                case "--agent-home":
                    agentHome = RequireValue(args, ref i, arg);
                    break;
                case "--zone-executable":
                    zoneExecutable = RequireValue(args, ref i, arg);
                    break;
                case "--content-root":
                    contentRoot = RequireValue(args, ref i, arg);
                    break;
                case "--content-public-key":
                    contentPublicKey = RequireValue(args, ref i, arg);
                    break;
                case "--content-key-id":
                    contentKeyId = RequireValue(args, ref i, arg);
                    break;
                case "--poll-seconds":
                    pollSeconds = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "--heartbeat-seconds":
                    heartbeatSeconds = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "--ready-timeout-seconds":
                    readyTimeoutSeconds = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "--stop-timeout-seconds":
                    stopTimeoutSeconds = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "--lease-seconds":
                    leaseSeconds = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "-h":
                case "--help":
                    showHelp = true;
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal) && arg.Contains('='))
                    {
                        int split = arg.IndexOf('=');
                        var (name, value) = (arg[..split], arg[(split + 1)..]);
                        switch (name)
                        {
                            case "--control-api": controlUrl = value; break;
                            case "--api-key": apiKey = value; break;
                            case "--zone-id": zoneId = value; break;
                            case "--zone-name": zoneName = value; break;
                            case "--agent-id": agentId = value; break;
                            case "--agent-home": agentHome = value; break;
                            case "--zone-executable": zoneExecutable = value; break;
                            case "--content-root": contentRoot = value; break;
                            case "--content-public-key": contentPublicKey = value; break;
                            case "--content-key-id": contentKeyId = value; break;
                            case "--poll-seconds": pollSeconds = ParsePositiveInt(value, name); break;
                            case "--heartbeat-seconds": heartbeatSeconds = ParsePositiveInt(value, name); break;
                            case "--ready-timeout-seconds": readyTimeoutSeconds = ParsePositiveInt(value, name); break;
                            case "--stop-timeout-seconds": stopTimeoutSeconds = ParsePositiveInt(value, name); break;
                            case "--lease-seconds": leaseSeconds = ParsePositiveInt(value, name); break;
                            default: throw new ArgumentException($"Unknown argument: {arg}");
                        }
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown argument: {arg}");
                    }

                    break;
            }
        }

        if (showHelp)
        {
            return new AgentOptions
            {
                ControlApiUrl = "http://127.0.0.1:8080",
                ZoneId = "help",
                ZoneName = "help",
                AgentId = "help",
                AgentVersion = GetAgentVersion(),
                AgentHome = agentHome,
                ZoneExecutable = "help",
                ContentRoot = "help",
                ContentPublicKeyPath = "help",
                ContentKeyId = "help",
                ShowHelp = true
            };
        }

        if (string.IsNullOrWhiteSpace(controlUrl) ||
            !Uri.TryCreate(controlUrl, UriKind.Absolute, out var controlUri) ||
            controlUri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("--control-api or CRYSTAL_AGENT_CONTROL_URL must be an absolute HTTP(S) URL.");
        }

        if (!IsValidZoneId(zoneId))
        {
            throw new ArgumentException("--zone-id must match ^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$.");
        }

        if (string.IsNullOrWhiteSpace(zoneName)) zoneName = zoneId;
        if (string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("--agent-id is required.");
        if (string.IsNullOrWhiteSpace(zoneExecutable))
            throw new ArgumentException("--zone-executable or CRYSTAL_AGENT_ZONE_EXECUTABLE is required.");
        if (string.IsNullOrWhiteSpace(contentRoot))
            throw new ArgumentException("--content-root or CRYSTAL_ZONE_HOME is required.");
        if (string.IsNullOrWhiteSpace(contentPublicKey))
            throw new ArgumentException("--content-public-key or CRYSTAL_ZONE_CONTENT_PUBLIC_KEY is required.");
        if (string.IsNullOrWhiteSpace(contentKeyId))
            throw new ArgumentException("--content-key-id or CRYSTAL_ZONE_CONTENT_KEY_ID is required.");

        string executable = Path.GetFullPath(zoneExecutable);
        string runtimeRoot = Path.GetFullPath(contentRoot);
        string publicKeyPath = Path.GetFullPath(contentPublicKey);
        if (!File.Exists(executable) && !Directory.Exists(executable))
            throw new ArgumentException($"Zone executable does not exist: {executable}");
        if (!Directory.Exists(runtimeRoot))
            throw new ArgumentException($"Content root does not exist: {runtimeRoot}");
        if (!File.Exists(publicKeyPath))
            throw new ArgumentException($"Content public key does not exist: {publicKeyPath}");
        if (pollSeconds is < 1 or > 300) throw new ArgumentException("poll seconds must be between 1 and 300.");
        if (heartbeatSeconds is < 1 or > 300) throw new ArgumentException("heartbeat seconds must be between 1 and 300.");
        if (readyTimeoutSeconds is < 1 or > 3600) throw new ArgumentException("ready timeout must be between 1 and 3600 seconds.");
        if (stopTimeoutSeconds is < 1 or > 300) throw new ArgumentException("stop timeout must be between 1 and 300 seconds.");
        if (leaseSeconds is < 5 or > 300) throw new ArgumentException("lease seconds must be between 5 and 300.");

        return new AgentOptions
        {
            ControlApiUrl = controlUri.ToString().TrimEnd('/'),
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(),
            ZoneId = zoneId.Trim(),
            ZoneName = zoneName.Trim(),
            AgentId = agentId.Trim(),
            AgentVersion = GetAgentVersion(),
            AgentHome = Path.GetFullPath(agentHome),
            ZoneExecutable = executable,
            ContentRoot = runtimeRoot,
            ContentPublicKeyPath = publicKeyPath,
            ContentKeyId = contentKeyId.Trim(),
            PollInterval = TimeSpan.FromSeconds(pollSeconds),
            HeartbeatInterval = TimeSpan.FromSeconds(heartbeatSeconds),
            ReadyTimeout = TimeSpan.FromSeconds(readyTimeoutSeconds),
            StopTimeout = TimeSpan.FromSeconds(stopTimeoutSeconds),
            CommandLeaseSeconds = leaseSeconds
        };
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            "Crystal Zone Agent\n\n" +
            "Usage: Crystal.ZoneAgent [options]\n\n" +
            "Options:\n" +
            "  --control-api <url>          Control API base URL\n" +
            "  --api-key <key>              Existing Control API service key (X-Api-Key)\n" +
            "  --zone-id <id>               Stable zone id\n" +
            "  --zone-name <name>           Display name (default: zone id)\n" +
            "  --agent-id <id>              Agent identity (default: <hostname>-agent)\n" +
            "  --agent-home <dir>           Agent state, releases and logs directory\n" +
            "  --zone-executable <path>     Crystal.ZoneServer executable or .dll\n" +
            "  --content-root <dir>         Runtime content root for Maps/Configs/Envir\n" +
            "  --content-public-key <path>  ECDSA P-256 release public key\n" +
            "  --content-key-id <id>        Expected release signing key id\n" +
            "  --poll-seconds <n>           Command poll interval (default: 2)\n" +
            "  --heartbeat-seconds <n>      Heartbeat interval (default: 5)\n" +
            "  --ready-timeout-seconds <n>  Candidate readiness timeout (default: 300)\n" +
            "  --stop-timeout-seconds <n>   Graceful stop timeout (default: 45)\n" +
            "  --lease-seconds <n>          Command lease (default: 60)\n" +
            "  -h, --help                   Show this help\n\n" +
            "Environment:\n" +
            "  CRYSTAL_AGENT_CONTROL_URL, CRYSTAL_AGENT_API_KEY, CRYSTAL_ZONE_ID,\n" +
            "  CRYSTAL_ZONE_NAME, CRYSTAL_AGENT_ID, CRYSTAL_AGENT_HOME,\n" +
            "  CRYSTAL_AGENT_ZONE_EXECUTABLE, CRYSTAL_ZONE_HOME,\n" +
            "  CRYSTAL_ZONE_CONTENT_PUBLIC_KEY, CRYSTAL_ZONE_CONTENT_KEY_ID");
    }

    private static string GetAgentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(AgentOptions).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "0.1.0";
    }

    private static bool IsValidZoneId(string value)
    {
        if (value.Length is < 1 or > 64) return false;
        if (value[0] is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')))
            return false;
        return value.All(character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-');
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

    private static string RequireValue(string[] args, ref int index, string name)
    {
        if (index + 1 >= args.Length) throw new ArgumentException($"{name} requires a value.");
        return args[++index];
    }

    private static int ParsePositiveInt(string value, string name)
    {
        if (!int.TryParse(value, out int parsed) || parsed <= 0)
            throw new ArgumentException($"{name} requires a positive integer.");
        return parsed;
    }
}
