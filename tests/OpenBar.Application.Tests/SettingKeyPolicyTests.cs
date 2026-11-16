using OpenBar.Application.Persistence;
using Xunit;

namespace OpenBar.Application.Tests;

public sealed class SettingKeyPolicyTests
{
    [Theory]
    [InlineData("integration.password")]
    [InlineData("user.Password")]
    [InlineData("smtp.passwd")]
    [InlineData("service.secret")]
    [InlineData("oauth.token")]
    [InlineData("github.apikey")]
    [InlineData("github.api_key")]
    [InlineData("github.apiKey")]
    [InlineData("signing.privateKey")]
    [InlineData("signing.private_key")]
    [InlineData("windows.credential")]
    public void FlagsObviousSecretKeys(string key)
    {
        Assert.True(SettingKeyPolicy.IsSensitive(key));
    }

    [Theory]
    [InlineData("ui.theme")]
    [InlineData("ui.language")]
    [InlineData("dock.autoHide")]
    [InlineData("application.startup.count")]
    [InlineData("application.startup.last_started_utc")]
    [InlineData("widget.clock.timezone")]
    [InlineData("layout.default.name")]
    public void AllowsRegularConfigurationKeys(string key)
    {
        Assert.False(SettingKeyPolicy.IsSensitive(key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyKeysAreNotFlagged(string key)
    {
        // Empty keys are rejected separately as invalid input.
        Assert.False(SettingKeyPolicy.IsSensitive(key));
    }
}
