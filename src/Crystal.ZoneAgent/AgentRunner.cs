using System.Text.Json;
using Crystal.Agent.Contracts;
using Crystal.GameCore;

namespace Crystal.ZoneAgent;

internal sealed class ActivationFailedException : Exception
{
    public ActivationFailedException(string message, bool restoredPrevious, string? rollbackError = null)
        : base(message)
    {
        RestoredPrevious = restoredPrevious;
        RollbackError = rollbackError;
    }

    public bool RestoredPrevious { get; }
    public string? RollbackError { get; }
}

internal sealed class AgentRunner : IDisposable
{
    private static readonly string[] Capabilities =
    [
        AgentCommandTypes.StageContent,
        AgentCommandTypes.ActivateContent,
        AgentCommandTypes.RollbackContent,
        AgentCommandTypes.ReloadContent,
        AgentCommandTypes.StopZone,
        "process-supervisor"
    ];

    private readonly AgentOptions _options;
    private readonly ControlApiClient _client;
    private readonly AgentStateStore _state;
    private readonly ContentStager _stager;
    private readonly ZoneProcessSupervisor _supervisor;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _activeSync = new();
    private readonly DateTimeOffset _agentStartedAt = DateTimeOffset.UtcNow;
    private ActiveReleaseState? _active;
    private volatile bool _suppressRestart;
    private int _restartFailures;
    private int _metricsMisses;

    public AgentRunner(AgentOptions options)
    {
        _options = options;
        _client = new ControlApiClient(options);
        _state = new AgentStateStore(options);
        _stager = new ContentStager(options, _client);
        _supervisor = new ZoneProcessSupervisor(options);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await RegisterWithRetryAsync(cancellationToken);
        await RestoreActiveReleaseAsync(cancellationToken);

        var heartbeat = HeartbeatLoopAsync(cancellationToken);
        var commands = CommandLoopAsync(cancellationToken);
        var monitor = MonitorLoopAsync(cancellationToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _suppressRestart = true;
            try
            {
                await SendHeartbeatAsync(CancellationToken.None, AgentStatuses.Degraded);
            }
            catch
            {
            }

            await _supervisor.StopAsync(_options.StopTimeout, CancellationToken.None, "agent shutdown");
            try
            {
                await Task.WhenAll(heartbeat, commands, monitor);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RegisterWithRetryAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var zone = await _client.RegisterAsync(
                    new AgentRegistrationRequest(
                        _options.ZoneId,
                        _options.ZoneName,
                        _options.AgentId,
                        _options.AgentVersion,
                        AgentContractVersion.Current,
                        AgentContractVersion.Current,
                        _agentStartedAt,
                        Capabilities),
                    cancellationToken);
                Log($"Registered zone {zone.ZoneId} ({zone.ZoneName}) as {_options.AgentId}.");
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogError($"Registration failed: {exception.Message}. Retrying in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task RestoreActiveReleaseAsync(CancellationToken cancellationToken)
    {
        var active = await _state.LoadActiveAsync(cancellationToken);
        if (active is null)
        {
            Log("No persisted active release. Zone starts degraded and waits for an activate command.");
            return;
        }

        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            var staged = await _stager.VerifyAsync(active, cancellationToken);
            await _supervisor.StartAsync(staged, cancellationToken);
            SetActive(active);
            Log($"Restored active release {active.ReleaseId} ({active.Version}) from {active.ReleaseDirectory}.");
        }
        catch (Exception exception)
        {
            SetActive(null);
            _supervisor.RecordUnexpectedExit();
            LogError($"Could not restore active release {active.ReleaseId}: {exception.Message}");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatAsync(cancellationToken, _supervisor.IsRunning
                    ? AgentStatuses.Online
                    : AgentStatuses.Degraded);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ControlApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                LogError("Heartbeat rejected because the zone is not registered; re-registering.");
                try
                {
                    await RegisterWithRetryAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (Exception exception)
            {
                LogError($"Heartbeat failed: {exception.Message}");
            }

            try
            {
                await Task.Delay(_options.HeartbeatInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken cancellationToken, string status)
    {
        var active = GetActive();
        int? pid = _supervisor.ProcessId;
        RuntimeMetrics runtimeMetrics = ReadRuntimeMetrics(pid);
        if (runtimeMetrics.IsValid)
        {
            _metricsMisses = 0;
        }
        else if (pid is null)
        {
            _metricsMisses = 0;
        }
        else if (pid is not null)
        {
            _metricsMisses++;
            if (_metricsMisses >= 3) status = AgentStatuses.Degraded;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["metricsSource"] = runtimeMetrics.Source,
            ["restartFailures"] = _restartFailures.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (pid is not null) metadata["zonePid"] = pid.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (runtimeMetrics.Metrics is { } metrics)
        {
            metadata["metricsObservedAt"] = metrics.ObservedAt.ToString("O");
            metadata["metricsWindowSeconds"] = metrics.WindowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["metricsSampleCount"] = metrics.SampleCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (!string.IsNullOrWhiteSpace(runtimeMetrics.Error)) metadata["metricsError"] = runtimeMetrics.Error;
        if (active is not null)
        {
            metadata["activeReleaseDirectory"] = active.ReleaseDirectory;
            if (active.PreviousReleaseId is long previous)
            {
                metadata["previousReleaseId"] = previous.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        if (_supervisor.LastExitCode is int exitCode)
        {
            metadata["lastExitCode"] = exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(_supervisor.LastError)) metadata["lastError"] = _supervisor.LastError;
        if (!string.IsNullOrWhiteSpace(_supervisor.LastLogPath)) metadata["lastLogPath"] = _supervisor.LastLogPath;

        await _client.HeartbeatAsync(
            new AgentHeartbeatRequest(
                _options.ZoneId,
                _options.AgentId,
                DateTimeOffset.UtcNow,
                status,
                active?.ReleaseId,
                active?.ContentHash,
                PlayerCount: runtimeMetrics.Metrics?.PlayerCount ?? 0,
                TickP95Ms: runtimeMetrics.Metrics?.TickP95Ms ?? 0,
                ErrorRate: runtimeMetrics.Metrics?.ErrorRate ?? 0,
                Metadata: metadata),
            cancellationToken);
    }

    private RuntimeMetrics ReadRuntimeMetrics(int? pid)
    {
        if (pid is null) return new RuntimeMetrics("process-stopped", null, null);
        if (!File.Exists(_options.MetricsFilePath))
        {
            return new RuntimeMetrics("unavailable", null, "Metrics file is missing.");
        }

        try
        {
            string json = File.ReadAllText(_options.MetricsFilePath);
            bool valid = ZoneRuntimeMetricsJson.TryParse(
                json,
                _options.ZoneId,
                pid.Value,
                TimeSpan.FromSeconds(15),
                DateTimeOffset.UtcNow,
                out ZoneRuntimeMetrics? metrics,
                out string? error);
            return valid
                ? new RuntimeMetrics("zone-runtime", metrics, null)
                : new RuntimeMetrics("unavailable", null, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new RuntimeMetrics("unavailable", null, exception.Message);
        }
    }

    private sealed record RuntimeMetrics(
        string Source,
        ZoneRuntimeMetrics? Metrics,
        string? Error)
    {
        public bool IsValid => Metrics is not null;
    }

    private async Task CommandLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await FlushPendingResultsAsync(cancellationToken);
                var commands = await _client.ClaimCommandsAsync(
                    limit: 1,
                    _options.CommandLeaseSeconds,
                    cancellationToken);
                foreach (var command in commands)
                {
                    await HandleCommandAsync(command, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                LogError($"Command loop failed: {exception.Message}");
            }

            try
            {
                await Task.Delay(_options.PollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task HandleCommandAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        var cached = await _state.GetCommandResultAsync(command.CommandId, cancellationToken);
        if (cached is not null)
        {
            Log($"Command {command.CommandId} was already executed; re-posting cached result.");
            await PostCommandResultAsync(cached.Result, cancellationToken);
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        AgentCommandResult result;
        if (command.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            result = new AgentCommandResult(
                command.CommandId,
                _options.ZoneId,
                AgentStatuses.Rejected,
                startedAt,
                DateTimeOffset.UtcNow,
                "Command expired before execution.",
                null);
        }
        else
        {
            try
            {
                result = await ExecuteCommandAsync(command, startedAt, cancellationToken);
            }
            catch (ActivationFailedException exception)
            {
                result = new AgentCommandResult(
                    command.CommandId,
                    _options.ZoneId,
                    AgentStatuses.Failed,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    exception.Message,
                    JsonSerializer.SerializeToElement(new
                    {
                        restoredPrevious = exception.RestoredPrevious,
                        rollbackError = exception.RollbackError
                    }, AgentContractJson.Options));
            }
            catch (Exception exception)
            {
                result = new AgentCommandResult(
                    command.CommandId,
                    _options.ZoneId,
                    AgentStatuses.Failed,
                    startedAt,
                    DateTimeOffset.UtcNow,
                    exception.Message,
                    null);
            }
        }

        await _state.SaveCommandResultAsync(result, cancellationToken);
        await PostCommandResultAsync(result, cancellationToken);
    }

    private async Task<AgentCommandResult> ExecuteCommandAsync(
        AgentCommand command,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        switch (command.Type)
        {
            case AgentCommandTypes.StageContent:
            {
                long releaseId = ReadReleaseId(command);
                var release = await _client.GetReleaseAsync(releaseId, cancellationToken);
                var staged = await _stager.StageAsync(release, cancellationToken);
                return Succeeded(command, startedAt, new
                {
                    releaseId = staged.ReleaseId,
                    version = staged.Version,
                    contentHash = staged.ContentHash,
                    stagedDirectory = staged.Directory,
                    mode = "staged"
                });
            }
            case AgentCommandTypes.ActivateContent:
            case AgentCommandTypes.RollbackContent:
            {
                long releaseId = ReadReleaseId(command);
                var release = await _client.GetReleaseAsync(releaseId, cancellationToken);
                var staged = await _stager.StageAsync(release, cancellationToken);
                var previous = GetActive();
                var active = await StartStagedReleaseAsync(
                    staged,
                    previous,
                    preservePrevious: false,
                    cancellationToken);
                return Succeeded(command, startedAt, new
                {
                    releaseId = active.ReleaseId,
                    version = active.Version,
                    contentHash = active.ContentHash,
                    previousReleaseId = active.PreviousReleaseId,
                    activeDirectory = active.ReleaseDirectory,
                    mode = "process-restart"
                });
            }
            case AgentCommandTypes.ReloadContent:
            {
                var previous = GetActive()
                               ?? throw new InvalidOperationException("No active release is available to reload.");
                var staged = await _stager.VerifyAsync(previous, cancellationToken);
                var active = await StartStagedReleaseAsync(
                    staged,
                    previous,
                    preservePrevious: true,
                    cancellationToken);
                return Succeeded(command, startedAt, new
                {
                    releaseId = active.ReleaseId,
                    version = active.Version,
                    contentHash = active.ContentHash,
                    activeDirectory = active.ReleaseDirectory,
                    mode = "process-reload"
                });
            }
            case AgentCommandTypes.StopZone:
            {
                await _lifecycle.WaitAsync(cancellationToken);
                _suppressRestart = true;
                try
                {
                    await _supervisor.StopAsync(_options.StopTimeout, CancellationToken.None, "zone.stop command");
                    SetActive(null);
                    await _state.ClearActiveAsync(cancellationToken);
                }
                finally
                {
                    _suppressRestart = false;
                    _lifecycle.Release();
                }

                return Succeeded(command, startedAt, new { stopped = true });
            }
            default:
                throw new InvalidOperationException($"Unsupported command type '{command.Type}'.");
        }
    }

    private async Task<ActiveReleaseState> StartStagedReleaseAsync(
        StagedRelease staged,
        ActiveReleaseState? previous,
        bool preservePrevious,
        CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        _suppressRestart = true;
        try
        {
            try
            {
                await _supervisor.StartAsync(staged, cancellationToken);
            }
            catch (Exception candidateException)
            {
                string? rollbackError = null;
                bool restored = false;
                if (previous is not null)
                {
                    try
                    {
                        var previousStaged = await _stager.VerifyAsync(previous, CancellationToken.None);
                        await _supervisor.StartAsync(previousStaged, CancellationToken.None);
                        SetActive(previous);
                        restored = true;
                        Log($"Restored previous release {previous.ReleaseId} after candidate failure.");
                    }
                    catch (Exception restoreException)
                    {
                        rollbackError = restoreException.Message;
                        SetActive(null);
                        await _state.ClearActiveAsync(CancellationToken.None);
                        LogError($"Could not restore previous release {previous.ReleaseId}: {rollbackError}");
                    }
                }
                else
                {
                    SetActive(null);
                    await _state.ClearActiveAsync(CancellationToken.None);
                }

                throw new ActivationFailedException(
                    $"Candidate release {staged.ReleaseId} failed to start: {candidateException.Message}",
                    restored,
                    rollbackError);
            }

            var active = new ActiveReleaseState(
                staged.ReleaseId,
                staged.Version,
                staged.ContentHash,
                staged.Directory,
                DateTimeOffset.UtcNow,
                previous?.ReleaseId,
                previous?.Version,
                previous?.ContentHash,
                previous?.ReleaseDirectory);
            if (preservePrevious && previous is not null)
            {
                active = active with
                {
                    PreviousReleaseId = previous.PreviousReleaseId,
                    PreviousVersion = previous.PreviousVersion,
                    PreviousContentHash = previous.PreviousContentHash,
                    PreviousReleaseDirectory = previous.PreviousReleaseDirectory
                };
            }

            await _state.SaveActiveAsync(active, cancellationToken);
            SetActive(active);
            _restartFailures = 0;
            _supervisor.ClearError();
            Log($"Activated release {active.ReleaseId} ({active.Version}) with content hash {active.ContentHash}.");
            return active;
        }
        finally
        {
            _suppressRestart = false;
            _lifecycle.Release();
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                if (_suppressRestart || _supervisor.IsRunning) continue;
                var active = GetActive();
                if (active is null) continue;

                await _lifecycle.WaitAsync(cancellationToken);
                try
                {
                    if (_suppressRestart || _supervisor.IsRunning) continue;
                    _supervisor.RecordUnexpectedExit();
                    _restartFailures++;
                    int delaySeconds = _restartFailures switch
                    {
                        1 => 2,
                        2 => 5,
                        3 => 10,
                        _ => 30
                    };
                    LogError(
                        $"Zone process is not running; restarting release {active.ReleaseId} " +
                        $"in {delaySeconds} seconds (attempt {_restartFailures}).");
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    var staged = await _stager.VerifyAsync(active, cancellationToken);
                    await _supervisor.StartAsync(staged, cancellationToken);
                    _supervisor.ClearError();
                    _restartFailures = 0;
                    Log($"Zone process restarted for release {active.ReleaseId}.");
                }
                finally
                {
                    _lifecycle.Release();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                LogError($"Zone process monitor failed: {exception.Message}");
            }
        }
    }

    private async Task FlushPendingResultsAsync(CancellationToken cancellationToken)
    {
        foreach (var pending in await _state.LoadPendingResultsAsync(cancellationToken))
        {
            await PostCommandResultAsync(pending.Result, cancellationToken);
        }
    }

    private async Task PostCommandResultAsync(
        AgentCommandResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await _client.CompleteCommandAsync(result, cancellationToken);
            await _state.MarkCommandResultPostedAsync(result.CommandId, cancellationToken);
            Log($"Command {result.CommandId} result reported: {result.Status}.");
        }
        catch (ControlApiException exception)
            when (exception.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Conflict)
        {
            await _state.MarkCommandResultPostedAsync(result.CommandId, cancellationToken);
            LogError($"Command {result.CommandId} result was not accepted: {exception.Message}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogError($"Could not report command {result.CommandId} result: {exception.Message}");
        }
    }

    private static long ReadReleaseId(AgentCommand command)
    {
        if (command.Payload is { ValueKind: JsonValueKind.Object } payload &&
            payload.TryGetProperty("releaseId", out var property) &&
            property.TryGetInt64(out long releaseId) &&
            releaseId > 0)
        {
            return releaseId;
        }

        throw new InvalidOperationException("Command payload is missing a positive releaseId.");
    }

    private static AgentCommandResult Succeeded(
        AgentCommand command,
        DateTimeOffset startedAt,
        object payload)
    {
        return new AgentCommandResult(
            command.CommandId,
            command.ZoneId,
            AgentStatuses.Succeeded,
            startedAt,
            DateTimeOffset.UtcNow,
            null,
            JsonSerializer.SerializeToElement(payload, AgentContractJson.Options));
    }

    private ActiveReleaseState? GetActive()
    {
        lock (_activeSync) return _active;
    }

    private void SetActive(ActiveReleaseState? active)
    {
        lock (_activeSync) _active = active;
    }

    private static void Log(string message)
    {
        Console.WriteLine($"{DateTimeOffset.UtcNow:O} [AGENT] {message}");
    }

    private static void LogError(string message)
    {
        Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} [AGENT] ERROR {message}");
    }

    public void Dispose()
    {
        _client.Dispose();
        _lifecycle.Dispose();
    }
}
