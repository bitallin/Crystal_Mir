using System.Security.Cryptography;
using System.Text.Json;
using Server;
using Server.MirEnvir;
using Shared;

var dbPath = args.FirstOrDefault(arg => !arg.StartsWith('-')) ?? "Server.MirDB";
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Database not found: {dbPath}");
    return 2;
}

var fullPath = Path.GetFullPath(dbPath);
var tempDirectory = Path.Combine(Path.GetTempPath(), $"crystal-dbinspect-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDirectory);

int headerVersion;
int headerCustomVersion;
using (var headerStream = File.OpenRead(fullPath))
using (var headerReader = new BinaryReader(headerStream))
{
    headerVersion = headerReader.ReadInt32();
    headerCustomVersion = headerReader.ReadInt32();
}

var originalDirectory = Environment.CurrentDirectory;
try
{
    File.Copy(fullPath, Path.Combine(tempDirectory, "Server.MirDB"), true);
    Environment.CurrentDirectory = tempDirectory;
    Packet.IsServer = true;
    Settings.Load();

    var envir = Envir.Edit;
    if (!envir.LoadDB())
    {
        Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            error = "LoadDB returned false. The database version may be unsupported or corrupted.",
            file = fullPath,
            headerVersion,
            headerCustomVersion,
            minVersion = Envir.MinVersion,
            currentVersion = Envir.Version
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 3;
    }

    using var hashStream = File.OpenRead(fullPath);
    var summary = new
    {
        file = fullPath,
        sizeBytes = new FileInfo(fullPath).Length,
        sha256 = Convert.ToHexString(SHA256.HashData(hashStream)).ToLowerInvariant(),
        headerVersion,
        headerCustomVersion,
        loadVersion = Envir.LoadVersion,
        loadCustomVersion = Envir.LoadCustomVersion,
        currentVersion = Envir.Version,
        indices = new
        {
            map = envir.MapIndex,
            item = envir.ItemIndex,
            monster = envir.MonsterIndex,
            npc = envir.NPCIndex,
            quest = envir.QuestIndex,
            gameshop = envir.GameshopIndex,
            conquest = envir.ConquestIndex,
            respawn = envir.RespawnIndex
        },
        counts = new
        {
            maps = envir.MapInfoList.Count,
            items = envir.ItemInfoList.Count,
            monsters = envir.MonsterInfoList.Count,
            magics = envir.MagicInfoList.Count,
            npcs = envir.NPCInfoList.Count,
            quests = envir.QuestInfoList.Count,
            gameshop = envir.GameShopList.Count,
            recipes = envir.RecipeInfoList.Count,
            buffs = envir.BuffInfoList.Count,
            conquests = envir.ConquestInfoList.Count,
            gtMaps = envir.GTMapList.Count
        },
        dragon = envir.DragonInfo is null
            ? null
            : new
            {
                envir.DragonInfo.Enabled,
                envir.DragonInfo.MapFileName,
                envir.DragonInfo.MonsterName,
                envir.DragonInfo.BodyName,
                envir.DragonInfo.Level
            }
    };

    Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
finally
{
    Environment.CurrentDirectory = originalDirectory;
    try { Directory.Delete(tempDirectory, true); } catch { }
}
