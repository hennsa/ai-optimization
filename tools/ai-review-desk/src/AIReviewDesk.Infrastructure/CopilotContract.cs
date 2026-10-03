using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AIReviewDesk.Infrastructure;

public sealed record CopilotInstallation(string Executable, IReadOnlyList<string> PrefixArguments);
public sealed record CopilotAccountState(bool Available, bool Supported, bool Authenticated, string? Identity, string? Version, string Message, bool ConfigurationBlocked = false)
{
    public string DisplayStatus => !Available ? "Copilot is unavailable" : ConfigurationBlocked ? "Copilot setup is blocked"
        : !Supported ? "Copilot version is unsupported" : Authenticated ? "Signed in" : "Sign-in status unavailable";
}

/// <summary>Deliberate version-specific authority contract. Changing this requires real security verification.</summary>
public static partial class CopilotContract
{
    public const string SupportedVersion = "1.0.91";
    // Fail closed even for an empty profile until credential-safe configuration inspection
    // and a completed authenticated production run have been independently verified.
    public static bool ReviewContractVerified => false;
    public static readonly string[] AllowedTools = ["view", "grep", "glob"];
    public const string DeniedTools = "powershell,create,edit,write,task,skill,list_agents,read_agent,write_agent,run_dynamic_workflow,dynamic_workflows_manage,web_fetch,fetch_copilot_cli_documentation,search_code_subagent,sql,session_store_sql,read_powershell,list_powershell,stop_powershell";

    public static string? ParseVersion(string output) => VersionPattern().Match(output) is { Success: true } match ? match.Groups[1].Value : null;
    public static bool IsSupported(string? version) => OperatingSystem.IsWindows() && version == SupportedVersion;
    [GeneratedRegex(@"^GitHub Copilot CLI (\d+\.\d+\.\d+)\.?\s*$", RegexOptions.Multiline)]
    private static partial Regex VersionPattern();

    public static CopilotInstallation? Detect(string? path = null)
    {
        // Never execute .cmd/.ps1 wrappers or shell-built commands. Resolve the installed npm loader explicitly.
        var directories = (path ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => d.Trim().Trim('"')).Where(d => Path.IsPathFullyQualified(d)).Select(Path.GetFullPath).ToArray();
        var native = directories.Select(d => Path.Combine(d, "copilot.exe")).FirstOrDefault(File.Exists);
        if (native != null) return new(native, []);
        var node = directories.Select(d => Path.Combine(d, "node.exe")).FirstOrDefault(File.Exists);
        var loader = directories.Select(d => Path.Combine(d, "node_modules", "@github", "copilot", "npm-loader.js")).FirstOrDefault(File.Exists);
        return node != null && loader != null ? new(node, [loader]) : null;
    }

    public static ProcessStartInfo StartInfo(CopilotInstallation installation, string cwd, string profile, string cache, IEnumerable<string> arguments, Func<string, string?>? readOperatingSystemVariable = null)
    {
        var info = new ProcessStartInfo(installation.Executable)
        {
            WorkingDirectory = Path.GetFullPath(cwd), UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in installation.PrefixArguments.Concat(arguments)) info.ArgumentList.Add(arg);
        info.Environment.Clear();
        readOperatingSystemVariable ??= Environment.GetEnvironmentVariable;
        foreach (var key in new[] { "SystemRoot", "WINDIR", "PATH", "TEMP", "TMP", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "USERDOMAIN", "USERNAME", "HOMEDRIVE", "HOMEPATH", "COMSPEC", "ProgramData" })
            if (readOperatingSystemVariable(key) is { Length: > 0 } value) info.Environment[key] = value;
        info.Environment["COPILOT_HOME"] = Path.GetFullPath(profile);
        info.Environment["COPILOT_CACHE_HOME"] = Path.GetFullPath(cache);
        info.Environment["COPILOT_AUTO_UPDATE"] = "false";
        info.Environment["USE_TGREP"] = "false";
        // 1.0.91's shipped prompt-mode loader enables these only for the exact value "true".
        // Trusted-folder configuration can still enable repository hooks, so profile/cwd checks remain required.
        info.Environment["GITHUB_COPILOT_PROMPT_MODE_REPO_HOOKS"] = "false";
        info.Environment["GITHUB_COPILOT_PROMPT_MODE_EXTENSIONS"] = "false";
        // No credential, NODE_OPTIONS, proxy, provider, hook, extension or Copilot override environment is inherited.
        return info;
    }

    public static IReadOnlyList<string> ReviewArguments(string repository, string transientLogs) =>
    [
        "--add-dir", Path.GetFullPath(repository), "--disallow-temp-dir",
        "--available-tools", "view,grep,glob", "--allow-tool", "view,grep,glob", "--deny-tool", DeniedTools,
        "--disable-builtin-mcps", "--no-custom-instructions", "--no-remote", "--no-remote-export",
        "--no-ask-user", "--no-auto-update", "--no-auto-login", "--no-eager-powershell-resolution", "--no-experimental",
        "--output-format", "json", "--stream", "on", "--log-level", "none", "--log-dir", transientLogs
    ];
}
