using System.Security;
using Microsoft.Win32;

namespace AIReviewDesk.Infrastructure;

/// <summary>
/// Checks machine-wide Copilot policy locations whose behavior is outside the
/// verified dedicated-profile contract. Policy contents are never opened.
/// </summary>
public static class CopilotMachinePolicy
{
    public const string AbsentEvidence = "copilot-machine-policy:absent";

    public sealed record FileSource(string? Path, string Label, bool EmptyDirectoryIsAllowed = false);
    public sealed record RegistrySource(string KeyPath, RegistryView View, string Label);

    private static RegistrySource[] GetRegistrySources()
    {
        if (!OperatingSystem.IsWindows()) throw Block("Windows machine policy sources are unavailable on this platform");
        return
        [
            new(@"SOFTWARE\Policies\GitHub\Copilot", RegistryView.Registry64, "64-bit GitHub Copilot policy"),
            new(@"SOFTWARE\Policies\GitHub\Copilot", RegistryView.Registry32, "32-bit GitHub Copilot policy"),
            new(@"SOFTWARE\Policies\GitHubCopilot", RegistryView.Registry64, "64-bit GitHubCopilot policy"),
            new(@"SOFTWARE\Policies\GitHubCopilot", RegistryView.Registry32, "32-bit GitHubCopilot policy")
        ];
    }

    public static string Inspect(
        IEnumerable<FileSource>? fileSources = null,
        IEnumerable<RegistrySource>? registrySources = null,
        Func<string, RegistryView, bool>? registryKeyExists = null)
    {
        fileSources ??= GetDefaultFileSources();
        registrySources ??= OperatingSystem.IsWindows()
            ? GetRegistrySources()
            : throw Block("Windows machine policy sources are unavailable on this platform");
        registryKeyExists ??= RegistryKeyExists;

        foreach (var source in fileSources)
            InspectFileSource(source);

        foreach (var source in registrySources)
        {
            bool exists;
            try
            {
                exists = registryKeyExists(source.KeyPath, source.View);
            }
            catch (Exception ex)
            {
                throw Block(source.Label, ex);
            }
            if (exists) throw Block(source.Label);
        }

        return AbsentEvidence;
    }

    private static IEnumerable<FileSource> GetDefaultFileSources()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        yield return new FileSource(
            string.IsNullOrWhiteSpace(programData) ? null : Path.Combine(programData, "GitHub", "Copilot", "policy.d"),
            "GitHub Copilot policy directory",
            EmptyDirectoryIsAllowed: true);
        yield return new FileSource(
            string.IsNullOrWhiteSpace(programFiles) ? null : Path.Combine(programFiles, "GitHubCopilot", "managed-settings.json"),
            "64-bit managed settings file");
        yield return new FileSource(
            string.IsNullOrWhiteSpace(programFilesX86) ? null : Path.Combine(programFilesX86, "GitHubCopilot", "managed-settings.json"),
            "32-bit managed settings file");
    }

    private static void InspectFileSource(FileSource source)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source.Path))
                throw Block(source.Label);

            var fullPath = Path.GetFullPath(source.Path);
            var chain = GetPathChain(fullPath);
            for (var index = 0; index < chain.Count; index++)
            {
                var attributes = GetAttributesOrMissing(chain[index]);
                if (attributes is null) continue;

                if ((attributes.Value & FileAttributes.ReparsePoint) != 0)
                    throw Block(source.Label);

                var isTarget = index == chain.Count - 1;
                var isDirectory = (attributes.Value & FileAttributes.Directory) != 0;
                if (!isTarget && !isDirectory)
                    throw Block(source.Label);
                if (!isTarget) continue;

                if (!source.EmptyDirectoryIsAllowed || !isDirectory)
                    throw Block(source.Label);

                // A completely empty policy.d directory contributes no policy.
                // Enumeration errors are treated as an unsafe/ambiguous source.
                using var entries = Directory.EnumerateFileSystemEntries(fullPath).GetEnumerator();
                if (entries.MoveNext())
                    throw Block(source.Label);
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Copilot machine policy", StringComparison.Ordinal)) { throw; }
        catch (Exception ex) when (IsProbeFailure(ex))
        {
            throw Block(source.Label, ex);
        }
    }

    private static List<string> GetPathChain(string fullPath)
    {
        var chain = new List<string>();
        var current = fullPath;
        while (current.Length > 0)
        {
            chain.Add(current);
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
        chain.Reverse();
        return chain;
    }

    private static FileAttributes? GetAttributesOrMissing(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static bool RegistryKeyExists(string keyPath, RegistryView view)
    {
        if (!OperatingSystem.IsWindows()) throw Block("Windows machine policy sources are unavailable on this platform");
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var key = baseKey.OpenSubKey(keyPath, writable: false);
        return key is not null;
    }

    private static bool IsProbeFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or
        ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception;

    private static InvalidOperationException Block(string source, Exception? inner = null) =>
        new($"Copilot machine policy is present or could not be safely inspected ({source}). Review launch was blocked.", inner);
}
