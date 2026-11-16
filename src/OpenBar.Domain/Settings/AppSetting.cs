namespace OpenBar.Domain.Settings;

/// <summary>
/// A global, non-secret application setting persisted as a key/value pair.
/// Secrets (passwords, API keys, OAuth tokens) must never be stored here; they
/// belong to the future Credential Manager/DPAPI secret store.
/// </summary>
public sealed class AppSetting
{
    public const int MaxKeyLength = 200;
    public const int MaxValueLength = 32_768;

    /// <summary>Stable, case-sensitive setting key (primary key).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Serialized setting value. Never a credential.</summary>
    public string Value { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
