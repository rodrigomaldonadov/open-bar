namespace OpenBar.Application.Persistence;

/// <summary>
/// Central policy that keeps credentials out of the settings table. Secret
/// material belongs to the future Windows Credential Manager/DPAPI store, not
/// to SQLite.
/// </summary>
public static class SettingKeyPolicy
{
    private static readonly string[] SensitiveFragments =
    [
        "password",
        "passwd",
        "secret",
        "token",
        "apikey",
        "api_key",
        "privatekey",
        "private_key",
        "credential"
    ];

    /// <summary>
    /// True when the key clearly denotes secret material. This is a defensive
    /// guard, not a substitute for the dedicated secret store.
    /// </summary>
    public static bool IsSensitive(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        foreach (var fragment in SensitiveFragments)
        {
            if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
