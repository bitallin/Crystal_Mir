using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Crystal.ZoneAgent;

internal sealed class ZoneProcessSupervisor
{
    private readonly AgentOptions _options;
    private readonly object _sync = new();
    private Process? _process;
    private ProcessLog? _log;
    private DateTimeOffset? _startedAt;
    private int? _lastExitCode;
    private string? _lastError;
    private string? _lastLogPath;

    public ZoneProcessSupervisor(AgentOptions options)
    {
        _options = options;
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public int? ProcessId
    {
        get
        {
            lock (_sync)
            {
                return _process is { HasExited: false } process ? process.Id : null;
            }
        }
    }

    public DateTimeOffset? StartedAt
    {
        get { lock (_sync) return _startedAt; }
    }

    public int? LastExitCode
    {
        get { lock (_sync) return _lastExitCode; }
    }

    public string? LastError
    {
        get { lock (_sync) return _lastError; }
    }

    public string? LastLogPath
    {
        get { lock (_sync) return _lastLogPath; }
    }

    public async Task StartAsync(StagedRelease release, CancellationToken cancellationToken)
    {
        await StopAsync(_options.StopTimeout, CancellationToken.None, reason: "replaced");

        DeleteReadyFile();
        Directory.CreateDirectory(_options.LogsDirectory);
        string logPath = Path.Combine(
            _options.LogsDirectory,
            $"zone-{release.ReleaseId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.log");
        var log = new ProcessLog(logPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ZoneExecutable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? "dotnet"
                : _options.ZoneExecutable,
            WorkingDirectory = _options.ContentRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (startInfo.FileName == "dotnet")
        {
            startInfo.ArgumentList.Add(_options.ZoneExecutable);
        }

        startInfo.ArgumentList.Add("--content-root");
        startInfo.ArgumentList.Add(_options.ContentRoot);
        startInfo.ArgumentList.Add("--content-release");
        startInfo.ArgumentList.Add(release.Directory);
        startInfo.ArgumentList.Add("--content-public-key");
        startInfo.ArgumentList.Add(_options.ContentPublicKeyPath);
        startInfo.ArgumentList.Add("--content-key-id");
        startInfo.ArgumentList.Add(_options.ContentKeyId);
        startInfo.ArgumentList.Add("--ready-file");
        startInfo.ArgumentList.Add(_options.ReadyFilePath);
        startInfo.ArgumentList.Add("--startup-timeout");
        startInfo.ArgumentList.Add(Math.Ceiling(_options.ReadyTimeout.TotalSeconds).ToString(
            System.Globalization.CultureInfo.InvariantCulture));

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => log.Write("OUT", args.Data);
        process.ErrorDataReceived += (_, args) => log.Write("ERR", args.Data);
        bool owned = true;

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Zone process did not start.");
            }

            lock (_sync)
            {
                _process = process;
                _log = log;
                _startedAt = DateTimeOffset.UtcNow;
                _lastLogPath = logPath;
                _lastError = null;
                owned = false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            DateTimeOffset deadline = DateTimeOffset.UtcNow + _options.ReadyTimeout;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryReadReadyFile(process.Id))
                {
                    log.Write("AGENT", $"Zone process {process.Id} is ready.");
                    return;
                }

                if (process.HasExited)
                {
                    int exitCode = process.ExitCode;
                    lock (_sync)
                    {
                        _lastExitCode = exitCode;
                        _lastError = $"Zone process exited before ready with code {exitCode}.";
                    }

                    throw new InvalidOperationException(
                        $"Zone process exited before ready (exit {exitCode}). See {logPath}.");
                }

                await Task.Delay(200, cancellationToken);
            }

            lock (_sync)
            {
                _lastError = "Zone process did not become ready before the timeout.";
            }

            throw new TimeoutException(
                $"Zone process did not become ready within {_options.ReadyTimeout.TotalSeconds:0} seconds. See {logPath}.");
        }
        catch
        {
            await StopAsync(_options.StopTimeout, CancellationToken.None, reason: "startup failed");
            if (owned)
            {
                log.Dispose();
                process.Dispose();
            }

            throw;
        }
    }

    public async Task StopAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string reason = "stopped")
    {
        Process? process;
        ProcessLog? log;
        lock (_sync)
        {
            process = _process;
            log = _log;
        }

        if (process is null)
        {
            DeleteReadyFile();
            log?.Dispose();
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                log?.Write("AGENT", $"Stopping zone process {process.Id}: {reason}.");
                SendTerminate(process);
                var wait = process.WaitForExitAsync(cancellationToken);
                var completed = await Task.WhenAny(wait, Task.Delay(timeout, cancellationToken));
                if (completed != wait)
                {
                    log?.Write("AGENT", $"Zone process {process.Id} did not stop in time; killing it.");
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }

            lock (_sync)
            {
                _lastExitCode = process.HasExited ? process.ExitCode : null;
            }
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                    _log = null;
                    _startedAt = null;
                }
            }

            try
            {
                process.CancelOutputRead();
                process.CancelErrorRead();
            }
            catch
            {
            }

            log?.Dispose();
            process.Dispose();
            DeleteReadyFile();
        }
    }

    public void RecordUnexpectedExit()
    {
        lock (_sync)
        {
            if (_process is { HasExited: true } process)
            {
                _lastExitCode = process.ExitCode;
                _lastError = $"Zone process exited unexpectedly with code {process.ExitCode}.";
            }
        }
    }

    public void ClearError()
    {
        lock (_sync)
        {
            _lastError = null;
        }
    }

    private bool TryReadReadyFile(int expectedProcessId)
    {
        if (!File.Exists(_options.ReadyFilePath)) return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_options.ReadyFilePath));
            if (!document.RootElement.TryGetProperty("pid", out var pid) ||
                pid.GetInt32() != expectedProcessId)
            {
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private void DeleteReadyFile()
    {
        try
        {
            if (File.Exists(_options.ReadyFilePath)) File.Delete(_options.ReadyFilePath);
        }
        catch
        {
        }
    }

    private static void SendTerminate(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            if (kill(process.Id, 15) == 0) return;
        }

        try
        {
            if (!process.CloseMainWindow())
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    private sealed class ProcessLog : IDisposable
    {
        private readonly object _sync = new();
        private readonly TextWriter _writer;

        public ProcessLog(string path)
        {
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = TextWriter.Synchronized(new StreamWriter(stream) { AutoFlush = true });
        }

        public void Write(string source, string? message)
        {
            if (message is null) return;
            lock (_sync)
            {
                _writer.WriteLine($"{DateTimeOffset.UtcNow:O} [{source}] {message}");
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _writer.Dispose();
            }
        }
    }
}
