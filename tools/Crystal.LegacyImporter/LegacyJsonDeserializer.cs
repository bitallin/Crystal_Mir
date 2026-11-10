using System.Collections;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Crystal.LegacyImporter;

public sealed class LegacyJsonDeserializer
{
    private readonly IReadOnlyDictionary<(string EntityType, long Index), object> _references;
    private readonly List<object> _path = new();
    private readonly Dictionary<(Type Type, string Name), MemberAccessor?> _members = new();

    public LegacyJsonDeserializer(IReadOnlyDictionary<(string EntityType, long Index), object> references)
    {
        _references = references;
    }

    public object Deserialize(JsonElement element, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        var value = ReadValue(element, targetType);
        return value ?? throw new InvalidDataException(
            $"JSON null cannot be assigned to required type {targetType.FullName}.");
    }

    public void DeserializeInto(JsonElement element, object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (element.ValueKind != JsonValueKind.Object || IsReference(element) || IsCycle(element))
        {
            throw new InvalidDataException(
                $"Expected an object payload for {target.GetType().FullName}, got {element.ValueKind}.");
        }

        _path.Add(target);
        try
        {
            PopulateObject(element, target);
        }
        finally
        {
            _path.RemoveAt(_path.Count - 1);
        }
    }

    private object? ReadValue(JsonElement element, Type targetType)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var nullableType = Nullable.GetUnderlyingType(targetType);
        if (nullableType is not null)
        {
            return ReadValue(element, nullableType);
        }

        if (IsReference(element))
        {
            return ResolveReference(element, targetType);
        }

        if (IsCycle(element))
        {
            return ResolveCycle(targetType);
        }

        if (targetType == typeof(string))
        {
            return element.GetString();
        }

        if (targetType == typeof(char))
        {
            var text = element.GetString();
            if (string.IsNullOrEmpty(text)) return '\0';
            if (text.Length != 1)
            {
                throw new InvalidDataException($"Cannot convert '{text}' to a character.");
            }

            return text[0];
        }

        if (targetType == typeof(bool))
        {
            return element.GetBoolean();
        }

        if (targetType.IsEnum)
        {
            var text = element.GetString()
                ?? throw new InvalidDataException($"Expected enum string for {targetType.FullName}.");
            return Enum.Parse(targetType, text, ignoreCase: false);
        }

        if (targetType == typeof(Guid))
        {
            return Guid.Parse(element.GetString()!);
        }

        if (targetType == typeof(DateTime))
        {
            return DateTime.Parse(
                element.GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
        }

        if (targetType == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(
                element.GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
        }

        if (targetType == typeof(TimeSpan))
        {
            return TimeSpan.Parse(element.GetString()!, CultureInfo.InvariantCulture);
        }

        if (targetType == typeof(byte[]))
        {
            return element.GetBytesFromBase64();
        }

        if (ReadNumber(element, targetType, out var number))
        {
            return number;
        }

        if (targetType == typeof(Point)) return ReadPoint(element);
        if (targetType == typeof(PointF)) return ReadPointF(element);
        if (targetType == typeof(Size)) return ReadSize(element);
        if (targetType == typeof(Rectangle)) return ReadRectangle(element);
        if (targetType == typeof(Color)) return ReadColor(element);

        if (targetType.IsArray)
        {
            return ReadArray(element, targetType);
        }

        if (typeof(IDictionary).IsAssignableFrom(targetType))
        {
            return ReadDictionary(element, targetType);
        }

        if (typeof(IList).IsAssignableFrom(targetType))
        {
            return ReadCollection(element, targetType);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Expected an object for {targetType.FullName}, got {element.ValueKind}.");
        }

        var instance = CreateInstance(targetType);
        _path.Add(instance);
        try
        {
            PopulateObject(element, instance);
        }
        finally
        {
            _path.RemoveAt(_path.Count - 1);
        }

        return instance;
    }

    private void PopulateObject(JsonElement element, object target)
    {
        var type = target.GetType();
        foreach (var property in element.EnumerateObject())
        {
            var member = FindMember(type, property.Name)
                ?? throw new InvalidDataException(
                    $"Unknown serialized member '{type.FullName}.{property.Name}'.");
            var value = ReadValue(property.Value, member.MemberType);
            member.SetValue(target, value);
        }
    }

    private object ReadArray(JsonElement element, Type arrayType)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Expected array for {arrayType.FullName}, got {element.ValueKind}.");
        }

        var elementType = arrayType.GetElementType()
            ?? throw new InvalidDataException($"Array type has no element type: {arrayType.FullName}.");
        var items = element.EnumerateArray().ToArray();
        var array = Array.CreateInstance(elementType, items.Length);
        for (var index = 0; index < items.Length; index++)
        {
            array.SetValue(ReadValue(items[index], elementType), index);
        }

        return array;
    }

    private object ReadDictionary(JsonElement element, Type dictionaryType)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Expected object for dictionary {dictionaryType.FullName}, got {element.ValueKind}.");
        }

        var dictionary = (IDictionary)CreateInstance(dictionaryType);
        var arguments = GetDictionaryArguments(dictionaryType);
        var keyType = arguments[0];
        var valueType = arguments[1];
        foreach (var property in element.EnumerateObject())
        {
            var key = ConvertDictionaryKey(property.Name, keyType);
            dictionary.Add(key, ReadValue(property.Value, valueType));
        }

        return dictionary;
    }

    private object ReadCollection(JsonElement element, Type collectionType)
    {
        var collection = CreateInstance(collectionType);
        if (element.ValueKind == JsonValueKind.Array)
        {
            PopulateCollectionItems(element, collection, collectionType);
            return collection;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"Expected array or custom collection object for {collectionType.FullName}, got {element.ValueKind}.");
        }

        _path.Add(collection);
        try
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("items"))
                {
                    PopulateCollectionItems(property.Value, collection, collectionType);
                    continue;
                }

                var member = FindMember(collectionType, property.Name)
                    ?? throw new InvalidDataException(
                        $"Unknown serialized member '{collectionType.FullName}.{property.Name}'.");
                member.SetValue(collection, ReadValue(property.Value, member.MemberType));
            }
        }
        finally
        {
            _path.RemoveAt(_path.Count - 1);
        }

        return collection;
    }

    private void PopulateCollectionItems(JsonElement element, object collection, Type collectionType)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Expected items array for {collectionType.FullName}, got {element.ValueKind}.");
        }

        var itemType = GetCollectionItemType(collectionType)
            ?? throw new InvalidDataException($"Cannot determine collection item type for {collectionType.FullName}.");
        var list = (IList)collection;
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ReadValue(item, itemType));
        }
    }

    private object ResolveReference(JsonElement element, Type targetType)
    {
        var entityType = element.GetProperty("$ref").GetString()
            ?? throw new InvalidDataException("Reference has no entity type.");
        var index = element.GetProperty("index").GetInt64();
        if (!_references.TryGetValue((entityType, index), out var target))
        {
            throw new InvalidDataException($"Unresolved content reference {entityType}.{index}.");
        }

        if (!targetType.IsInstanceOfType(target))
        {
            throw new InvalidDataException(
                $"Reference {entityType}.{index} resolves to {target.GetType().FullName}, " +
                $"which is not assignable to {targetType.FullName}.");
        }

        return target;
    }

    private object ResolveCycle(Type targetType)
    {
        for (var index = _path.Count - 1; index >= 0; index--)
        {
            if (targetType.IsInstanceOfType(_path[index])) return _path[index];
        }

        throw new InvalidDataException(
            $"Cannot resolve $cycle for {targetType.FullName}; no enclosing object of that type exists.");
    }

    private static bool IsReference(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$ref", out _);
    }

    private static bool IsCycle(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty("$cycle", out var cycle) &&
               cycle.ValueKind == JsonValueKind.True;
    }

    private MemberAccessor? FindMember(Type type, string name)
    {
        var key = (type, name);
        if (_members.TryGetValue(key, out var cached)) return cached;

        MemberAccessor? accessor = null;
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (field is not null &&
            !field.IsStatic &&
            !field.IsDefined(typeof(NonSerializedAttribute), inherit: false))
        {
            accessor = MemberAccessor.ForField(field);
        }
        else
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is not null &&
                property.GetIndexParameters().Length == 0 &&
                property.SetMethod is { IsPublic: true } setter)
            {
                accessor = MemberAccessor.ForProperty(property, setter);
            }
        }

        _members[key] = accessor;
        return accessor;
    }

    private static object CreateInstance(Type type)
    {
        if (type.IsValueType)
        {
            return Activator.CreateInstance(type)!;
        }

        var constructor = type.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);
        if (constructor is not null)
        {
            return constructor.Invoke(null);
        }

        return RuntimeHelpers.GetUninitializedObject(type);
    }

    private static Type[] GetDictionaryArguments(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType) continue;
            var definition = current.GetGenericTypeDefinition();
            if (definition == typeof(Dictionary<,>) ||
                definition == typeof(SortedDictionary<,>) ||
                definition == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>))
            {
                return current.GetGenericArguments();
            }
        }

        var dictionaryInterface = type.GetInterfaces()
            .FirstOrDefault(candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictionaryInterface is not null)
        {
            return dictionaryInterface.GetGenericArguments();
        }

        throw new InvalidDataException($"Cannot determine dictionary key/value types for {type.FullName}.");
    }

    private static Type? GetCollectionItemType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            return type.GetGenericArguments()[0];
        }

        var listInterface = type.GetInterfaces()
            .FirstOrDefault(candidate =>
                candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IList<>));
        return listInterface?.GetGenericArguments()[0];
    }

    private static object ConvertDictionaryKey(string value, Type keyType)
    {
        if (keyType == typeof(string)) return value;
        if (keyType.IsEnum) return Enum.Parse(keyType, value, ignoreCase: false);

        return Convert.ChangeType(value, keyType, CultureInfo.InvariantCulture)
            ?? throw new InvalidDataException($"Cannot convert dictionary key '{value}' to {keyType.FullName}.");
    }

    private static bool ReadNumber(JsonElement element, Type targetType, out object value)
    {
        value = null!;
        if (element.ValueKind != JsonValueKind.Number) return false;

        switch (Type.GetTypeCode(targetType))
        {
            case TypeCode.SByte:
                value = element.GetSByte();
                return true;
            case TypeCode.Byte:
                value = element.GetByte();
                return true;
            case TypeCode.Int16:
                value = element.GetInt16();
                return true;
            case TypeCode.UInt16:
                value = element.GetUInt16();
                return true;
            case TypeCode.Int32:
                value = element.GetInt32();
                return true;
            case TypeCode.UInt32:
                value = element.GetUInt32();
                return true;
            case TypeCode.Int64:
                value = element.GetInt64();
                return true;
            case TypeCode.UInt64:
                value = element.GetUInt64();
                return true;
            case TypeCode.Single:
                value = element.GetSingle();
                return true;
            case TypeCode.Double:
                value = element.GetDouble();
                return true;
            case TypeCode.Decimal:
                value = element.GetDecimal();
                return true;
            default:
                return false;
        }
    }

    private static Point ReadPoint(JsonElement element)
    {
        return new Point(
            element.GetProperty("x").GetInt32(),
            element.GetProperty("y").GetInt32());
    }

    private static PointF ReadPointF(JsonElement element)
    {
        return new PointF(
            element.GetProperty("x").GetSingle(),
            element.GetProperty("y").GetSingle());
    }

    private static Size ReadSize(JsonElement element)
    {
        return new Size(
            element.GetProperty("width").GetInt32(),
            element.GetProperty("height").GetInt32());
    }

    private static Rectangle ReadRectangle(JsonElement element)
    {
        return new Rectangle(
            element.GetProperty("x").GetInt32(),
            element.GetProperty("y").GetInt32(),
            element.GetProperty("width").GetInt32(),
            element.GetProperty("height").GetInt32());
    }

    private static Color ReadColor(JsonElement element)
    {
        return Color.FromArgb(
            element.GetProperty("a").GetInt32(),
            element.GetProperty("r").GetInt32(),
            element.GetProperty("g").GetInt32(),
            element.GetProperty("b").GetInt32());
    }

    private sealed class MemberAccessor
    {
        private readonly FieldInfo? _field;
        private readonly PropertyInfo? _property;
        private readonly MethodInfo? _setter;

        private MemberAccessor(FieldInfo field)
        {
            _field = field;
            MemberType = field.FieldType;
        }

        private MemberAccessor(PropertyInfo property, MethodInfo setter)
        {
            _property = property;
            _setter = setter;
            MemberType = property.PropertyType;
        }

        public Type MemberType { get; }

        public static MemberAccessor ForField(FieldInfo field) => new(field);

        public static MemberAccessor ForProperty(PropertyInfo property, MethodInfo setter) =>
            new(property, setter);

        public void SetValue(object target, object? value)
        {
            if (_field is not null)
            {
                _field.SetValue(target, value);
                return;
            }

            _setter!.Invoke(target, new[] { value });
        }
    }
}
