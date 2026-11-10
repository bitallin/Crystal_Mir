namespace Crystal.Content;

public static class ContentEntityTypes
{
    public const string Item = "item";
    public const string Monster = "monster";
    public const string Map = "map";
    public const string Npc = "npc";
    public const string Quest = "quest";
    public const string Magic = "magic";
    public const string GameShop = "gameshop";
    public const string Recipe = "recipe";
    public const string Buff = "buff";
    public const string Conquest = "conquest";
    public const string Dragon = "dragon";
    public const string Respawn = "respawn";
    public const string Script = "script";
    public const string Asset = "asset";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Item,
        Monster,
        Map,
        Npc,
        Quest,
        Magic,
        GameShop,
        Recipe,
        Buff,
        Conquest,
        Dragon,
        Respawn,
        Script,
        Asset
    };
}
