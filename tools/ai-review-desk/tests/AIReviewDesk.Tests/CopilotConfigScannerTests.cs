using System.Text;
using System.Text.Json;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.Tests;

public sealed class CopilotConfigScannerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ard-config-scanner-" + Guid.NewGuid().ToString("N"));
    private readonly string sentinel = "ARD_SYNTHETIC_OAUTH_SENTINEL_" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    public CopilotConfigScannerTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);
    private string Auth => "\"loggedInUsers\":[{\"host\":\"https://example.invalid\",\"login\":\"synthetic-user\"}]," +
        "\"lastLoggedInUser\":{\"host\":\"https://example.invalid\",\"login\":\"synthetic-user\"}," +
        "\"authTokens\":{\"https://example.invalid:synthetic-user\":{\"token\":\"" + sentinel + "\"}}";

    private void NoLeak(string output) => Assert.True(!output.Contains(sentinel, StringComparison.Ordinal),
        "A synthetic sensitive value escaped the structural inspection boundary.");

    [Fact]
    public void AccountMetadataAndCredentialValuesReturnOnlyStructuralBooleans()
    {
        var result = CopilotConfigScanner.Inspect(Encoding.UTF8.GetBytes("{" + Auth + "}"));
        Assert.True(result.AccountMetadataPresent);
        Assert.True(result.CredentialFieldPresent);
        NoLeak(result.ToString());
        NoLeak(JsonSerializer.Serialize(result));
        Assert.All(typeof(CopilotConfigInspection).GetProperties(), p => Assert.Equal(typeof(bool), p.PropertyType));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"lastLoggedInUser\":null,\"loggedInUsers\":[],\"authTokens\":{}}")]
    [InlineData("{\"installedPlugins\":{},\"trustedFolders\":[]}")]
    [InlineData("{\"loggedInUsers\":[{\"host\":\"https://example.invalid\",\"login\":\"user\",\"kind\":\"github\"}],\"firstLaunchAt\":\"2026-10-03T00:00:00Z\",\"appTipShown\":true,\"reasoningSummariesCleanupDone\":true}")]
    [InlineData("// CLI-managed state\n{\"loggedInUsers\":[{\"host\":\"https://example.invalid\",\"login\":\"user\"}]}")]
    public void UnderstoodInertStatePasses(string json) => CopilotConfigScanner.Inspect(Encoding.UTF8.GetBytes(json));

    [Theory]
    [InlineData("hooks", CopilotConfigBlock.Hooks)]
    [InlineData("mcpServers", CopilotConfigBlock.Mcp)]
    [InlineData("enabledPlugins", CopilotConfigBlock.Plugins)]
    [InlineData("extensions", CopilotConfigBlock.Extensions)]
    [InlineData("agents", CopilotConfigBlock.Agents)]
    [InlineData("skills", CopilotConfigBlock.Skills)]
    [InlineData("lspServers", CopilotConfigBlock.LanguageServer)]
    [InlineData("providers", CopilotConfigBlock.Provider)]
    [InlineData("command", CopilotConfigBlock.Authority)]
    [InlineData("unknownStartup", CopilotConfigBlock.UnknownStructure)]
    public async Task UnsafeConfigNeverExposesCredentialInExceptionsPreflightOrPersistedMetadata(string property, CopilotConfigBlock category)
    {
        var profile = Path.Combine(root, "profile");
        var run = Path.Combine(root, "run");
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(profile, "config.json"), "{" + Auth + ",\"" + property + "\":{\"command\":\"" + sentinel + "\"}}");
        var error = Assert.Throws<CopilotConfigBlockedException>(() => CopilotPreflight.Inspect(profile, run));
        Assert.Equal(category, error.Category);
        Assert.Null(error.InnerException);
        NoLeak(error.Message);
        NoLeak(error.ToString());
        var record = new ReviewRecord { ProjectId = Guid.NewGuid(), Status = ReviewStatus.Failed, Diagnostic = error.Message };
        NoLeak(JsonSerializer.Serialize(record));
        var handoffError = Assert.Throws<InvalidOperationException>(() => HandoffFormatter.FormatResult(record));
        NoLeak(handoffError.ToString());
        var history = new ReviewHistoryStore(Path.Combine(root, "history"));
        await history.SaveAsync(record);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "history"), "*.json", SearchOption.AllDirectories))
            NoLeak(File.ReadAllText(file));
    }

    [Theory]
    [InlineData("{\"authTokens\":{\"account\":{\"token\":123}}}", CopilotConfigBlock.UnexpectedSchema)]
    [InlineData("{\"loggedInUsers\":{}}", CopilotConfigBlock.UnexpectedSchema)]
    [InlineData("{\"loggedInUsers\":[{\"host\":\"x\"}]}", CopilotConfigBlock.UnexpectedSchema)]
    [InlineData("{\"loggedInUsers\":[],\"loggedInUsers\":[]}", CopilotConfigBlock.UnexpectedSchema)]
    [InlineData("{\"loggedInUsers\":[{\"host\":\"x\",\"login\":\"u\",\"hooks\":{}}]}", CopilotConfigBlock.Hooks)]
    [InlineData("{\"authTokens\":{\"account\":{\"token\":\"x\",\"unknown\":{}}}}", CopilotConfigBlock.UnknownStructure)]
    [InlineData("{\"trustedFolders\":[\"somewhere\"]}", CopilotConfigBlock.Authority)]
    [InlineData("{\"installedPlugins\":{\"plugin\":{}}}", CopilotConfigBlock.Plugins)]
    [InlineData("{\"reasoningSummariesCleanupDone\":false}", CopilotConfigBlock.Authority)]
    [InlineData("{\"firstLaunchAt\":{\"command\":\"unsafe\"}}", CopilotConfigBlock.UnexpectedSchema)]
    [InlineData("[]", CopilotConfigBlock.UnexpectedSchema)]
    [InlineData("{\"h\\u006foks\":{}}", CopilotConfigBlock.Hooks)]
    [InlineData("{\"authTokens\":{},\"auth\\u0054okens\":{}}", CopilotConfigBlock.UnexpectedSchema)]
    public void NestedUnknownExecutableAndAmbiguousShapesFailClosed(string json, CopilotConfigBlock category)
    {
        var error = Assert.Throws<CopilotConfigBlockedException>(() => CopilotConfigScanner.Inspect(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(category, error.Category);
    }

    [Fact]
    public void MalformedSecretTokenAndTrailingInputProduceOnlyFixedDiagnostics()
    {
        foreach (var json in new[] { "{" + Auth + ",", "{" + Auth + "} " + sentinel,
            "{\"authTokens\":{\"account\":{\"token\":\"" + sentinel + "\\q\"}}}" })
        {
            var error = Assert.Throws<CopilotConfigBlockedException>(() => CopilotConfigScanner.Inspect(Encoding.UTF8.GetBytes(json)));
            NoLeak(error.ToString());
            Assert.Null(error.InnerException);
        }
    }

    [Fact]
    public void CredentialContentNeverContributesToPreflightFingerprintAndFileIsUnchanged()
    {
        var profile = Path.Combine(root, "profile");
        var run = Path.Combine(root, "run");
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(run);
        var path = Path.Combine(profile, "config.json");
        var json = "{" + Auth + "}";
        File.WriteAllText(path, json);
        var first = CopilotPreflight.Inspect(profile, run);
        Assert.True(File.ReadAllText(path) == json, "Structural inspection modified its synthetic input.");
        NoLeak(first);
        File.WriteAllText(path, json.Replace(sentinel, "ANOTHER_SYNTHETIC_NONSECRET_TOKEN", StringComparison.Ordinal));
        Assert.Equal(first, CopilotPreflight.Inspect(profile, run));
    }

    [Fact]
    public void EscapedLongCredentialAndDynamicKeyAreSkippedWithoutAnOutputPath()
    {
        var json = "{\"authTokens\":{\"" + sentinel + "\":{\"to\\u006ben\":\"" +
            new string('x', 128 * 1024) + sentinel + "\\n\\\"\\u20ac\"}}}";
        NoLeak(CopilotConfigScanner.Inspect(Encoding.UTF8.GetBytes(json)).ToString());
    }

    [Fact]
    public void CliPersistedExperimentalDisableIsAcceptedButAnOptInIsBlocked()
    {
        var profile = Path.Combine(root, "profile");
        var run = Path.Combine(root, "run");
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(run);
        var path = Path.Combine(profile, "settings.json");
        File.WriteAllText(path, "{\"disableAllHooks\":true,\"experimental\":false}");
        Assert.NotEmpty(CopilotPreflight.Inspect(profile, run));
        File.WriteAllText(path, "{\"disableAllHooks\":true,\"experimental\":true}");
        var error = Assert.Throws<InvalidOperationException>(() => CopilotPreflight.Inspect(profile, run));
        NoLeak(error.ToString());
        File.WriteAllText(path, "{\"experimental\":false}");
        Assert.Throws<InvalidOperationException>(() => CopilotPreflight.Inspect(profile, run));
    }

    [Fact]
    public void UnknownCredentialBearingSettingsAndPropertyNamesNeverEscape()
    {
        var profile = Path.Combine(root, "profile");
        var run = Path.Combine(root, "run");
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(run);
        File.WriteAllText(Path.Combine(profile, "settings.json"), "{\"disableAllHooks\":true," + Auth + "}");
        var error = Assert.Throws<InvalidOperationException>(() => CopilotPreflight.Inspect(profile, run));
        NoLeak(error.ToString());
        error = Assert.Throws<CopilotConfigBlockedException>(() => CopilotConfigScanner.Inspect(
            Encoding.UTF8.GetBytes("{\"" + sentinel + "\":\"" + sentinel + "\"}")));
        NoLeak(error.ToString());
    }

    [Fact]
    public void SizeAndUnreadableInputFailWithFixedMessages()
    {
        var error = Assert.Throws<CopilotConfigBlockedException>(() => CopilotConfigScanner.Inspect(new byte[CopilotConfigScanner.MaximumFileBytes + 1]));
        Assert.Equal(CopilotConfigBlock.UnexpectedSchema, error.Category);
        error = Assert.Throws<CopilotConfigBlockedException>(() => CopilotConfigScanner.InspectFile(Path.Combine(root, "missing")));
        Assert.Equal(CopilotConfigBlock.Unreadable, error.Category);
        NoLeak(error.ToString());
    }
}
