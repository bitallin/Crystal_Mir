using System.Text.Json;
using Crystal.Agent.Contracts;

namespace Crystal.ZoneAgent;

internal sealed record ActiveReleaseState(
    long ReleaseId,
    string Version,
    string ContentHash,
    string ReleaseDirectory,
    DateTimeOffset ActivatedAt,
    long? PreviousReleaseId,
    string? PreviousVersion,
    string? PreviousContentHash,
    string? PreviousReleaseDirectory);

internal sealed record StoredCommandResult(
    AgentCommandResult Result,
    DateTimeOffset? PostedAt);

internal sealed class AgentStateStore
{
    private readonly AgentOptions _options;

    public AgentStateStore(AgentOptions options)
    {
        _options = options;
        Directory.CreateDirectory(options.AgentHome);
        Directory.CreateDirectory(options.ReleasesDirectory);
        Directory.CreateDirectory(options.StagingDirectory);
        Directory.CreateDirectory(options.StateDirectory);
        Directory.CreateDirectory(options.LogsDirectory);
        Directory.CreateDirectory(options.CommandResultsDirectory);
    }

    public async Task<ActiveReleaseState?> LoadActiveAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_options.ActiveStatePath)) return null;
        try
        {
            return JsonSerializer.Deserialize<ActiveReleaseState>(
                await File.ReadAllTextAsync(_options.ActiveStatePath, cancellationToken),
                AgentContractJson.Options);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Console.Error.WriteLine($"Could not read active state: {exception.Message}");
            return null;
        }
    }

    public async Task SaveActiveAsync(ActiveReleaseState state, CancellationToken cancellationToken)
    {
        await WriteJsonAtomicAsync(_options.ActiveStatePath, state, cancellationToken);
    }

    public Task ClearActiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (File.Exists(_options.ActiveStatePath)) File.Delete(_options.ActiveStatePath);
        }
        catch (IOException)
        {
        }

        return Task.CompletedTask;
    }

    public async Task<StoredCommandResult?> GetCommandResultAsync(
        string commandId,
        CancellationToken cancellationToken)
    {
        var path = CommandResultPath(commandId);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<StoredCommandResult>(
                await File.ReadAllTextAsync(path, cancellationToken),
                AgentContractJson.Options);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Console.Error.WriteLine($"Could not read command result {commandId}: {exception.Message}");
            return null;
        }
    }

    public async Task SaveCommandResultAsync(
        AgentCommandResult result,
        CancellationToken cancellationToken)
    {
        await WriteJsonAtomicAsync(
            CommandResultPath(result.CommandId),
            new StoredCommandResult(result, null),
            cancellationToken);
    }

    public async Task MarkCommandResultPostedAsync(
        string commandId,
        CancellationToken cancellationToken)
    {
        var stored = await GetCommandResultAsync(commandId, cancellationToken);
        if (stored is null) return;
        await WriteJsonAtomicAsync(
            CommandResultPath(commandId),
            stored with { PostedAt = DateTimeOffset.UtcNow },
            cancellationToken);
    }

    public async Task<IReadOnlyList<StoredCommandResult>> LoadPendingResultsAsync(
        CancellationToken cancellationToken)
    {
        var results = new List<StoredCommandResult>();
        foreach (var path in Directory.EnumerateFiles(_options.CommandResultsDirectory, "*.json"))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredCommandResult>(
                    await File.ReadAllTextAsync(path, cancellationToken),
                    AgentContractJson.Options);
                if (stored is { PostedAt: null }) results.Add(stored);
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                Console.Error.WriteLine($"Could not read pending command result {path}: {exception.Message}");
            }
        }

        return results;
    }

    private string CommandResultPath(string commandId)
    {
        return Path.Combine(_options.CommandResultsDirectory, $"{commandId}.json");
    }

    private static async Task WriteJsonAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(value, AgentContractJson.Options) + "\n",
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }
}
