using System.Text.Json.Serialization;

namespace Crystal.Patcher;

public enum PatchActionKind
{
    Add,
    Replace,
    Delete
}

public sealed record PatchAction(PatchActionKind Kind, string Path, ClientPatchFile? File)
{
    [JsonIgnore]
    public long Size => File?.Size ?? 0;
}

public sealed record PatchPlan(IReadOnlyList<PatchAction> Actions)
{
    public long DownloadBytes => Actions
        .Where(action => action.Kind is PatchActionKind.Add or PatchActionKind.Replace)
        .Sum(action => action.Size);

    public int AddedCount => Actions.Count(action => action.Kind == PatchActionKind.Add);
    public int ReplacedCount => Actions.Count(action => action.Kind == PatchActionKind.Replace);
    public int DeletedCount => Actions.Count(action => action.Kind == PatchActionKind.Delete);
    public bool IsEmpty => Actions.Count == 0;
}

public static class PatchPlanner
{
    public static PatchPlan Create(
        ClientPatchManifest manifest,
        IReadOnlyDictionary<string, LocalPatchFile> localFiles)
    {
        var actions = new List<PatchAction>();
        foreach (var file in manifest.Files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (!PatchPath.TryNormalize(file.Path, out var path, out var error))
            {
                throw new InvalidDataException(error);
            }

            if (!localFiles.TryGetValue(path, out var local))
            {
                actions.Add(new PatchAction(PatchActionKind.Add, path, file));
                continue;
            }

            if (local.Size != file.Size ||
                !string.Equals(local.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                actions.Add(new PatchAction(PatchActionKind.Replace, path, file));
            }
        }

        foreach (var deleted in manifest.Deleted.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!PatchPath.TryNormalize(deleted, out var path, out var error))
            {
                throw new InvalidDataException(error);
            }

            if (localFiles.ContainsKey(path))
            {
                actions.Add(new PatchAction(PatchActionKind.Delete, path, null));
            }
        }

        return new PatchPlan(actions);
    }
}
