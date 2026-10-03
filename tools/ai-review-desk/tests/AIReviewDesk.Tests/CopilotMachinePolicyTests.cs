using AIReviewDesk.Infrastructure;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace AIReviewDesk.Tests;

[SupportedOSPlatform("windows")]
public sealed class CopilotMachinePolicyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ard-machine-policy-" + Guid.NewGuid().ToString("N"));

    public CopilotMachinePolicyTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void MissingFileAndRegistrySourcesReturnDeterministicAbsenceEvidence()
    {
        var sources = new[] { new CopilotMachinePolicy.FileSource(Path.Combine(root, "missing.json"), "test managed settings") };
        var registry = new[] { new CopilotMachinePolicy.RegistrySource(@"SOFTWARE\Policies\GitHub\Copilot", RegistryView.Registry64, "test registry policy") };

        var first = CopilotMachinePolicy.Inspect(sources, registry, (_, _) => false);
        var second = CopilotMachinePolicy.Inspect(sources, registry, (_, _) => false);

        Assert.Equal(CopilotMachinePolicy.AbsentEvidence, first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void EmptyPolicyDirectoryIsAllowed()
    {
        var directory = Path.Combine(root, "policy.d");
        Directory.CreateDirectory(directory);
        var sources = new[] { new CopilotMachinePolicy.FileSource(directory, "test policy directory", EmptyDirectoryIsAllowed: true) };

        Assert.Equal(CopilotMachinePolicy.AbsentEvidence, CopilotMachinePolicy.Inspect(sources, [], (_, _) => false));
    }

    [Fact]
    public void ExistingManagedSettingsFileBlocksWithoutReadingContents()
    {
        var file = Path.Combine(root, "managed-settings.json");
        File.WriteAllText(file, "synthetic policy data");
        using var exclusive = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var sources = new[] { new CopilotMachinePolicy.FileSource(file, "test managed settings") };

        var error = Assert.Throws<InvalidOperationException>(() => CopilotMachinePolicy.Inspect(sources, [], (_, _) => false));
        Assert.Contains("Copilot machine policy", error.Message);
        Assert.Contains("blocked", error.Message);
    }

    [Fact]
    public void NonemptyPolicyDirectoryBlocks()
    {
        var directory = Path.Combine(root, "policy.d");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "policy.json"), "synthetic policy data");
        var sources = new[] { new CopilotMachinePolicy.FileSource(directory, "test policy directory", EmptyDirectoryIsAllowed: true) };

        Assert.Throws<InvalidOperationException>(() => CopilotMachinePolicy.Inspect(sources, [], (_, _) => false));
    }

    [Fact]
    public void UnexpectedDirectoryAtFileSourceBlocksAsAmbiguous()
    {
        var path = Path.Combine(root, "managed-settings.json");
        Directory.CreateDirectory(path);
        var sources = new[] { new CopilotMachinePolicy.FileSource(path, "test managed settings") };

        Assert.Throws<InvalidOperationException>(() => CopilotMachinePolicy.Inspect(sources, [], (_, _) => false));
    }

    [Fact]
    public void ExistingRegistryKeyBlocksWithoutReadingValues()
    {
        var registry = new[] { new CopilotMachinePolicy.RegistrySource(@"SOFTWARE\Policies\GitHubCopilot", RegistryView.Registry32, "test registry policy") };

        var error = Assert.Throws<InvalidOperationException>(() => CopilotMachinePolicy.Inspect([], registry, (_, _) => true));
        Assert.Contains("Copilot machine policy", error.Message);
    }

    [Fact]
    public void UnreadableRegistryMetadataBlocksClosed()
    {
        var registry = new[] { new CopilotMachinePolicy.RegistrySource(@"SOFTWARE\Policies\GitHub\Copilot", RegistryView.Registry64, "test registry policy") };

        var error = Assert.Throws<InvalidOperationException>(() => CopilotMachinePolicy.Inspect([], registry, (_, _) => throw new UnauthorizedAccessException()));

        Assert.Contains("Copilot machine policy", error.Message);
        Assert.Contains("could not be safely inspected", error.Message);
    }

    [Fact]
    public void UnreadableFileMetadataBlocksClosed()
    {
        var sources = new[] { new CopilotMachinePolicy.FileSource("<invalid>", "test managed settings") };

        var error = Assert.Throws<InvalidOperationException>(() => CopilotMachinePolicy.Inspect(sources, [], (_, _) => false));

        Assert.Contains("Copilot machine policy", error.Message);
    }
}
