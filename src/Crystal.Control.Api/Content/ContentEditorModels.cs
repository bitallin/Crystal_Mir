using System.Text.Json;
using System.Text.Json.Serialization;

namespace Crystal.Control.Api.Content;

public sealed record ContentEditorOptionDto(string Value, string Label);

public sealed record ContentEditorFieldDto(
    string Path,
    string Label,
    string Kind,
    bool Required = false,
    bool ReadOnly = false,
    object? DefaultValue = null,
    long? Min = null,
    long? Max = null,
    IReadOnlyList<ContentEditorOptionDto>? Options = null,
    IReadOnlyList<ContentEditorFieldDto>? Fields = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ReferenceType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? StoreAs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Nullable = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Multiline = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MinItems = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? MaxItems = null);

public sealed record ContentEditorSectionDto(
    string Id,
    string Label,
    string Description,
    IReadOnlyList<ContentEditorFieldDto> Fields);

public sealed record ContentEditorSchemaDto(
    string EntityType,
    int SchemaVersion,
    string Title,
    string Description,
    IReadOnlyList<ContentEditorSectionDto> Sections);

public sealed record CreateDraftEntityRequest(
    int? SchemaVersion,
    JsonElement? Data,
    string? Reason);

public sealed class DraftInputValidationException : Exception
{
    public DraftInputValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("Draft entity validation failed.")
    {
        Errors = errors;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed record ContentEditorIdentity(long LocalIndex, string ContentKey);

public sealed record ContentEditorReferenceValue(
    string Path,
    string EntityType,
    long Index,
    bool Required = true,
    string? Key = null);
