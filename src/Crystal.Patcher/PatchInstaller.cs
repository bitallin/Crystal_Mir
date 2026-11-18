namespace Crystal.Patcher;

public sealed record PatchApplyResult(
    string Release,
    int Added,
    int Replaced,
    int Deleted,
    string? BackupDirectory);

public sealed record PatchRollbackResult(string? RestoredRelease, int Restored, int Deleted);

public static class PatchInstaller
{
    public static bool RecoverPending(string rootDirectory)
    {
        var transaction = PatchStateStore.ReadPending(rootDirectory);
        if (transaction is null) return false;

        var state = PatchStateStore.Read(rootDirectory);
        if (state is not null &&
            string.Equals(state.TransactionId, transaction.Id, StringComparison.Ordinal))
        {
            PatchStateStore.DeletePending(rootDirectory);
            return false;
        }

        Restore(rootDirectory, transaction);
        PatchStateStore.DeletePending(rootDirectory);
        WritePreviousState(rootDirectory, transaction);
        TryDeleteDirectory(transaction.BackupDirectory);
        return true;
    }

    public static PatchApplyResult Apply(
        string rootDirectory,
        ClientPatchManifest manifest,
        PatchPlan plan,
        string stagingDirectory,
        PatchState? previousState,
        DateTimeOffset appliedAt)
    {
        RecoverPending(rootDirectory);

        var transactionId = Guid.NewGuid().ToString("N");
        var backupDirectory = Path.Combine(
            PatchStateStore.GetBackupsDirectory(rootDirectory),
            transactionId);
        var added = plan.Actions
            .Where(action => action.Kind == PatchActionKind.Add)
            .Select(action => action.Path)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var replaced = plan.Actions
            .Where(action => action.Kind == PatchActionKind.Replace)
            .Select(action => action.Path)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var deleted = plan.Actions
            .Where(action => action.Kind == PatchActionKind.Delete)
            .Select(action => action.Path)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var transaction = new PatchTransaction
        {
            Id = transactionId,
            Release = manifest.Release,
            StartedAt = appliedAt,
            ManifestGeneratedAt = manifest.GeneratedAt,
            BackupDirectory = backupDirectory,
            PreviousRelease = previousState?.ActiveRelease,
            PreviousManifestGeneratedAt = previousState?.ManifestGeneratedAt,
            PreviousAppliedAt = previousState?.AppliedAt,
            Added = added,
            Replaced = replaced,
            Deleted = deleted
        };

        Directory.CreateDirectory(backupDirectory);
        PatchStateStore.WritePending(rootDirectory, transaction);

        try
        {
            foreach (var path in replaced.Concat(deleted))
            {
                BackupExistingFile(rootDirectory, backupDirectory, path);
            }

            foreach (var action in plan.Actions.Where(action =>
                         action.Kind is PatchActionKind.Add or PatchActionKind.Replace))
            {
                var destination = PatchPath.ResolveUnder(rootDirectory, action.Path);
                if (File.Exists(destination))
                {
                    if (action.Kind == PatchActionKind.Add)
                    {
                        throw new InvalidOperationException(
                            $"Patch target '{action.Path}' appeared after planning; refusing to overwrite it.");
                    }
                }

                var source = PatchPath.ResolveUnder(stagingDirectory, action.Path);
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException(
                        $"Staged patch file '{action.Path}' is missing.",
                        source);
                }

                var parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.Move(source, destination, overwrite: false);
            }

            var state = new PatchState
            {
                ActiveRelease = manifest.Release,
                PreviousRelease = previousState?.ActiveRelease,
                AppliedAt = appliedAt,
                ManifestGeneratedAt = manifest.GeneratedAt,
                PreviousManifestGeneratedAt = previousState?.ManifestGeneratedAt,
                PreviousAppliedAt = previousState?.AppliedAt,
                TransactionId = transactionId,
                BackupDirectory = backupDirectory,
                Added = added,
                Replaced = replaced,
                Deleted = deleted
            };
            PatchStateStore.Write(rootDirectory, state);
            PatchStateStore.DeletePending(rootDirectory);

            if (previousState?.BackupDirectory is { Length: > 0 } oldBackup &&
                !string.Equals(oldBackup, backupDirectory, StringComparison.Ordinal))
            {
                TryDeleteDirectory(oldBackup);
            }

            return new PatchApplyResult(
                manifest.Release,
                added.Count,
                replaced.Count,
                deleted.Count,
                backupDirectory);
        }
        catch
        {
            Restore(rootDirectory, transaction);
            PatchStateStore.DeletePending(rootDirectory);
            WritePreviousState(rootDirectory, transaction);
            TryDeleteDirectory(backupDirectory);
            throw;
        }
    }

    public static PatchRollbackResult Rollback(string rootDirectory)
    {
        RecoverPending(rootDirectory);
        var state = PatchStateStore.Read(rootDirectory);
        if (state is null)
        {
            throw new InvalidOperationException("No client patch state is available for rollback.");
        }

        var hasActions = state.Added.Count > 0 || state.Replaced.Count > 0 || state.Deleted.Count > 0;
        if (!hasActions)
        {
            throw new InvalidOperationException("No previous client version is available for rollback.");
        }

        var transaction = new PatchTransaction
        {
            Id = state.TransactionId ?? string.Empty,
            Release = state.ActiveRelease ?? string.Empty,
            StartedAt = state.AppliedAt ?? DateTimeOffset.UtcNow,
            ManifestGeneratedAt = state.ManifestGeneratedAt ?? DateTimeOffset.MinValue,
            BackupDirectory = state.BackupDirectory ?? string.Empty,
            PreviousRelease = state.PreviousRelease,
            PreviousManifestGeneratedAt = state.PreviousManifestGeneratedAt,
            PreviousAppliedAt = state.PreviousAppliedAt,
            Added = state.Added,
            Replaced = state.Replaced,
            Deleted = state.Deleted
        };

        Restore(rootDirectory, transaction);
        WritePreviousState(rootDirectory, transaction);
        if (!string.IsNullOrEmpty(transaction.BackupDirectory))
        {
            TryDeleteDirectory(transaction.BackupDirectory);
        }

        var restored = transaction.Replaced.Count + transaction.Deleted.Count;
        return new PatchRollbackResult(
            transaction.PreviousRelease,
            restored,
            transaction.Added.Count);
    }

    private static void Restore(string rootDirectory, PatchTransaction transaction)
    {
        foreach (var path in transaction.Added)
        {
            var destination = PatchPath.ResolveUnder(rootDirectory, path);
            if (File.Exists(destination)) File.Delete(destination);
        }

        foreach (var path in transaction.Replaced.Concat(transaction.Deleted))
        {
            if (string.IsNullOrEmpty(transaction.BackupDirectory)) continue;
            var backup = PatchPath.ResolveUnder(transaction.BackupDirectory, path);
            if (!File.Exists(backup)) continue;

            var destination = PatchPath.ResolveUnder(rootDirectory, path);
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.Move(backup, destination, overwrite: true);
        }
    }

    private static void BackupExistingFile(
        string rootDirectory,
        string backupDirectory,
        string path)
    {
        var source = PatchPath.ResolveUnder(rootDirectory, path);
        if (!File.Exists(source)) return;

        var backup = PatchPath.ResolveUnder(backupDirectory, path);
        var parent = Path.GetDirectoryName(backup);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.Move(source, backup, overwrite: true);
    }

    private static void WritePreviousState(string rootDirectory, PatchTransaction transaction)
    {
        if (transaction.PreviousRelease is null &&
            transaction.PreviousManifestGeneratedAt is null &&
            transaction.PreviousAppliedAt is null)
        {
            PatchStateStore.Delete(rootDirectory);
            return;
        }

        PatchStateStore.Write(rootDirectory, new PatchState
        {
            ActiveRelease = transaction.PreviousRelease,
            AppliedAt = transaction.PreviousAppliedAt,
            ManifestGeneratedAt = transaction.PreviousManifestGeneratedAt
        });
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
