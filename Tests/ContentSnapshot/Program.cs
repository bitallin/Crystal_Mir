using System.Text.Json;
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

Console.WriteLine($"ContentSnapshot: {passed} checks passed.");
