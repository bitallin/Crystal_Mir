using System.Text.Json;
using Crystal.Content;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Crystal.LegacyImporter;

public sealed record RecipeContentImportResult(
    IReadOnlyList<ContentEntity> Entities,
    int FileCount);

public static class RecipeContentImporter
{
    public static RecipeContentImportResult Map(Envir envir, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(envir);

        var root = Path.GetFullPath(contentRoot);
        var recipeRoot = Path.Combine(root, "Envir", "Recipe");
        if (!Directory.Exists(recipeRoot))
        {
            return new RecipeContentImportResult([], 0);
        }

        var files = Directory.GetFiles(recipeRoot, "*.txt")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        var entities = new List<ContentEntity>(files.Length);
        var originalDirectory = Environment.CurrentDirectory;
        var previousItems = Envir.Main.ItemInfoList;
        var previousRecipeId = Envir.Main.NextRecipeID;

        try
        {
            Environment.CurrentDirectory = root;
            Envir.Main.ItemInfoList = envir.ItemInfoList;
            Envir.Main.NextRecipeID = 0;

            for (var index = 0; index < files.Length; index++)
            {
                var filePath = files[index];
                var name = Path.GetFileNameWithoutExtension(filePath);
                var recipe = new RecipeInfo(name);
                var data = CreateData(recipe, name, Path.GetRelativePath(root, filePath));

                entities.Add(new ContentEntity
                {
                    EntityType = ContentEntityTypes.Recipe,
                    LocalIndex = index + 1L,
                    ContentKey = $"recipe.{name}",
                    Data = data
                });
            }
        }
        finally
        {
            Envir.Main.NextRecipeID = previousRecipeId;
            Envir.Main.ItemInfoList = previousItems;
            Environment.CurrentDirectory = originalDirectory;
        }

        return new RecipeContentImportResult(entities, files.Length);
    }

    private static JsonElement CreateData(RecipeInfo recipe, string name, string relativePath)
    {
        var data = new RecipeContentData(
            name,
            relativePath.Replace('\\', '/'),
            ToItemData(recipe.Item),
            recipe.Ingredients?.Select(ToItemData).Where(item => item is not null).Cast<RecipeItemData>().ToArray() ?? [],
            recipe.Tools?.Select(ToItemData).Where(item => item is not null).Cast<RecipeItemData>().ToArray() ?? [],
            recipe.RequiredFlag.ToArray(),
            recipe.RequiredLevel,
            recipe.RequiredQuest.ToArray(),
            recipe.RequiredClass.Select(value => value.ToString()).ToArray(),
            recipe.RequiredGender?.ToString(),
            recipe.Chance,
            recipe.Gold);

        return ContentJson.Canonicalize(JsonSerializer.SerializeToElement(data, ContentJson.CompactOptions));
    }

    private static RecipeItemData? ToItemData(UserItem? item)
    {
        if (item is null) return null;

        return new RecipeItemData(
            item.ItemIndex,
            item.Info?.Name,
            item.Count,
            item.CurrentDura,
            item.MaxDura);
    }

    private sealed record RecipeContentData(
        string Name,
        string FileName,
        RecipeItemData? Output,
        IReadOnlyList<RecipeItemData> Ingredients,
        IReadOnlyList<RecipeItemData> Tools,
        IReadOnlyList<int> RequiredFlags,
        ushort? RequiredLevel,
        IReadOnlyList<int> RequiredQuests,
        IReadOnlyList<string> RequiredClasses,
        string? RequiredGender,
        byte Chance,
        uint Gold);

    private sealed record RecipeItemData(
        int ItemIndex,
        string? ItemName,
        ushort Count,
        ushort CurrentDura,
        ushort MaxDura);
}
