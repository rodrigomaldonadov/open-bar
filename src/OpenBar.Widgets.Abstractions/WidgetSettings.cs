using System.Text.Json;

namespace OpenBar.Widgets.Abstractions;

/// <summary>
/// Immutable, validated view over a widget's JSON configuration. The runtime
/// owns parsing so widgets do not implement persistence logic; widgets only
/// deserialize the strongly typed shape they need.
/// </summary>
public sealed class WidgetSettings
{
    public static WidgetSettings Empty { get; } = CreateEmpty();

    private static WidgetSettings CreateEmpty()
    {
        using var document = JsonDocument.Parse("{}");
        return new WidgetSettings("{}", 1, document.RootElement.Clone());
    }

    private WidgetSettings(string json, int schemaVersion, JsonElement root)
    {
        Json = json;
        SchemaVersion = schemaVersion;
        Root = root;
    }

    /// <summary>Original JSON text, preserved exactly as supplied.</summary>
    public string Json { get; }

    public int SchemaVersion { get; }

    public JsonElement Root { get; }

    /// <summary>
    /// Parses and validates JSON. Throws <see cref="JsonException"/> when the
    /// text is not valid JSON and <see cref="ArgumentOutOfRangeException"/>
    /// when the schema version is invalid.
    /// </summary>
    public static WidgetSettings Parse(string json, int schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                schemaVersion,
                "The widget settings schema version must be at least 1.");
        }

        using var document = JsonDocument.Parse(json);
        return new WidgetSettings(json, schemaVersion, document.RootElement.Clone());
    }

    /// <summary>Deserializes the configuration into the widget's own settings type.</summary>
    public T? Deserialize<T>(JsonSerializerOptions? options = null)
        => Root.Deserialize<T>(options);
}
