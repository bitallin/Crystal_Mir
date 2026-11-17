using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crystal.Content;
using Crystal.Control.Api.Content;
using Crystal.LegacyRuntime;
using Server.MirDatabase;

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

JsonElement Json(string value) => ContentJson.ParseCanonical(value);

ContentEntity Entity(string type, long index, string key, string json)
{
    return new ContentEntity
    {
        EntityType = type,
        LocalIndex = index,
        ContentKey = key,
        Data = Json(json)
    };
}

string NewTempDirectory()
{
    var path = Path.Combine(Path.GetTempPath(), $"crystal-editor-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(path);
    return path;
}

ContentSnapshot SnapshotOf(params ContentEntity[] snapshotEntities)
{
    return new ContentSnapshot
    {
        Manifest = new ContentSnapshotManifest
        {
            ReleaseVersion = "unit-release",
            Source = "unit-test",
            Generator = "unit-test",
            ContentHash = "unit",
            Entities = new SortedDictionary<string, ContentSnapshotFile>(StringComparer.Ordinal),
            Counts = new SortedDictionary<string, int>(StringComparer.Ordinal)
        },
        Entities = snapshotEntities
    };
}

var expectedTypes = new[]
{
    ContentEntityTypes.Item,
    ContentEntityTypes.Monster,
    ContentEntityTypes.Map,
    ContentEntityTypes.Npc,
    ContentEntityTypes.Script,
    ContentEntityTypes.Drop,
    ContentEntityTypes.Magic,
    ContentEntityTypes.Quest,
    ContentEntityTypes.GameShop,
    ContentEntityTypes.Recipe
};

Check(ContentEditorSchemas.All.Select(schema => schema.EntityType).Order(StringComparer.Ordinal)
    .SequenceEqual(expectedTypes.Order(StringComparer.Ordinal)), "Exactly ten typed editors must be registered.");
foreach (var schema in ContentEditorSchemas.All)
{
    Check(schema.SchemaVersion == 1, $"{schema.EntityType} schema version must be 1.");
    Check(schema.Sections.Count > 0, $"{schema.EntityType} must expose sections.");
}

var allFields = ContentEditorSchemas.All
    .SelectMany(schema => schema.Sections.SelectMany(section => section.Fields))
    .ToArray();
foreach (var kind in new[] { "number", "array", "reference", "code", "color", "readonly" })
{
    Check(allFields.Any(field => field.Kind == kind), $"Schema field kind '{kind}' must be represented.");
}
Check(allFields.Any(field => field.ReferenceType == "item" && field.Nullable == false && field.MinItems is null),
    "Reference DTO must expose referenceType while keeping optional DTO fields backward compatible.");
Check(allFields.Any(field => field.Kind == "code" && field.Multiline == true), "Code fields must expose multiline.");
Check(allFields.Any(field => field.Kind == "array" && field.Fields is { Count: > 0 }), "Array fields must expose child fields.");

Check(ContentEditorSchemas.ResolveIdentity(ContentEntityTypes.Item, Json("""{"Name":"A"}"""), 7) == new ContentEditorIdentity(7, "item.7"),
    "Item identity must use auto local index.");
Check(ContentEditorSchemas.ResolveIdentity(ContentEntityTypes.Monster, Json("""{"Name":"M"}"""), 8) == new ContentEditorIdentity(8, "monster.8"),
    "Monster identity must use auto local index.");
Check(ContentEditorSchemas.ResolveIdentity(ContentEntityTypes.Map, Json("""{"FileName":"0"}"""), 9) == new ContentEditorIdentity(9, "map.9"),
    "Map identity must use auto local index.");
Check(ContentEditorSchemas.ResolveIdentity(ContentEntityTypes.Npc, Json("""{"Name":"N"}"""), 10) == new ContentEditorIdentity(10, "npc.10"),
    "NPC identity must use auto local index.");
Check(ContentEditorSchemas.ResolveIdentity(ContentEntityTypes.Quest, Json("""{"Name":"Q"}"""), 11) == new ContentEditorIdentity(11, "quest.11"),
    "Quest identity must use auto local index.");
Check(ContentEditorSchemas.ResolveIdentity(ContentEntityTypes.GameShop, Json("""{"ItemIndex":1}"""), 12) == new ContentEditorIdentity(12, "gameshop.12"),
    "Game shop identity must use auto local index.");
Check(ContentEditorSchemas.ResolveIdentity(ContentEntityTypes.Magic, Json("""{"Spell":"FireBall"}"""), 99) == new ContentEditorIdentity(31, "magic.31"),
    "Magic identity must use the selected Spell value.");
Check(ContentEditorSchemas.ResolveIdentity(ContentEntityTypes.Drop, Json("""{"monsterIndex":42}"""), 99) == new ContentEditorIdentity(42, "drop.monster.42"),
    "Drop identity must use monsterIndex.");
Check(ContentEditorSchemas.ResolveIdentity(ContentEntityTypes.Recipe,
        Json("""{"name":"IGNORE","output":{"itemName":"Green Poison"}}"""), 99) == new ContentEditorIdentity(99, "recipe.Green-Poison"),
    "Recipe identity must use a safe produced-item key.");
Check(ContentEditorSchemas.ResolveIdentity(ContentEntityTypes.Script,
        Json("""{"relativePath":"Envir/NPCs/Quest/Main Story.txt"}"""), 99) == new ContentEditorIdentity(99, "script.Quest.Main-Story"),
    "Script identity must normalize the script path.");

Check(ContentEditorSchemas.IsContentKeyValid(ContentEntityTypes.Recipe,
        Json("""{"name":"Legacy","output":{"itemName":"GreenPoison(Old)"}}"""), 4, "recipe.GreenPoison(Old)"),
    "Historical recipe keys with parentheses must remain valid.");
Check(!ContentEditorSchemas.IsContentKeyValid(ContentEntityTypes.Drop,
        Json("""{"monsterIndex":4}"""), 4, "drop.monster.5"),
    "Drop content keys must not be rewritten to another monster.");

var itemErrors = ContentItemEditorSchema.ValidateStructure(
    Json("""{"Index":1,"Name":123,"Type":"Weapon"}"""), 1, "item.1");
Check(itemErrors.ContainsKey("Name"), "Item structure validation must report wrong string types.");

var npcErrors = ContentNpcEditorSchema.ValidateStructure(
    Json("""{"Index":1,"Name":"NPC","Location":{"x":"left","y":2},"MapIndex":1,"FileName":"a"}"""), 1, "npc.1");
Check(npcErrors.ContainsKey("Location.x"), "Nested object validation must include the parent path.");

var dropErrors = ContentDropEditorSchema.ValidateStructure(
    Json("""
         {"monsterIndex":1,"monsterName":"M","drops":[
           {"Chance":1,"GroupedDrop":{"Random":true,"First":false,"items":[
             {"Chance":2,"Gold":100,"GroupedDrop":{"Random":false,"First":true,"items":[]}}
           ]}}
         ]}
         """), 1, "drop.monster.1");
Check(dropErrors.Values.Any(messages => messages.Any(message => message.Contains("exactly one", StringComparison.Ordinal))),
    "Nested drop groups must reject entries with multiple target types.");

var validDrop = Json("""
    {"monsterIndex":1,"monsterName":"M","drops":[
      {"Chance":1,"GroupedDrop":{"Random":true,"First":false,"items":[
        {"Chance":2,"Item":{"$ref":"item","index":1},"Type":0,"QuestRequired":false}
      ]}}
    ]}
    """);
Check(ContentDropEditorSchema.ValidateStructure(validDrop, 1, "drop.monster.1").Count == 0,
    "Nested drop group with an item reference must be valid.");
Check(ContentDropEditorSchema.GetReferences(validDrop).Any(reference =>
        reference.Path == "drops[0].GroupedDrop.items[0].Item" && reference.Index == 1),
    "Drop reference traversal must descend into grouped drops.");

var normalizedShop = ContentGameShopEditorSchema.Normalize(
    Json("""{"GIndex":3,"ItemIndex":710,"Info":{"index":710,"key":"item.710","Name":"GreenPoison"}}"""));
Check(normalizedShop.GetProperty("ItemIndex").GetInt64() == 710, "Game shop normalization must retain ItemIndex.");
Check(normalizedShop.GetProperty("Info").GetProperty("$ref").GetString() == "item",
    "Game shop normalization must emit a typed $ref.");
Check(normalizedShop.GetProperty("Info").GetProperty("key").GetString() == "item.710",
    "Game shop normalization must synchronize the reference key.");

var normalizedScript = ContentScriptEditorSchema.Normalize(Json("""
    {"relativePath":"Envir/NPCs/Quest/Main Story.txt","language":"CrystalNpc","source":"[@MAIN]\nSAY Hello\nGOTO @NEXT\n\n[@NEXT]\nSAY Bye\n"}
    """));
Check(normalizedScript.GetProperty("relativePath").GetString() == "Quest/Main Story", "Script path prefix must be normalized.");
Check(normalizedScript.GetProperty("pages").GetArrayLength() == 2, "Script parser must expose pages.");
Check(normalizedScript.GetProperty("ast").GetProperty("pages").GetArrayLength() == 2,
    "Script parser must expose AST pages.");
Check(normalizedScript.GetProperty("sha256").GetString()?.Length == 64, "Script normalization must compute SHA256.");
Check(normalizedScript.GetProperty("validation").GetProperty("compiledSha256").GetString()?.Length == 64,
    "Script normalization must compute the compiled hash.");
Check(normalizedScript.GetProperty("validation").GetProperty("errorCount").GetInt32() == 0,
    "Valid script source must have no parse errors.");

var rawScriptErrors = ContentEditorSchemas.ValidateRawStructure(
    ContentEntityTypes.Script,
    Json("""{"relativePath":"M2/Invalid.txt","language":"CrystalNpc","source":123}"""));
Check(rawScriptErrors.ContainsKey("source"),
    "Raw structure validation must reject non-string script source before normalization.");
var rawReadOnlyErrors = ContentEditorSchemas.ValidateRawStructure(
    ContentEntityTypes.Item,
    Json("""{"Index":null,"Name":"Sword"}"""));
Check(!rawReadOnlyErrors.ContainsKey("Index"),
    "Raw structure validation must ignore server-derived read-only fields.");

var entities = new[]
{
    Entity(ContentEntityTypes.Item, 1, "item.1", """{"Index":1,"Name":"Sword","Type":"Weapon"}"""),
    Entity(ContentEntityTypes.Monster, 1, "monster.1", """{"Index":1,"Name":"Hen"}"""),
    Entity(ContentEntityTypes.Npc, 1, "npc.1",
        """{"Index":1,"Name":"NPC","MapIndex":1,"FileName":"Missing/Script","Location":{"x":1,"y":1}}"""),
    Entity(ContentEntityTypes.Drop, 1, "drop.monster.1",
        """{"monsterIndex":1,"monsterName":"Hen","drops":[{"Chance":1,"Item":{"$ref":"item","index":999},"Type":0}]}"""),
    Entity(ContentEntityTypes.Recipe, 1, "recipe.Sword",
        """{"name":"Sword","output":{"itemIndex":1,"itemName":"Sword"},"ingredients":[],"tools":[],"chance":100,"gold":0}"""),
    Entity(ContentEntityTypes.Recipe, 2, "recipe.SwordDuplicate",
        """{"name":"SwordDuplicate","output":{"itemIndex":1,"itemName":"SwordDuplicate"},"ingredients":[],"tools":[],"chance":100,"gold":0}""")
};
var semanticIssues = ContentValidationRules.ValidateEntitySemantics(entities).ToArray();
Check(semanticIssues.Any(issue => issue.Code == "reference-missing" && issue.ContentKey == "npc.1"),
    "NPC script references must be checked against script entities.");
Check(semanticIssues.Any(issue => issue.Code == "reference-missing" && issue.ContentKey == "drop.monster.1"),
    "Drop item references must be checked against item entities.");
Check(semanticIssues.Any(issue => issue.Code == "recipe-duplicate"), "Duplicate recipe output must be blocked.");

var map = new MapInfo { Index = 7, FileName = "draft-map" };
var respawn = new RespawnInfo
{
    MonsterIndex = 1,
    Location = new Point(3, 4),
    Count = 2,
    Spread = 1,
    Delay = 5,
    RandomDelay = 1,
    RespawnIndex = 9
};
var projected = LegacyRespawnProjection.CreateEntity(map, respawn, 0);
Check(projected.EntityType == ContentEntityTypes.Respawn, "Respawn projection must create a respawn entity.");
Check(projected.LocalIndex == 7_000_001, "Respawn projection must use a stable map-scoped local index.");
Check(projected.ContentKey == "respawn.map7.1", "Respawn projection must use the stable content key.");
Check(projected.Data.GetProperty("mapIndex").GetInt64() == 7 &&
      projected.Data.GetProperty("MonsterIndex").GetInt64() == 1,
    "Respawn projection must preserve map and monster identity.");

var snapshotDirectory = NewTempDirectory();
try
{
    var manifest = ContentSnapshotWriter.Write(new ContentSnapshotWriteOptions
    {
        OutputDirectory = snapshotDirectory,
        ReleaseVersion = "unit-editor.1",
        Source = "unit-test",
        CreatedAt = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero)
    }, entities);
    Check(manifest.Counts[ContentEntityTypes.Recipe] == 2, "Snapshot writer must preserve recipe rows.");
    var lines = File.ReadAllLines(Path.Combine(snapshotDirectory, "entities", "recipe.jsonl"));
    Check(lines.Length == 2 && lines[0].Contains("\"contentKey\":\"recipe.Sword\"", StringComparison.Ordinal),
        "Snapshot JSONL must be stably ordered by local index.");
    var roundTrip = ContentSnapshotReader.Read(snapshotDirectory);
    Check(roundTrip.Entities.Where(entity => entity.EntityType == ContentEntityTypes.Recipe)
        .Select(entity => entity.ContentKey)
        .SequenceEqual(["recipe.Sword", "recipe.SwordDuplicate"]),
        "Snapshot JSONL export must round-trip in stable order.");
}
finally
{
    if (Directory.Exists(snapshotDirectory)) Directory.Delete(snapshotDirectory, recursive: true);
}

var materializeDirectory = NewTempDirectory();
try
{
    var materializeItem = Entity(ContentEntityTypes.Item, 1, "item.1", """{"Index":1,"Name":"Sword"}""");
    var materializeMonster = Entity(ContentEntityTypes.Monster, 1, "monster.1", """{"Index":1,"Name":"Hen"}""");
    var scriptSource = "[@MAIN]\n#SAY\nHello";
    var scriptHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scriptSource))).ToLowerInvariant();
    var materializeEntities = new[]
    {
        materializeItem,
        materializeMonster,
        Entity(ContentEntityTypes.Drop, 1, "drop.monster.1",
            """{"monsterIndex":1,"drops":[{"Chance":50,"Item":{"$ref":"item","index":1},"Type":0}]}"""),
        Entity(ContentEntityTypes.Recipe, 1, "recipe.Sword",
            """{"name":"Sword","output":{"itemIndex":1,"itemName":"Sword","count":1},"chance":100,"gold":10,"ingredients":[{"itemName":"Ore","count":2}],"tools":[]}"""),
        Entity(ContentEntityTypes.Script, 1, "script.Quest.Main",
            $$"""{"relativePath":"Envir/NPCs/Quest/Main.txt","source":"[@MAIN]\n#SAY\nHello","sha256":"{{scriptHash}}"}"""),
        Entity(ContentEntityTypes.Quest, 1, "quest.1",
            """{"Index":1,"Name":"Quest","FileName":"Quest/Main","Description":[],"TaskDescription":[],"CompletionDescription":[],"ReturnDescription":[],"CarryItems":[],"KillTasks":[],"ItemTasks":[],"FlagTasks":[],"FixedRewards":[],"SelectRewards":[],"ExpReward":0,"GoldReward":0,"CreditReward":0}""")
    };
    LegacyContentFileMaterializer.Materialize(SnapshotOf(materializeEntities), materializeDirectory);
    Check(File.Exists(Path.Combine(materializeDirectory, "Envir", "Drops", "Hen.txt")),
        "Release materialization must write drop files.");
    Check(File.ReadAllText(Path.Combine(materializeDirectory, "Envir", "Drops", "Hen.txt")).Contains("1/50 Sword"),
        "Release materialization must resolve drop item names.");
    Check(File.Exists(Path.Combine(materializeDirectory, "Envir", "Recipe", "Sword.txt")),
        "Release materialization must write recipe files.");
    Check(File.Exists(Path.Combine(materializeDirectory, "Envir", "NPCs", "Quest", "Main.txt")),
        "Release materialization must write script files.");
    Check(File.Exists(Path.Combine(materializeDirectory, "Envir", "Quests", "Quest", "Main.txt")),
        "Release materialization must write quest files.");

    var badScriptDirectory = NewTempDirectory();
    var badRecipeDirectory = NewTempDirectory();
    var badDropDirectory = NewTempDirectory();
    try
    {
        Expect<InvalidDataException>(() => LegacyContentFileMaterializer.Materialize(SnapshotOf(
            Entity(ContentEntityTypes.Script, 1, "script.Bad",
                """{"relativePath":"Bad.txt","source":"[@MAIN]","sha256":"deadbeef"}""")), badScriptDirectory),
            "Script materialization must reject a source hash mismatch.");
        Expect<InvalidDataException>(() => LegacyContentFileMaterializer.Materialize(SnapshotOf(
            Entity(ContentEntityTypes.Recipe, 1, "recipe.Bad",
                """{"name":"FileName","output":{"itemIndex":1,"itemName":"Other","count":1},"chance":100,"gold":0,"ingredients":[],"tools":[]}""")), badRecipeDirectory),
            "Recipe materialization must reject file identity drift.");
        Expect<InvalidDataException>(() => LegacyContentFileMaterializer.Materialize(SnapshotOf(
            Entity(ContentEntityTypes.Drop, 1, "drop.monster.1",
                """{"monsterIndex":1,"drops":[{"Chance":50,"Item":{"$ref":"item","index":999},"Type":0}]}""")), badDropDirectory),
            "Drop materialization must reject missing item references.");
    }
    finally
    {
        foreach (var directory in new[] { badScriptDirectory, badRecipeDirectory, badDropDirectory })
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
finally
{
    if (Directory.Exists(materializeDirectory)) Directory.Delete(materializeDirectory, recursive: true);
}

var releaseDirectory = NewTempDirectory();
var releaseContentRoot = NewTempDirectory();
var releaseUnsignedDirectory = NewTempDirectory();
var releaseTamperedDirectory = NewTempDirectory();
try
{
    var mapEntities = new[]
    {
        Entity(ContentEntityTypes.Map, 1, "map.1", """{"Index":1,"FileName":"MissingMap","Title":"Missing"}""")
    };
    ContentSnapshotWriter.Write(new ContentSnapshotWriteOptions
    {
        OutputDirectory = releaseDirectory,
        ReleaseVersion = "release-test",
        Source = "unit-test",
        CreatedAt = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero)
    }, mapEntities);
    CopyDirectory(releaseDirectory, releaseUnsignedDirectory);

    using var releaseKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var publicKeyPath = Path.Combine(releaseDirectory, "public.pem");
    File.WriteAllText(publicKeyPath, releaseKey.ExportSubjectPublicKeyInfoPem());
    ContentSnapshotSigner.SignSnapshot(
        releaseDirectory,
        releaseKey.ExportPkcs8PrivateKeyPem(),
        "release-test-key",
        new DateTimeOffset(2026, 11, 17, 1, 0, 0, TimeSpan.Zero));
    CopyDirectory(releaseDirectory, releaseTamperedDirectory);

    Expect<CryptographicException>(() => LegacyContentReleaseLoader.Prepare(
            releaseUnsignedDirectory,
            releaseContentRoot,
            publicKeyPath,
            "release-test-key"),
        "Zone release loading must reject an unsigned snapshot.");
    Expect<FileNotFoundException>(() => LegacyContentReleaseLoader.Prepare(
            releaseDirectory,
            releaseContentRoot,
            publicKeyPath,
            "release-test-key"),
        "Zone release loading must reject a missing map before rebuilding the legacy database.");
    Expect<CryptographicException>(() => LegacyContentReleaseLoader.Prepare(
            releaseDirectory,
            releaseContentRoot,
            publicKeyPath,
            "wrong-key-id"),
        "Zone release loading must enforce the expected signing key id.");

    File.AppendAllText(
        Path.Combine(releaseTamperedDirectory, "entities", "map.jsonl"),
        Environment.NewLine,
        Encoding.UTF8);
    Expect<InvalidDataException>(() => LegacyContentReleaseLoader.Prepare(
            releaseTamperedDirectory,
            releaseContentRoot,
            publicKeyPath,
            "release-test-key"),
        "Zone release loading must reject a tampered entity file.");
}
finally
{
    foreach (var directory in new[] { releaseDirectory, releaseContentRoot, releaseUnsignedDirectory, releaseTamperedDirectory })
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}

Console.WriteLine($"Content editor tests passed ({passed} checks).");

static void CopyDirectory(string source, string target)
{
    Directory.CreateDirectory(target);
    foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
    {
        Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
    }

    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }
}
