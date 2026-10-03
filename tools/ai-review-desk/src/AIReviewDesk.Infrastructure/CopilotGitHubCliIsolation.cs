using System.Runtime.InteropServices;

namespace AIReviewDesk.Infrastructure;

/// <summary>Saved-profile hydration is allowed; executable GitHub CLI fallback is not.</summary>
public static class CopilotGitHubCliIsolation
{
    public static string ChildPath => Environment.SystemDirectory;

    public static void Inspect(CopilotInstallation installation, string cwd)
    {
        var directories = new List<string>
        {
            cwd, ChildPath, Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.GetDirectoryName(Path.GetFullPath(installation.Executable))!
        };
        var legacySystem = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System");
        try
        {
            if ((File.GetAttributes(legacySystem) & FileAttributes.Directory) != 0) directories.Add(legacySystem);
            else throw new InvalidOperationException("Windows executable search directory is ambiguous. Review launch was blocked.");
        }
        catch (FileNotFoundException) { } // An absent legacy search directory cannot supply an executable.
        catch (DirectoryNotFoundException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException("Windows executable search directory could not be verified. Review launch was blocked."); }
        if (installation.PrefixArguments.Count != 0)
        {
            // Detect resolves only the official npm loader. Its fully qualified native child
            // has a separate application directory in Windows executable search order.
            var architecture = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64", Architecture.Arm64 => "arm64",
                _ => throw new InvalidOperationException("Copilot native runtime architecture is outside the verified contract.")
            };
            directories.Add(Path.Combine(Path.GetDirectoryName(installation.PrefixArguments[0])!,
                "node_modules", "@github", "copilot-win32-" + architecture));
        }
        InspectDirectories(directories);
    }

    public static void InspectDirectories(IEnumerable<string> directories)
    {
        try
        {
            foreach (var directory in directories.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                CopilotPreflight.AssertNoReparseAncestors(directory);
                // Enumerating, rather than File.Exists, makes inaccessible/ambiguous sources fail closed.
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                    if (Path.GetFileName(entry).ToLowerInvariant() is "gh" or "gh.exe" or "gh.com" or "gh.cmd" or "gh.bat" or "gh.ps1")
                        throw new InvalidOperationException("GitHub CLI is present in a reviewer executable search directory. Review launch was blocked to prevent account fallback.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("GitHub CLI executable isolation could not be verified. Review launch was blocked.");
        }
    }
}
