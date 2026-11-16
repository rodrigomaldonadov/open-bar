using System.Text.Json;
using System.Text.RegularExpressions;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime;

/// <summary>
/// Validates widget definitions at discovery time. Invalid definitions are
/// rejected individually so one broken module never hides the rest of the
/// catalog.
/// </summary>
public static partial class WidgetDefinitionValidator
{
    private static readonly Regex WidgetIdPattern = CreateWidgetIdPattern();

    public static void Validate(WidgetDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        ValidateWidgetId(definition.WidgetId);
        ValidateMetadata(definition.Metadata);

        if (definition.CreateWidget is null)
        {
            throw new WidgetDefinitionValidationException(nameof(definition.CreateWidget), "A widget factory is required.");
        }

        if (definition.SettingsSchemaVersion < 1)
        {
            throw new WidgetDefinitionValidationException(
                nameof(definition.SettingsSchemaVersion),
                "The settings schema version must be at least 1.");
        }

        ValidateJson(definition.DefaultSettingsJson);
    }

    private static void ValidateWidgetId(string widgetId)
    {
        if (string.IsNullOrWhiteSpace(widgetId))
        {
            throw new WidgetDefinitionValidationException(nameof(WidgetDefinition.WidgetId), "A widget id is required.");
        }

        if (widgetId.Length > WidgetDefinition.MaxWidgetIdLength)
        {
            throw new WidgetDefinitionValidationException(
                nameof(WidgetDefinition.WidgetId),
                $"The widget id must be at most {WidgetDefinition.MaxWidgetIdLength} characters.");
        }

        if (!WidgetIdPattern.IsMatch(widgetId))
        {
            throw new WidgetDefinitionValidationException(
                nameof(WidgetDefinition.WidgetId),
                "The widget id must use lower-case letters, digits, dots, underscores or hyphens.");
        }
    }

    private static void ValidateMetadata(WidgetMetadata? metadata)
    {
        if (metadata is null)
        {
            throw new WidgetDefinitionValidationException(nameof(WidgetDefinition.Metadata), "Widget metadata is required.");
        }

        RequireText(metadata.DisplayName, nameof(metadata.DisplayName), WidgetMetadata.MaxDisplayNameLength);
        RequireText(metadata.Category, nameof(metadata.Category), WidgetMetadata.MaxCategoryLength);
        RequireText(metadata.Version, nameof(metadata.Version), WidgetMetadata.MaxVersionLength);

        if (metadata.Description.Length > WidgetMetadata.MaxDescriptionLength)
        {
            throw new WidgetDefinitionValidationException(
                nameof(metadata.Description),
                $"The description must be at most {WidgetMetadata.MaxDescriptionLength} characters.");
        }

        if (metadata.Author is not null && metadata.Author.Length > WidgetMetadata.MaxAuthorLength)
        {
            throw new WidgetDefinitionValidationException(
                nameof(metadata.Author),
                $"The author must be at most {WidgetMetadata.MaxAuthorLength} characters.");
        }

        ValidateSize(metadata.MinWidth, nameof(metadata.MinWidth));
        ValidateSize(metadata.MinHeight, nameof(metadata.MinHeight));
        ValidateSize(metadata.MaxWidth, nameof(metadata.MaxWidth));
        ValidateSize(metadata.MaxHeight, nameof(metadata.MaxHeight));
        ValidateSize(metadata.DefaultWidth, nameof(metadata.DefaultWidth));
        ValidateSize(metadata.DefaultHeight, nameof(metadata.DefaultHeight));

        if (metadata.MaxWidth < metadata.MinWidth || metadata.MaxHeight < metadata.MinHeight)
        {
            throw new WidgetDefinitionValidationException(
                nameof(metadata.MaxWidth),
                "Maximum widget dimensions must be greater than or equal to the minimum dimensions.");
        }

        if (metadata.DefaultWidth < metadata.MinWidth
            || metadata.DefaultWidth > metadata.MaxWidth
            || metadata.DefaultHeight < metadata.MinHeight
            || metadata.DefaultHeight > metadata.MaxHeight)
        {
            throw new WidgetDefinitionValidationException(
                nameof(metadata.DefaultWidth),
                "Default widget dimensions must be within the minimum and maximum bounds.");
        }

        if ((metadata.Capabilities & ~WidgetCapabilities.All) != 0)
        {
            throw new WidgetDefinitionValidationException(
                nameof(metadata.Capabilities),
                "The widget declares an unknown capability flag.");
        }
    }

    private static void ValidateSize(double value, string propertyName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new WidgetDefinitionValidationException(propertyName, "Widget dimensions must be positive finite numbers.");
        }
    }

    private static void RequireText(string? value, string propertyName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new WidgetDefinitionValidationException(propertyName, "This metadata field is required.");
        }

        if (value.Length > maxLength)
        {
            throw new WidgetDefinitionValidationException(
                propertyName,
                $"This metadata field must be at most {maxLength} characters.");
        }
    }

    private static void ValidateJson(string? json)
    {
        if (json is null)
        {
            throw new WidgetDefinitionValidationException(
                nameof(WidgetDefinition.DefaultSettingsJson),
                "Default settings JSON is required.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new WidgetDefinitionValidationException(
                nameof(WidgetDefinition.DefaultSettingsJson),
                $"Default settings must be valid JSON: {ex.Message}");
        }
    }

    private static Regex CreateWidgetIdPattern()
        => new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
