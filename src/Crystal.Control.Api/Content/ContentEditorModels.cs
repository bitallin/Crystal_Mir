using System.Text.Json;

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
    IReadOnlyList<ContentEditorFieldDto>? Fields = null);

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
