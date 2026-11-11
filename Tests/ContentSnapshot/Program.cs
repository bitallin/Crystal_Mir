using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crystal.Content;

var passed = 0;

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    passed++;
}

void Expect<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        passed++;
        return;
    }

    throw new InvalidOperationException(message);
}

ContentEntity Entity(string type, long index, string key, string json)
{
    return new ContentEntity
    {
        EntityType = type,
        LocalIndex = index,
        ContentKey = key,
        Data = ContentJson.ParseCanonical(json)
    };
}

string NewTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), $"crystal-content-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
}

var canonical = ContentJson.ParseCanonical("""{"b":2,"a":{"d":4,"c":3}}""");
Check(canonical.GetRawText() == """{"a":{"c":3,"d":4},"b":2}""", "Canonical JSON must sort object keys.");

var entities = new[]
{
    Entity(ContentEntityTypes.Item, 2, "item.2", """{"Index":2,"Name":"B"}"""),
    Entity(ContentEntityTypes.Monster, 1, "monster.1",
        """{"Index":1,"Name":"M","Drops":[{"Item":{"$ref":"item","index":1}}]}"""),
    Entity(ContentEntityTypes.Item, 1, "item.1", """{"Name":"A","Index":1}""")
};

var firstDirectory = NewTempDirectory();
var firstManifest = ContentSnapshotWriter.Write(new ContentSnapshotWriteOptions
{
    OutputDirectory = firstDirectory,
    ReleaseVersion = "test.1",
    Source = "unit-test",
    SourceHash = "source-hash",
    CreatedAt = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero)
}, entities);

Check(firstManifest.Counts[ContentEntityTypes.Item] == 2, "Item count must be 2.");
Check(firstManifest.Counts[ContentEntityTypes.Monster] == 1, "Monster count must be 1.");

var itemLines = File.ReadAllLines(Path.Combine(firstDirectory, "entities", "item.jsonl"));
Check(itemLines.Length == 2, "Item file must contain two lines.");
Check(itemLines[0].Contains("\"contentKey\":\"item.1\"", StringComparison.Ordinal), "Entities must be ordered by local index.");
Check(itemLines[1].Contains("\"contentKey\":\"item.2\"", StringComparison.Ordinal), "Entities must be ordered by local index.");

var snapshot = ContentSnapshotReader.Read(firstDirectory);
Check(snapshot.Entities.Count == 3, "Snapshot must round-trip all entities.");
Check(ContentSnapshotValidator.Validate(snapshot).Count == 0, "Generated snapshot must be valid.");

var secondDirectory = NewTempDirectory();
var secondManifest = ContentSnapshotWriter.Write(new ContentSnapshotWriteOptions
{
    OutputDirectory = secondDirectory,
    ReleaseVersion = "test.2",
    Source = "unit-test",
    SourceHash = "source-hash",
    CreatedAt = new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero)
}, entities.Reverse());

Check(firstManifest.ContentHash == secondManifest.ContentHash,
    "Content hash must not depend on entity input order, release version, or creation time.");
Check(firstManifest.Entities[ContentEntityTypes.Item].Sha256 == secondManifest.Entities[ContentEntityTypes.Item].Sha256,
    "Entity file hashes must be deterministic.");

var signedDirectory = NewTempDirectory();
ContentSnapshotWriter.Write(new ContentSnapshotWriteOptions
{
    OutputDirectory = signedDirectory,
    ReleaseVersion = "test.signed",
    Source = "signature-test",
    SourceHash = "source-hash",
    CreatedAt = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero)
}, entities);
using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var privateKeyPem = signingKey.ExportPkcs8PrivateKeyPem();
var publicKeyPem = signingKey.ExportSubjectPublicKeyInfoPem();
var signedManifest = ContentSnapshotSigner.SignSnapshot(
    signedDirectory,
    privateKeyPem,
    "test-release-key",
    new DateTimeOffset(2026, 11, 11, 1, 2, 3, TimeSpan.Zero));
Check(signedManifest.Signature is not null, "Signed manifest must contain a signature.");
Check(signedManifest.Signature!.Algorithm == ContentSnapshotFormat.SignatureAlgorithmEcdsaP256Sha256,
    "Signature algorithm must be recorded.");
Check(signedManifest.ContentHash == firstManifest.ContentHash,
    "Signing must not change the content hash.");
Check(ContentSnapshotSigner.VerifySnapshot(signedDirectory, publicKeyPem, "test-release-key").IsValid,
    "Signed snapshot must verify with the matching public key.");
Check(!ContentSnapshotSigner.VerifySnapshot(signedDirectory, publicKeyPem, "wrong-key").IsValid,
    "Signature verification must enforce the expected key id.");
using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
Check(!ContentSnapshotSigner.Verify(signedManifest, otherKey.ExportSubjectPublicKeyInfoPem()).IsValid,
    "Signature verification must fail with a different public key.");
Check(!ContentSnapshotSigner.Verify(firstManifest, publicKeyPem).IsValid,
    "Unsigned manifests must not verify.");
using var p384Key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
Expect<ArgumentException>(() => ContentSnapshotSigner.Sign(firstManifest, p384Key, "p384-key"),
    "Only NIST P-256 keys must be accepted for content signatures.");

var tamperedManifestPath = Path.Combine(signedDirectory, "manifest.json");
var tamperedManifest = JsonNode.Parse(File.ReadAllText(tamperedManifestPath))!.AsObject();
tamperedManifest["releaseVersion"] = "test.tampered";
File.WriteAllText(tamperedManifestPath, tamperedManifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Check(!ContentSnapshotSigner.VerifySnapshot(signedDirectory, publicKeyPem).IsValid,
    "Manifest metadata changes must invalidate the signature.");

Expect<InvalidDataException>(() => ContentSnapshotWriter.ValidateAndOrder(new[]
{
    Entity(ContentEntityTypes.Item, 1, "item.1", """{"Index":1}"""),
    Entity(ContentEntityTypes.Item, 1, "item.2", """{"Index":2}""")
}), "Duplicate local indices must be rejected.");

Expect<InvalidDataException>(() => ContentSnapshotWriter.ValidateAndOrder(new[]
{
    Entity(ContentEntityTypes.Item, 1, "item.1", """{"Index":1}"""),
    Entity(ContentEntityTypes.Item, 2, "item.1", """{"Index":2}""")
}), "Duplicate content keys must be rejected.");

File.AppendAllText(Path.Combine(firstDirectory, "entities", "item.jsonl"), "\n");
Expect<InvalidDataException>(() => ContentSnapshotReader.Read(firstDirectory),
    "Tampered entity files must fail hash validation.");

var scriptSource = """
#INCLUDE [shared/NPC.txt] @MAIN
#INSERT [shared/Common.txt]
[@MAIN]
#SAY
Welcome. <Next/@NEXT>
#IF
CHECKGOLD > 10
#ACT
CALL @DETAIL
GOTO @NEXT
[@NEXT]
#SAY
Next page.
[@DETAIL]
#SAY
Detail page.
""";
var script = NpcScriptParser.Parse(scriptSource);
Check(script.IsValid, "Valid NPC script must compile.");
Check(script.PageKeys.SequenceEqual(["@MAIN", "@NEXT", "@DETAIL"]), "NPC script pages must be preserved in source order.");
Check(script.Includes.Count == 1 && script.Includes[0].Target == "shared/NPC.txt",
    "NPC script include metadata must be captured.");
Check(script.Inserts.Count == 1 && script.Inserts[0].Target == "shared/Common.txt",
    "NPC script insert metadata must be captured.");
Check(script.Calls.SequenceEqual(["@DETAIL"]), "NPC script calls must be captured.");
Check(script.Ast.Pages.Count == 3 && script.Ast.StatementCount == 3,
    "NPC script AST must contain pages and command statements.");
Check(script.CompiledSha256.Length == 64, "NPC script compiled hash must be SHA256.");
var repeatedScript = NpcScriptParser.Parse(scriptSource.Replace("\n", "\r\n", StringComparison.Ordinal));
Check(script.CompiledSha256 == repeatedScript.CompiledSha256,
    "NPC script compiled hash must be stable across newline styles.");

var invalidScript = NpcScriptParser.Parse("""
[@MAIN]
#ACT
GOTO @MISSING
[@MAIN]
#UNKNOWN
""");
Check(!invalidScript.IsValid, "Invalid NPC script must fail compilation.");
Check(invalidScript.Issues.Any(issue => issue.Code == "duplicate-page"),
    "Duplicate NPC pages must be reported.");
Check(invalidScript.Issues.Any(issue => issue.Code == "unresolved-command-target"),
    "Missing NPC page targets must be reported.");
Check(invalidScript.Issues.Any(issue => issue.Code == "unknown-directive"),
    "Unknown NPC directives must be reported.");

var mapBytes = new byte[52 + 5 * 4 * 12];
BitConverter.GetBytes((short)5).CopyTo(mapBytes, 0);
BitConverter.GetBytes((short)4).CopyTo(mapBytes, 2);
BitConverter.GetBytes((short)-32768).CopyTo(mapBytes, 52);
BitConverter.GetBytes((short)-32768).CopyTo(mapBytes, 52 + 12 + 2);
mapBytes[52 + 12 + 8] = 1;
var mapMetadata = MapAssetParser.Parse(mapBytes);
Check(mapMetadata.Valid, "Synthetic v0 map must parse.");
Check(mapMetadata.Width == 5 && mapMetadata.Height == 4, "Map dimensions must match the header.");
Check(mapMetadata.CellCount == 20 && mapMetadata.BlockedCells == 2 && mapMetadata.WalkableCells == 18,
    "Map parser must count blocked and walkable cells.");
Check(mapMetadata.DoorCells == 1, "Map parser must count door cells.");
var invalidMapMetadata = MapAssetParser.Parse(new byte[52]);
Check(!invalidMapMetadata.Valid, "Truncated map files must fail validation.");

Console.WriteLine($"ContentSnapshot: {passed} checks passed.");
