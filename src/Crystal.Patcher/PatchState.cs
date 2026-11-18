using System.Text.Json;

namespace Crystal.Patcher;

public sealed record PatchState
{
    public int FormatVersion { get; init; } = 1;
    public string? ActiveRelease { get; init; }
    public string? PreviousRelease { get; init; }
    public DateTimeOffset? AppliedAt { get; init; }
    public DateTimeOffset? ManifestGeneratedAt { get; init; }
    public DateTimeOffset? PreviousManifestGeneratedAt { get; init; }
    public DateTimeOffset? PreviousAppliedAt { get; init; }
    public string? TransactionId { get; init; }
    public string? BackupDirectory { get; init; }
    public List<string> Added { get; init; } = new();
    public List<string> Replaced { get; init; } = new();
    public List<string> Deleted { get; init; } = new();
}

public sealed record PatchTransaction
{
    public int FormatVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string Release { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset ManifestGeneratedAt { get; init; }
    public string BackupDirectory { get; init; } = string.Empty;
    public string? PreviousRelease { get; init; }
    public DateTimeOffset? PreviousManifestGeneratedAt { get; init; }
    public DateTimeOffset? PreviousAppliedAt { get; init; }
    public List<string> Added { get; init; } = new();
    public List<string> Replaced { get; init; } = new();
    public List<string> Deleted { get; init; } = new();
}

public static class PatchStateStore
{
    public static string GetStateDirectory(string rootDirectory)
    {
        return Path.Combine(Path.GetFullPath(rootDirectory), PatchManifestFormat.StateDirectoryName);
    }

    public static string GetStatePath(string rootDirectory)
    {
        return Path.Combine(GetStateDirectory(rootDirectory), "state.json");
    }

    public static string GetPendingPath(string rootDirectory)
    {
        return Path.Combine(GetStateDirectory(rootDirectory), "pending.json");
    }

    public static string GetStagingDirectory(string rootDirectory)
    {
        return Path.Combine(GetStateDirectory(rootDirectory), "staging");
    }

    public static string GetBackupsDirectory(string rootDirectory)
    {
        return Path.Combine(GetStateDirectory(rootDirectory), "backups");
    }

    public static PatchState? Read(string rootDirectory)
    {
        return ReadJson<PatchState>(GetStatePath(rootDirectory));
    }

    public static void Write(string rootDirectory, PatchState state)
    {
        WriteJson(GetStatePath(rootDirectory), state);
    }

    public static void Delete(string rootDirectory)
    {
        var path = GetStatePath(rootDirectory);
        if (File.Exists(path)) File.Delete(path);
    }

    public static PatchTransaction? ReadPending(string rootDirectory)
    {
        return ReadJson<PatchTransaction>(GetPendingPath(rootDirectory));
    }

    public static void WritePending(string rootDirectory, PatchTransaction transaction)
    {
        WriteJson(GetPendingPath(rootDirectory), transaction);
    }

    public static void DeletePending(string rootDirectory)
    {
        var path = GetPendingPath(rootDirectory);
        if (File.Exists(path)) File.Delete(path);
    }

    private static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path)) return default;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, PatchJson.CompactOptions);
    }

    private static void WriteJson<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(value, PatchJson.IndentedOptions);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
