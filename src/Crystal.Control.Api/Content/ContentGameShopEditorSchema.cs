using System.Text.Json;

namespace Crystal.Control.Api.Content;

public static class ContentGameShopEditorSchema
{
    public const int SchemaVersion = 1;
    public const string EntityType = "gameshop";

    private static readonly IReadOnlyList<ContentEditorFieldDto> Fields =
    [
        ContentEditorValidation.Field("GIndex", "商城索引", "readonly", required: true, readOnly: true, defaultValue: 0),
        ContentEditorValidation.Field("ItemIndex", "物品索引", "reference", required: true, referenceType: "item", storeAs: "index", defaultValue: 0),
        ContentEditorValidation.Field("Info", "物品引用", "reference", required: true, referenceType: "item", storeAs: "ref", defaultValue: null),
        ContentEditorValidation.Field("GoldPrice", "金币价格", "integer", required: true, defaultValue: 0, min: 0, max: uint.MaxValue),
        ContentEditorValidation.Field("CreditPrice", "元宝价格", "integer", required: true, defaultValue: 0, min: 0, max: uint.MaxValue),
        ContentEditorValidation.Field("Count", "数量", "integer", required: true, defaultValue: 1, min: 1, max: ushort.MaxValue),
        ContentEditorValidation.Field("Class", "职业限制", "string", defaultValue: ""),
        ContentEditorValidation.Field("Category", "分类", "string", defaultValue: ""),
        ContentEditorValidation.Field("Stock", "库存", "integer", required: true, defaultValue: 0, min: 0),
        ContentEditorValidation.Field("iStock", "独立库存", "boolean", required: true, defaultValue: false),
        ContentEditorValidation.Field("Deal", "促销", "boolean", required: true, defaultValue: false),
        ContentEditorValidation.Field("TopItem", "置顶", "boolean", required: true, defaultValue: false),
        ContentEditorValidation.Field("Date", "上架时间", "string", defaultValue: ""),
        ContentEditorValidation.Field("CanBuyGold", "金币购买", "boolean", required: true, defaultValue: false),
        ContentEditorValidation.Field("CanBuyCredit", "元宝购买", "boolean", required: true, defaultValue: false)
    ];

    public static ContentEditorSchemaDto Schema { get; } = new(
        EntityType,
        SchemaVersion,
        "游戏商城",
        "编辑商品、价格、数量、分类、库存、上下架和购买方式；物品引用索引由服务端同步。",
        [
            new ContentEditorSectionDto("identity", "商品身份", "商城索引和物品引用。", Fields.Take(3).ToArray()),
            new ContentEditorSectionDto("pricing", "价格与数量", "金币、元宝价格和商品数量。", Fields.Skip(3).Take(3).ToArray()),
            new ContentEditorSectionDto("catalog", "分类与库存", "职业、分类、库存和上下架。", Fields.Skip(6).Take(8).ToArray())
        ]);

    public static Dictionary<string, string[]> ValidateStructure(JsonElement data, long localIndex, string? contentKey)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (data.ValueKind != JsonValueKind.Object)
        {
            errors["data"] = ["data must be a JSON object."];
            return errors;
        }

        ContentEditorValidation.ValidateFields(data, Fields, errors);
        if (data.TryGetProperty("GIndex", out var gindex) &&
            (!gindex.TryGetInt64(out var value) || value != localIndex))
        {
            errors["GIndex"] = [$"GIndex must match localIndex {localIndex}."];
        }

        var itemIndex = ContentEditorValidation.ReadLong(data, "ItemIndex");
        if (data.TryGetProperty("Info", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            var referenceIndex = ContentEditorValidation.ReadLong(info, "index");
            if (itemIndex is not null && referenceIndex is not null && itemIndex != referenceIndex)
            {
                errors["Info"] = ["Info.index must match ItemIndex."];
            }
        }

        if (!string.IsNullOrWhiteSpace(contentKey) &&
            !string.Equals(contentKey, $"{EntityType}.{localIndex}", StringComparison.Ordinal))
        {
            errors["contentKey"] = [$"contentKey must be '{EntityType}.{localIndex}'."];
        }

        return errors;
    }

    public static IEnumerable<string> ValidateCompleteness(JsonElement data)
    {
        foreach (var issue in ContentEditorValidation.ValidateRequiredFields(data, Fields))
        {
            yield return issue;
        }

        if (ContentEditorValidation.ReadLong(data, "ItemIndex") is not > 0)
        {
            yield return "Game shop item must reference a positive item index.";
        }

        var gold = ContentEditorValidation.ReadLong(data, "GoldPrice") ?? 0;
        var credit = ContentEditorValidation.ReadLong(data, "CreditPrice") ?? 0;
        if (gold == 0 && credit == 0)
        {
            yield return "Game shop item must have a gold or credit price.";
        }

    }

    public static ContentEditorIdentity ResolveIdentity(JsonElement data, long nextLocalIndex)
    {
        return new ContentEditorIdentity(nextLocalIndex, $"{EntityType}.{nextLocalIndex}");
    }

    public static JsonElement Normalize(JsonElement data)
    {
        return ContentEditorJson.Normalize(data, node =>
        {
            var itemIndex = ContentEditorJson.Long(node, "ItemIndex")
                            ?? (ContentEditorJson.Object(node, "Info") is { } info
                                ? ContentEditorJson.Long(info, "index")
                                : null)
                            ?? 0;
            node["ItemIndex"] = itemIndex;
            node["Info"] = ContentEditorJson.Reference("item", itemIndex, $"item.{itemIndex}");
        });
    }

    public static IEnumerable<ContentEditorReferenceValue> GetReferences(JsonElement data)
    {
        if (ContentEditorValidation.ReadLong(data, "ItemIndex") is > 0 and var itemIndex)
        {
            yield return new ContentEditorReferenceValue("ItemIndex", "item", itemIndex);
        }
    }
}
