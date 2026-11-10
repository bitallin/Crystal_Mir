using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Crystal.Content;
using Server.Library.MirDatabase;
using Server.MirDatabase;

namespace Crystal.LegacyImporter;

public static class LegacyJsonSerializer
{
    private const int MaxDepth = 16;

    private static readonly Dictionary<Type, ReferenceMapping> ReferenceMappings = new()
    {
        [typeof(ItemInfo)] = new(ContentEntityTypes.Item, value => ((ItemInfo)value).Index),
        [typeof(MonsterInfo)] = new(ContentEntityTypes.Monster, value => ((MonsterInfo)value).Index),
        [typeof(MapInfo)] = new(ContentEntityTypes.Map, value => ((MapInfo)value).Index),
        [typeof(NPCInfo)] = new(ContentEntityTypes.Npc, value => ((NPCInfo)value).Index),
        [typeof(QuestInfo)] = new(ContentEntityTypes.Quest, value => ((QuestInfo)value).Index),
        [typeof(MagicInfo)] = new(ContentEntityTypes.Magic, value => (long)((MagicInfo)value).Spell),
        [typeof(ConquestInfo)] = new(ContentEntityTypes.Conquest, value => ((ConquestInfo)value).Index),
        [typeof(GTMap)] = new("gtmap", value => ((GTMap)value).Index)
    };

    private static readonly Dictionary<Type, HashSet<string>> ExplicitProperties = new()
    {
        [typeof(QuestInfo)] = new(StringComparer.Ordinal) { nameof(QuestInfo.FinishNpcIndex) }
    };

    public static JsonElement Serialize(object value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            WriteValue(writer, value, depth: 0, path: new HashSet<object>(ReferenceEqualityComparer.Instance), isRoot: true);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return ContentJson.Canonicalize(document.RootElement);
    }

    private static void WriteValue(
        Utf8JsonWriter writer,
        object? value,
        int depth,
        HashSet<object> path,
        bool isRoot = false)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (depth > MaxDepth)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value)
        {
            case string text:
                writer.WriteStringValue(text);
                return;
            case char character:
                writer.WriteStringValue(character.ToString());
                return;
            case bool boolean:
                writer.WriteBooleanValue(boolean);
                return;
            case Enum enumValue:
                writer.WriteStringValue(enumValue.ToString());
                return;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                return;
            case DateTime dateTime:
                writer.WriteStringValue(dateTime.ToString("O", CultureInfo.InvariantCulture));
                return;
            case DateTimeOffset dateTimeOffset:
                writer.WriteStringValue(dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
                return;
            case TimeSpan timeSpan:
                writer.WriteStringValue(timeSpan.ToString("c", CultureInfo.InvariantCulture));
                return;
            case Guid guid:
                writer.WriteStringValue(guid.ToString("D"));
                return;
        }

        var type = value.GetType();
        if (WritePrimitiveNumber(writer, value, type)) return;

        if (type.FullName == "System.Drawing.Point")
        {
            WriteDrawingPoint(writer, value);
            return;
        }

        if (type.FullName == "System.Drawing.PointF")
        {
            WriteDrawingPointF(writer, value);
            return;
        }

        if (type.FullName == "System.Drawing.Size")
        {
            WriteDrawingSize(writer, value);
            return;
        }

        if (type.FullName == "System.Drawing.Rectangle")
        {
            WriteDrawingRectangle(writer, value);
            return;
        }

        if (type.FullName == "System.Drawing.Color")
        {
            WriteDrawingColor(writer, value);
            return;
        }

        if (!isRoot && ReferenceMappings.TryGetValue(type, out var mapping))
        {
            var index = mapping.Index(value);
            writer.WriteStartObject();
            writer.WriteString("$ref", mapping.EntityType);
            writer.WriteNumber("index", index);
            writer.WriteString("key", $"{mapping.EntityType}.{index}");
            writer.WriteEndObject();
            return;
        }

        if (value is IDictionary dictionary)
        {
            WriteDictionary(writer, dictionary, depth, path);
            return;
        }

        if (value is IEnumerable enumerable)
        {
            if (HasSerializableMembers(type))
            {
                WriteCustomCollection(writer, enumerable, value, type, depth, path);
            }
            else
            {
                WriteArray(writer, enumerable, depth, path);
            }
            return;
        }

        WriteObject(writer, value, type, depth, path);
    }

    private static void WriteObject(
        Utf8JsonWriter writer,
        object value,
        Type type,
        int depth,
        HashSet<object> path)
    {
        if (!path.Add(value))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("$cycle", true);
            writer.WriteEndObject();
            return;
        }

        try
        {
            writer.WriteStartObject();
            var written = new HashSet<string>(StringComparer.Ordinal);

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                         .Where(field => !field.IsStatic)
                         .Where(field => !field.IsDefined(typeof(NonSerializedAttribute), inherit: false))
                         .OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                if (!written.Add(field.Name)) continue;
                var fieldValue = field.GetValue(value);
                if (fieldValue is null) continue;
                writer.WritePropertyName(field.Name);
                WriteValue(writer, fieldValue, depth + 1, path);
            }

            foreach (var property in GetAutoProperties(type).OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (!written.Add(property.Name)) continue;
                writer.WritePropertyName(property.Name);
                WriteValue(writer, property.GetValue(value), depth + 1, path);
            }

            if (ExplicitProperties.TryGetValue(type, out var explicitProperties))
            {
                foreach (var propertyName in explicitProperties.OrderBy(name => name, StringComparer.Ordinal))
                {
                    if (!written.Add(propertyName)) continue;
                    var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                    if (property is null) continue;
                    writer.WritePropertyName(propertyName);
                    WriteValue(writer, property.GetValue(value), depth + 1, path);
                }
            }

            writer.WriteEndObject();
        }
        finally
        {
            path.Remove(value);
        }
    }

    private static void WriteDictionary(
        Utf8JsonWriter writer,
        IDictionary dictionary,
        int depth,
        HashSet<object> path)
    {
        writer.WriteStartObject();
        var entries = new List<(string Key, object? Value)>();
        foreach (DictionaryEntry entry in dictionary)
        {
            entries.Add((Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty, entry.Value));
        }

        foreach (var (key, value) in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(key);
            WriteValue(writer, value, depth + 1, path);
        }

        writer.WriteEndObject();
    }

    private static void WriteArray(
        Utf8JsonWriter writer,
        IEnumerable enumerable,
        int depth,
        HashSet<object> path)
    {
        writer.WriteStartArray();
        foreach (var item in enumerable)
        {
            WriteValue(writer, item, depth + 1, path);
        }

        writer.WriteEndArray();
    }

    private static void WriteCustomCollection(
        Utf8JsonWriter writer,
        IEnumerable enumerable,
        object value,
        Type type,
        int depth,
        HashSet<object> path)
    {
        if (!path.Add(value))
        {
            writer.WriteNullValue();
            return;
        }

        try
        {
            writer.WriteStartObject();
            var written = new HashSet<string>(StringComparer.Ordinal) { "items" };
            writer.WritePropertyName("items");
            WriteArray(writer, enumerable, depth + 1, path);

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                         .Where(field => !field.IsStatic)
                         .OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                if (!written.Add(field.Name)) continue;
                var fieldValue = field.GetValue(value);
                if (fieldValue is null) continue;
                writer.WritePropertyName(field.Name);
                WriteValue(writer, fieldValue, depth + 1, path);
            }

            foreach (var property in GetAutoProperties(type).OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (!written.Add(property.Name)) continue;
                writer.WritePropertyName(property.Name);
                WriteValue(writer, property.GetValue(value), depth + 1, path);
            }

            writer.WriteEndObject();
        }
        finally
        {
            path.Remove(value);
        }
    }

    private static IEnumerable<PropertyInfo> GetAutoProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null)
            .Where(property => property.GetIndexParameters().Length == 0)
            .Where(property => property.DeclaringType?.GetField(
                $"<{property.Name}>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance) is not null);
    }

    private static bool HasSerializableMembers(Type type)
    {
        if (type.GetFields(BindingFlags.Public | BindingFlags.Instance).Any(field => !field.IsStatic))
        {
            return true;
        }

        return GetAutoProperties(type).Any();
    }

    private static bool WritePrimitiveNumber(Utf8JsonWriter writer, object value, Type type)
    {
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.Int64:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return true;
            case TypeCode.Byte:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
            case TypeCode.UInt64:
                writer.WriteNumberValue(Convert.ToUInt64(value, CultureInfo.InvariantCulture));
                return true;
            case TypeCode.Single:
            {
                var single = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                if (float.IsFinite(single)) writer.WriteNumberValue(single);
                else writer.WriteNullValue();
                return true;
            }
            case TypeCode.Double:
            {
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsFinite(number)) writer.WriteNumberValue(number);
                else writer.WriteNullValue();
                return true;
            }
            case TypeCode.Decimal:
                writer.WriteNumberValue(Convert.ToDecimal(value, CultureInfo.InvariantCulture));
                return true;
            default:
                return false;
        }
    }

    private static void WriteDrawingPoint(Utf8JsonWriter writer, object value)
    {
        var type = value.GetType();
        writer.WriteStartObject();
        writer.WriteNumber("x", Convert.ToInt32(type.GetProperty("X")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteNumber("y", Convert.ToInt32(type.GetProperty("Y")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteDrawingPointF(Utf8JsonWriter writer, object value)
    {
        var type = value.GetType();
        writer.WriteStartObject();
        writer.WriteNumber("x", Convert.ToSingle(type.GetProperty("X")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteNumber("y", Convert.ToSingle(type.GetProperty("Y")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteDrawingSize(Utf8JsonWriter writer, object value)
    {
        var type = value.GetType();
        writer.WriteStartObject();
        writer.WriteNumber("width", Convert.ToInt32(type.GetProperty("Width")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteNumber("height", Convert.ToInt32(type.GetProperty("Height")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteDrawingRectangle(Utf8JsonWriter writer, object value)
    {
        var type = value.GetType();
        writer.WriteStartObject();
        writer.WriteNumber("x", Convert.ToInt32(type.GetProperty("X")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteNumber("y", Convert.ToInt32(type.GetProperty("Y")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteNumber("width", Convert.ToInt32(type.GetProperty("Width")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteNumber("height", Convert.ToInt32(type.GetProperty("Height")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static void WriteDrawingColor(Utf8JsonWriter writer, object value)
    {
        var type = value.GetType();
        writer.WriteStartObject();
        writer.WriteNumber("a", Convert.ToInt32(type.GetProperty("A")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteNumber("r", Convert.ToInt32(type.GetProperty("R")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteNumber("g", Convert.ToInt32(type.GetProperty("G")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteNumber("b", Convert.ToInt32(type.GetProperty("B")!.GetValue(value), CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private sealed record ReferenceMapping(string EntityType, Func<object, long> Index);
}
