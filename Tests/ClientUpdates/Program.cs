using System.Security.Cryptography;
using System.Text.Json;
using Crystal.Patcher;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void CheckContains(IEnumerable<string> values, string expected, string message)
{
    if (!values.Any(value => value.Contains(expected, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException(message + ": " + string.Join(" | ", values));
    }
}

static void WriteFile(string root, string relativePath, string content)
{
    var path = PatchPath.ResolveUnder(root, relativePath);
    var parent = Path.GetDirectoryName(path);
    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
    File.WriteAllText(path, content);
}

static string ReadFile(string root, string relativePath)
{
    return File.ReadAllText(PatchPath.ResolveUnder(root, relativePath));
}

var root = Path.Combine(Path.GetTempPath(), "crystal-client-updates-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var privateKeyPem = key.ExportPkcs8PrivateKeyPem();
    var publicKeyPem = key.ExportSubjectPublicKeyInfoPem();
    const string keyId = "test-key";
    var now = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);

    var source = Path.Combine(root, "source");
    var client = Path.Combine(root, "client");
    Directory.CreateDirectory(source);
    Directory.CreateDirectory(client);
    WriteFile(source, "a.txt", "alpha-new");
    WriteFile(source, "b.txt", "bravo-new");
    WriteFile(source, "nested/c.txt", "charlie-new");
    WriteFile(client, "a.txt", "alpha-new");
    WriteFile(client, "b.txt", "bravo-old");
    WriteFile(client, "nested/old.txt", "remove-me");
    WriteFile(client, "user/keep.ini", "keep-me");

    var manifest = PatchManifestBuilder.Build(new PatchBuildOptions
    {
        RootDirectory = source,
        Channel = "stable",
        Release = "2026.11.18.1",
        UrlPrefix = ".",
        MinimumClientVersion = "1.0.0",
        MinimumLauncherVersion = "1.0.0",
        ProtocolVersion = 1,
        ContentSchemaVersion = 1,
        GeneratedAt = now,
        ExpiresAt = now.AddDays(1),
        Deleted = new List<string> { "nested/old.txt" }
    });
    manifest = PatchManifestSigner.Sign(manifest, privateKeyPem, keyId, now);
    var validationOptions = new PatchValidationOptions
    {
        ExpectedChannel = "stable",
        ExpectedKeyId = keyId,
        ClientVersion = "1.2.0",
        LauncherVersion = "1.2.0",
        ProtocolVersion = 1,
        ContentSchemaVersion = 1,
        Now = now.AddMinutes(1)
    };
    var validation = PatchManifestValidator.Validate(manifest, publicKeyPem, validationOptions);
    Check(validation.IsValid, "Valid signed manifest was rejected: " + string.Join("; ", validation.Errors));

    var tampered = manifest with { Release = "2026.11.18.2" };
    Check(!PatchManifestSigner.Verify(tampered, publicKeyPem, keyId).IsValid, "Tampered release was accepted");

    var tamperedFile = manifest with
    {
        Files = manifest.Files.Select(file => file.Path == "a.txt"
            ? file with { Sha256 = new string('0', 64) }
            : file).ToList()
    };
    Check(!PatchManifestSigner.Verify(tamperedFile, publicKeyPem, keyId).IsValid, "Tampered file hash was accepted");
    Check(!PatchManifestSigner.Verify(manifest, publicKeyPem, "other-key").IsValid, "Wrong key id was accepted");

    var expired = PatchManifestSigner.Sign(
        manifest with { ExpiresAt = now.AddMinutes(-1) },
        privateKeyPem,
        keyId,
        now);
    var expiredResult = PatchManifestValidator.Validate(expired, publicKeyPem, validationOptions);
    CheckContains(expiredResult.Errors, "expired", "Expired manifest was accepted");

    var tooOldResult = PatchManifestValidator.Validate(manifest, publicKeyPem, new PatchValidationOptions
    {
        ClientVersion = "0.9.0",
        LauncherVersion = "1.2.0",
        Now = now.AddMinutes(1)
    });
    CheckContains(tooOldResult.Errors, "below the required minimum", "Old client was accepted");

    var protocolResult = PatchManifestValidator.Validate(manifest, publicKeyPem, new PatchValidationOptions
    {
        ClientVersion = "1.2.0",
        LauncherVersion = "1.2.0",
        ProtocolVersion = 2,
        Now = now.AddMinutes(1)
    });
    CheckContains(protocolResult.Errors, "protocol version", "Protocol mismatch was accepted");

    var channelResult = PatchManifestValidator.Validate(manifest, publicKeyPem, new PatchValidationOptions
    {
        ClientVersion = "1.2.0",
        LauncherVersion = "1.2.0",
        ExpectedChannel = "beta",
        Now = now.AddMinutes(1)
    });
    CheckContains(channelResult.Errors, "channel", "Wrong channel was accepted");

    var traversal = PatchManifestSigner.Sign(new ClientPatchManifest
    {
        Channel = "stable",
        Release = "bad",
        MinimumClientVersion = "1.0.0",
        MinimumLauncherVersion = "1.0.0",
        GeneratedAt = now,
        ExpiresAt = now.AddDays(1),
        Files = new List<ClientPatchFile>
        {
            new() { Path = "../evil.txt", Size = 1, Sha256 = new string('a', 64), Url = "evil.txt" },
            new() { Path = "C:\\evil.txt", Size = 1, Sha256 = new string('b', 64), Url = "evil2.txt" },
            new() { Path = ".crystal-update/state.json", Size = 1, Sha256 = new string('c', 64), Url = "evil3.txt" },
            new() { Path = "bad?.txt", Size = 1, Sha256 = new string('d', 64), Url = "evil4.txt" },
            new() { Path = "CON.txt", Size = 1, Sha256 = new string('e', 64), Url = "evil5.txt" },
            new() { Path = "trail.", Size = 1, Sha256 = new string('f', 64), Url = "evil6.txt" }
        }
    }, privateKeyPem, keyId, now);
    var traversalResult = PatchManifestValidator.Validate(traversal, publicKeyPem, new PatchValidationOptions
    {
        ClientVersion = "1.2.0",
        LauncherVersion = "1.2.0",
        Now = now.AddMinutes(1)
    });
    CheckContains(traversalResult.Errors, "traversal", "Traversal path was accepted");
    CheckContains(traversalResult.Errors, "invalid character", "Drive/colon path was accepted");
    CheckContains(traversalResult.Errors, "reserved update state", "State directory path was accepted");
    CheckContains(traversalResult.Errors, "reserved Windows device", "Windows device path was accepted");

    var duplicate = PatchManifestSigner.Sign(new ClientPatchManifest
    {
        Channel = "stable",
        Release = "duplicate",
        GeneratedAt = now,
        ExpiresAt = now.AddDays(1),
        Files = new List<ClientPatchFile>
        {
            new() { Path = "a.txt", Size = 1, Sha256 = new string('a', 64), Url = "a.txt" },
            new() { Path = "A.txt", Size = 1, Sha256 = new string('b', 64), Url = "b.txt" }
        }
    }, privateKeyPem, keyId, now);
    var duplicateResult = PatchManifestValidator.Validate(duplicate, publicKeyPem, new PatchValidationOptions
    {
        ClientVersion = "1.2.0",
        LauncherVersion = "1.2.0",
        Now = now.AddMinutes(1)
    });
    CheckContains(duplicateResult.Errors, "Duplicate manifest path", "Case-insensitive duplicate path was accepted");

    Directory.CreateDirectory(Path.Combine(client, PatchManifestFormat.StateDirectoryName));
    File.WriteAllText(Path.Combine(client, PatchManifestFormat.StateDirectoryName, "ignored.bin"), "state");
    var scan = PatchScanner.Scan(client);
    Check(!scan.Keys.Any(path => path.StartsWith(PatchManifestFormat.StateDirectoryName, StringComparison.OrdinalIgnoreCase)),
        "Update state directory leaked into local scan");

    var baseUri = new Uri(Path.GetFullPath(source) + Path.DirectorySeparatorChar);
    var applyResult = await PatchUpdateService.UpdateAsync(new PatchUpdateRequest
    {
        Manifest = manifest,
        RootDirectory = client,
        PublicKeyPem = publicKeyPem,
        ManifestBaseUri = baseUri,
        Validation = validationOptions,
        Now = now.AddMinutes(1)
    });
    Check(!applyResult.UpToDate, "Initial update was reported as up to date");
    Check(applyResult.Added == 1 && applyResult.Replaced == 1 && applyResult.Deleted == 1,
        $"Unexpected update counts: added={applyResult.Added}, replaced={applyResult.Replaced}, deleted={applyResult.Deleted}");
    Check(ReadFile(client, "a.txt") == "alpha-new", "Unchanged file was altered");
    Check(ReadFile(client, "b.txt") == "bravo-new", "Changed file was not replaced");
    Check(ReadFile(client, "nested/c.txt") == "charlie-new", "New file was not installed");
    Check(!File.Exists(PatchPath.ResolveUnder(client, "nested/old.txt")), "Deleted file still exists");
    Check(ReadFile(client, "user/keep.ini") == "keep-me", "Unknown local file was deleted");

    var state = PatchStateStore.Read(client);
    Check(state?.ActiveRelease == "2026.11.18.1", "Active release was not persisted");
    Check(state?.BackupDirectory is { Length: > 0 } && Directory.Exists(state.BackupDirectory),
        "Backup directory was not retained");

    var repeat = await PatchUpdateService.UpdateAsync(new PatchUpdateRequest
    {
        Manifest = manifest,
        RootDirectory = client,
        PublicKeyPem = publicKeyPem,
        ManifestBaseUri = baseUri,
        Validation = validationOptions,
        Now = now.AddMinutes(2)
    });
    Check(repeat.UpToDate, "Repeated update was not a no-op");

    var older = PatchManifestSigner.Sign(
        manifest with
        {
            Release = "replay",
            GeneratedAt = now.AddMinutes(-5),
            ExpiresAt = now.AddDays(1)
        },
        privateKeyPem,
        keyId,
        now);
    var downgrade = PatchManifestValidator.Validate(older, publicKeyPem, new PatchValidationOptions
    {
        ClientVersion = "1.2.0",
        LauncherVersion = "1.2.0",
        Now = now.AddMinutes(2),
        MinimumGeneratedAt = state?.ManifestGeneratedAt
    });
    CheckContains(downgrade.Errors, "downgrade", "Older manifest replay was accepted");

    var failedSource = Path.Combine(root, "failed-source");
    var failedClient = Path.Combine(root, "failed-client");
    Directory.CreateDirectory(failedSource);
    Directory.CreateDirectory(failedClient);
    WriteFile(failedSource, "file.txt", "good");
    WriteFile(failedClient, "file.txt", "old");
    var failedManifest = PatchManifestBuilder.Build(new PatchBuildOptions
    {
        RootDirectory = failedSource,
        Channel = "stable",
        Release = "hash-failure",
        UrlPrefix = ".",
        GeneratedAt = now,
        ExpiresAt = now.AddDays(1)
    });
    failedManifest = PatchManifestSigner.Sign(failedManifest, privateKeyPem, keyId, now);
    WriteFile(failedSource, "file.txt", "tampered");
    var failed = false;
    try
    {
        await PatchUpdateService.UpdateAsync(new PatchUpdateRequest
        {
            Manifest = failedManifest,
            RootDirectory = failedClient,
            PublicKeyPem = publicKeyPem,
            ManifestBaseUri = new Uri(Path.GetFullPath(failedSource) + Path.DirectorySeparatorChar),
            Validation = new PatchValidationOptions
            {
                ClientVersion = "1.2.0",
                LauncherVersion = "1.2.0",
                Now = now.AddMinutes(1)
            },
            Now = now.AddMinutes(1)
        });
    }
    catch (InvalidDataException exception)
    {
        failed = exception.Message.Contains("SHA256", StringComparison.OrdinalIgnoreCase) ||
                 exception.Message.Contains("size", StringComparison.OrdinalIgnoreCase);
    }

    Check(failed, "Hash mismatch did not fail the update");
    Check(ReadFile(failedClient, "file.txt") == "old", "Hash mismatch modified the client");
    Check(PatchStateStore.Read(failedClient) is null, "Hash mismatch created update state");

    var recovered = Path.Combine(root, "recover");
    Directory.CreateDirectory(recovered);
    WriteFile(recovered, "file.txt", "old");
    var backupRoot = Path.Combine(PatchStateStore.GetBackupsDirectory(recovered), "tx-1");
    Directory.CreateDirectory(backupRoot);
    var backupFile = PatchPath.ResolveUnder(backupRoot, "file.txt");
    Directory.CreateDirectory(Path.GetDirectoryName(backupFile)!);
    File.Move(PatchPath.ResolveUnder(recovered, "file.txt"), backupFile);
    WriteFile(recovered, "file.txt", "new");
    PatchStateStore.WritePending(recovered, new PatchTransaction
    {
        Id = "tx-1",
        Release = "pending",
        StartedAt = now,
        ManifestGeneratedAt = now,
        BackupDirectory = backupRoot,
        Replaced = new List<string> { "file.txt" }
    });
    Check(PatchInstaller.RecoverPending(recovered), "Pending transaction was not recovered");
    Check(ReadFile(recovered, "file.txt") == "old", "Pending transaction was not rolled back");
    Check(PatchStateStore.ReadPending(recovered) is null, "Pending transaction file was not removed");

    var rollback = PatchInstaller.Rollback(client);
    Check(rollback.RestoredRelease is null, "Initial install rollback should not report a previous release");
    Check(ReadFile(client, "b.txt") == "bravo-old", "Rollback did not restore replaced file");
    Check(ReadFile(client, "nested/old.txt") == "remove-me", "Rollback did not restore deleted file");
    Check(!File.Exists(PatchPath.ResolveUnder(client, "nested/c.txt")), "Rollback did not remove added file");
    Check(ReadFile(client, "user/keep.ini") == "keep-me", "Rollback altered unknown local file");

    var json = JsonSerializer.Serialize(manifest, PatchJson.CompactOptions);
    var parsed = PatchManifestLoader.Parse(json);
    Check(PatchManifestSigner.Verify(parsed, publicKeyPem, keyId).IsValid, "Manifest JSON round-trip broke signature");

    try
    {
        PatchPath.ResolveUnder(client, "../escape.txt");
        throw new InvalidOperationException("Path traversal was not rejected by ResolveUnder");
    }
    catch (InvalidDataException)
    {
    }

    Console.WriteLine("Client update checks passed: signature, tamper, expiry, version gate, path safety, scan, apply, no-op, anti-downgrade, hash failure, crash recovery, rollback, JSON round-trip.");
    return 0;
}
finally
{
    try
    {
        Directory.Delete(root, recursive: true);
    }
    catch
    {
    }
}
