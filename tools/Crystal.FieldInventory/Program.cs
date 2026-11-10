using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Server;
using Shared;

var outputDirectory = Path.GetFullPath(args.FirstOrDefault() ?? "docs/reference");
Directory.CreateDirectory(outputDirectory);

var sharedAssembly = typeof(Packet).Assembly;
var serverAssembly = Assembly.Load("Server.Library");
var originalDirectory = Environment.CurrentDirectory;
var tempDirectory = Path.Combine(Path.GetTempPath(), $"crystal-inventory-{Guid.NewGuid():N}");
Directory.CreateDirectory(tempDirectory);

try
{
    // Settings' static initializer creates Configs/. Keep generated files out of the repo.
    Environment.CurrentDirectory = tempDirectory;

    var content = BuildContentSchema(serverAssembly, sharedAssembly);
    var packets = BuildPacketCatalog(sharedAssembly);
    var config = BuildConfigKeys();

    File.WriteAllText(Path.Combine(outputDirectory, "content-schema.md"), content, Encoding.UTF8);
    File.WriteAllText(Path.Combine(outputDirectory, "packet-catalog.md"), packets, Encoding.UTF8);
    File.WriteAllText(Path.Combine(outputDirectory, "config-keys.md"), config, Encoding.UTF8);

    Console.WriteLine($"Wrote content-schema.md, packet-catalog.md, config-keys.md to {outputDirectory}");
}
finally
{
    Environment.CurrentDirectory = originalDirectory;
    try { Directory.Delete(tempDirectory, true); } catch { }
}

static string BuildContentSchema(Assembly serverAssembly, Assembly sharedAssembly)
{
    var sb = new StringBuilder();
    sb.AppendLine("# 内容字段清单（生成）");
    sb.AppendLine();
    sb.AppendLine($"> 生成时间：{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
    sb.AppendLine("> 生成工具：`tools/Crystal.FieldInventory`");
    sb.AppendLine("> 来源：`Server.Library` + `Shared`");
    sb.AppendLine();
    sb.AppendLine("本文件由反射生成，用于迁移和后台建模参考。字段名和类型以当前代码为准；修改实体后重新生成。");
    sb.AppendLine();
    sb.AppendLine("## 实体");

    var typeNames = new[]
    {
        "ItemInfo",
        "GameShopItem",
        "Server.MirDatabase.MonsterInfo",
        "Server.MirDatabase.MagicInfo",
        "Server.MirDatabase.MapInfo",
        "Server.MirDatabase.NPCInfo",
        "Server.MirDatabase.QuestInfo",
        "Server.MirDatabase.RecipeInfo",
        "Server.MirDatabase.BuffInfo",
        "Server.MirDatabase.ConquestInfo",
        "Server.MirDatabase.DragonInfo",
        "MineSet",
        "Server.MirDatabase.RespawnInfo",
        "SafeZoneInfo",
        "Server.MirDatabase.MovementInfo",
        "Server.MirDatabase.AccountInfo",
        "Server.MirDatabase.CharacterInfo",
        "Server.MirDatabase.GuildInfo",
        "Server.MirDatabase.HeroInfo",
        "Server.MirDatabase.IntelligentCreatureInfo",
        "Server.MirEnvir.MailInfo",
        "Server.MirDatabase.AuctionInfo",
        "Server.Library.MirDatabase.GTMap",
        "Server.MirDatabase.QuestProgressInfo",
        "Server.MirDatabase.ConquestGuildInfo"
    };

    foreach (var typeName in typeNames)
    {
        var type = FindType(typeName, serverAssembly, sharedAssembly);
        if (type is null)
        {
            sb.AppendLine();
            sb.AppendLine($"## `{typeName}`");
            sb.AppendLine();
            sb.AppendLine("未找到该类型，可能已重命名或移动。");
            continue;
        }

        sb.AppendLine();
        sb.AppendLine($"## `{type.Name}`");
        sb.AppendLine();
        sb.AppendLine($"- 类型：`{type.FullName}`");
        sb.AppendLine($"- 程序集：`{type.Assembly.GetName().Name}`");
        sb.AppendLine($"- 基类：`{type.BaseType?.Name ?? "无"}`");
        sb.AppendLine();
        sb.AppendLine("| 种类 | 字段/属性 | 类型 |");
        sb.AppendLine("| --- | --- | --- |");

        foreach (var member in GetMembers(type))
        {
            sb.AppendLine($"| {member.Kind} | `{member.Name}` | `{FormatType(member.Type)}` |");
        }
    }

    return sb.ToString();
}

static string BuildPacketCatalog(Assembly sharedAssembly)
{
    var sb = new StringBuilder();
    sb.AppendLine("# 协议包目录（生成）");
    sb.AppendLine();
    sb.AppendLine($"> 生成时间：{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
    sb.AppendLine("> 生成工具：`tools/Crystal.FieldInventory`");
    sb.AppendLine("> 来源：`Shared/Packet.cs`、`Shared/ClientPackets.cs`、`Shared/ServerPackets.cs`");
    sb.AppendLine();
    sb.AppendLine("本文件由反射生成。`Index` 来自各包类的 `Index` 属性；字段列表不包含 `Index` 和继承成员。");

    var packets = new List<PacketInfo>();
    foreach (var type in SafeGetTypes(sharedAssembly))
    {
        if (!typeof(Packet).IsAssignableFrom(type) || type.IsAbstract)
            continue;

        try
        {
            var instance = (Packet)RuntimeHelpers.GetUninitializedObject(type);
            var index = instance.Index;
            var direction = type.Namespace?.StartsWith("ServerPackets", StringComparison.Ordinal) == true
                ? "服务端 -> 客户端"
                : "客户端 -> 服务端";

            packets.Add(new PacketInfo(direction, index, type, GetMembers(type)));
        }
        catch
        {
            // Skip packets that cannot report an index without initialized state.
        }
    }

    foreach (var group in packets.GroupBy(p => p.Direction).OrderBy(g => g.Key))
    {
        sb.AppendLine();
        sb.AppendLine($"## {group.Key}");
        sb.AppendLine();
        sb.AppendLine("| Index | Packet | 字段 |");
        sb.AppendLine("| --- | --- | --- |");

        foreach (var packet in group.OrderBy(p => p.Index).ThenBy(p => p.Type.Name))
        {
            var fields = string.Join(", ", packet.Members.Select(m => $"{m.Name}:{FormatType(m.Type)}"));
            if (fields.Length == 0) fields = "-";
            sb.AppendLine($"| {packet.Index} | `{packet.Type.Name}` | {Escape(fields)} |");
        }
    }

    return sb.ToString();
}

static string BuildConfigKeys()
{
    var sb = new StringBuilder();
    sb.AppendLine("# 服务端配置项（生成）");
    sb.AppendLine();
    sb.AppendLine($"> 生成时间：{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
    sb.AppendLine("> 生成工具：`tools/Crystal.FieldInventory`");
    sb.AppendLine("> 来源：`Server/Settings.cs`");
    sb.AppendLine();
    sb.AppendLine("本文件列出 `Server.Settings` 的公开静态字段和常量。部分字段由 `Configs/Setup.ini` 加载；精确的 section/key 映射后续由配置 schema 生成。");
    sb.AppendLine();
    sb.AppendLine("| 类型 | 名称 | 当前值/默认值 |");
    sb.AppendLine("| --- | --- | --- |");

    var settingsType = typeof(Settings);
    foreach (var field in settingsType
                 .GetFields(BindingFlags.Public | BindingFlags.Static)
                 .OrderBy(f => f.Name, StringComparer.Ordinal))
    {
        object value;
        try { value = field.GetValue(null); }
        catch { value = "<unavailable>"; }

        sb.AppendLine($"| `{FormatType(field.FieldType)}` | `{field.Name}` | {Escape(FormatValue(value))} |");
    }

    return sb.ToString();
}

static Type FindType(string name, params Assembly[] assemblies)
{
    foreach (var assembly in assemblies)
    {
        var type = assembly.GetType(name);
        if (type is not null) return type;
    }

    var simpleName = name.Split('.').Last();
    foreach (var assembly in assemblies)
    {
        var type = SafeGetTypes(assembly).FirstOrDefault(t => t.Name == simpleName);
        if (type is not null) return type;
    }

    return null;
}

static IEnumerable<Type> SafeGetTypes(Assembly assembly)
{
    try { return assembly.GetTypes(); }
    catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
}

static IEnumerable<InventoryMember> GetMembers(Type type)
{
    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(f => !f.IsSpecialName)
        .Select(f => new InventoryMember(f.Name, f.FieldType, "字段"));

    var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(p => p.GetMethod is not null && p.GetIndexParameters().Length == 0)
        .Where(p => p.Name is not ("Index" or "Length" or "Observable" or "Compressed"))
        .Select(p => new InventoryMember(
            p.Name,
            p.PropertyType,
            p.SetMethod is { IsPublic: true } ? "属性" : "只读属性"));

    return fields.Concat(properties).OrderBy(m => m.Name, StringComparer.Ordinal);
}

static string FormatType(Type type)
{
    if (type.IsGenericType)
    {
        var name = type.Name.Split('`')[0];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
    }

    if (type.IsArray)
        return $"{FormatType(type.GetElementType()!)}[]";

    var nullable = Nullable.GetUnderlyingType(type);
    if (nullable is not null)
        return $"{FormatType(nullable)}?";

    return type == typeof(string) ? "string" : type.Name;
}

static string FormatValue(object value)
{
    if (value is null) return "null";
    if (value is string text) return $"\"{text}\"";
    if (value is bool flag) return flag ? "true" : "false";
    if (value is Enum) return value.ToString();
    if (value is System.Collections.IEnumerable and not string)
        return value.GetType().Name;
    return value.ToString();
}

static string Escape(string text) => text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

record InventoryMember(string Name, Type Type, string Kind);
record PacketInfo(string Direction, short Index, Type Type, IEnumerable<InventoryMember> Members);
