using System.Buffers.Binary;

namespace Crystal.Content;

public sealed record MapAssetMetadata(
    bool Valid,
    int Format,
    string FormatName,
    int Width,
    int Height,
    long CellCount,
    long WalkableCells,
    long BlockedCells,
    long DoorCells,
    string? Error)
{
    public static MapAssetMetadata Invalid(int format, string error)
    {
        return new MapAssetMetadata(
            false,
            format,
            FormatNameFor(format),
            0,
            0,
            0,
            0,
            0,
            0,
            error);
    }

    public static string FormatNameFor(int format)
    {
        return format switch
        {
            0 => "mir2-v0",
            1 => "mir2-v1",
            2 => "mir2-v2",
            3 => "shanda-v3",
            4 => "anti-hack-v4",
            5 => "wemade-v5",
            6 => "shanda-v6",
            7 => "heroes-v7",
            100 => "crystal-v100",
            _ => "unknown"
        };
    }
}
