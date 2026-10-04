using System.Security.Cryptography;
using System.Text;

namespace AIReviewDesk.Infrastructure;

public static class CopilotPreflight
{
    private static readonly HashSet<string> StateFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Opaque CLI-managed authentication/state: contents are never opened by this application.
        "config.json.lock", "session-store.db", "session-store.db-shm", "session-store.db-wal",
        "command-history-state.json", "open-sessions-state.json", "settings.json.lock", "installed-plugins.lock"
    };
    private static readonly HashSet<string> StateDirectories = new(StringComparer.OrdinalIgnoreCase)
    { "logs", "session-state", "command-history-state", "sidebar-sessions-state", "ide" };
    private static readonly HashSet<string> MustBeEmptyDirectories = new(StringComparer.OrdinalIgnoreCase)
    { "hooks", "extensions", "installed-plugins", "plugin-data", "servers", "agents", "skills", "instructions" };

    public static string Inspect(string profile, string runDirectory, string? repository = null)
    {
        var machinePolicy = CopilotMachinePolicy.Inspect();
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(runDirectory);
        AssertNoReparseAncestors(profile);
        AssertNoReparseAncestors(runDirectory);
        if (Directory.EnumerateFileSystemEntries(runDirectory).Any())
            throw new InvalidOperationException("The reviewer run directory is not empty. Review launch was blocked.");
        InspectAncestors(runDirectory);
        if (repository != null) InspectRepositoryContributions(repository);
        var evidence = new StringBuilder();
        evidence.AppendLine(machinePolicy);
        foreach (var entry in Directory.EnumerateFileSystemEntries(profile).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(entry);
            AssertNoReparse(entry);
            if (Directory.Exists(entry))
            {
                if (MustBeEmptyDirectories.Contains(name))
                {
                    AssertEmptyTree(entry);
                    evidence.AppendLine(name);
                }
                else if (!StateDirectories.Contains(name)) Block(name);
            }
            else if (name.Equals("settings.json", StringComparison.OrdinalIgnoreCase))
            {
                if (new FileInfo(entry).Length > 65536) Block("oversized settings.json");
                try { CopilotConfigScanner.InspectSettingsFile(entry); }
                catch (CopilotConfigBlockedException ex)
                { throw new InvalidOperationException($"Copilot settings.json was blocked: {ex.Category}. Credential values were not decoded or returned."); }
                evidence.AppendLine("settings:v1:disableAllHooks=true");
            }
            else if (name.Equals("config.json", StringComparison.OrdinalIgnoreCase))
            {
                var inspection = CopilotConfigScanner.InspectFile(entry);
                // Only structural booleans contribute. Never hash or retain raw configuration/authentication values.
                evidence.AppendLine($"config:v1:credential-field={inspection.CredentialFieldPresent}:account-metadata={inspection.AccountMetadataPresent}");
            }
            else if (!StateFiles.Contains(name)) Block(name);
        }
        // Structural evidence excludes credential values; post-run protocol and repository validation remain mandatory.
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence.ToString())));
    }

    private static void AssertEmptyTree(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            AssertNoReparse(entry);
            if (!Directory.Exists(entry)) Block(Path.GetFileName(directory));
            AssertEmptyTree(entry);
        }
    }

    private static void InspectAncestors(string runDirectory)
    {
        for (var parent = Directory.GetParent(runDirectory); parent != null; parent = parent.Parent)
        {
            foreach (var relative in new[] { ".github/hooks", ".github/copilot", ".claude", ".mcp.json", "mcp-config.json", "lsp-config.json", ".github/lsp.json", ".github/mcp.json", ".github/extensions", ".github/plugins" })
                if (File.Exists(Path.Combine(parent.FullName, relative)) || Directory.Exists(Path.Combine(parent.FullName, relative)))
                    Block($"ancestor configuration {relative}");
        }
    }

    private static void InspectRepositoryContributions(string repository)
    {
        AssertNoReparseAncestors(repository);
        foreach (var relative in new[] { ".github/lsp.json", ".github/extensions", ".github/plugins", ".claude/settings.json", ".claude/settings.local.json", ".github/copilot/settings.json", ".github/copilot/settings.local.json" })
            if (File.Exists(Path.Combine(repository, relative)) || Directory.Exists(Path.Combine(repository, relative)))
                Block($"repository executable configuration {relative}");
    }

    public static void AssertNoReparseAncestors(string path)
    {
        if (File.Exists(path)) AssertNoReparse(path);
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current != null; current = current.Parent)
            if (current.Exists) AssertNoReparse(current.FullName);
    }

    private static void AssertNoReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) Block("linked configuration path");
    }
    private static void Block(string source) => throw new InvalidOperationException($"Copilot configuration '{source}' is outside the verified dedicated-profile contract. This configuration must be resolved before reviewing. Nothing was launched.");
}
