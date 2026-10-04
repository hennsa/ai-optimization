using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

public enum PreparationFailure { GitUnavailable, GitStart, GitCommand, Timeout, Conflicts, SensitiveFile, ScopeTooLarge, RepositoryChanged }

public sealed class PreparationException(PreparationFailure reason) : InvalidOperationException(MessageFor(reason))
{
    public PreparationFailure Reason { get; } = reason;
    private static string MessageFor(PreparationFailure reason) => reason switch
    {
        PreparationFailure.GitUnavailable => "Git executable unavailable. Install Git for Windows and restart AI Review Desk.",
        PreparationFailure.GitStart => "The resolved Git executable could not be started.",
        PreparationFailure.GitCommand => "Git could not prepare the repository context. Check repository availability and the selected base.",
        PreparationFailure.Timeout => "Repository fingerprint/context preparation exceeded the time limit. Narrow the scope or try again.",
        PreparationFailure.Conflicts => "Repository conflicts must be resolved before preparing a review.",
        PreparationFailure.SensitiveFile => "A sensitive file prevents inclusion in review context. Choose selected paths that exclude it.",
        PreparationFailure.ScopeTooLarge => "The selected scope is too large to prepare safely. Choose fewer selected paths.",
        _ => "The repository changed during preparation. Refresh and try again."
    };
}

/// <summary>One process-wide absolute Git selection. No current-directory executable search.</summary>
public static class GitExecutableLocator
{
    private static readonly Lazy<string> Executable = new(() => Resolve(Candidates()));
    public static string Path => Executable.Value;

    private static IEnumerable<string> Candidates()
    {
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") })
            yield return System.IO.Path.Combine(folder, "Git", "cmd", "git.exe");
        if (OperatingSystem.IsWindows())
        {
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\GitForWindows");
                if (key?.GetValue("InstallPath") is string install && System.IO.Path.IsPathFullyQualified(install))
                    yield return System.IO.Path.Combine(install, "cmd", "git.exe");
            }
        }
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator))
        {
            var folder = entry.Trim().Trim('"');
            if (System.IO.Path.IsPathFullyQualified(folder)) yield return System.IO.Path.Combine(folder, "git.exe");
        }
    }

    // Public deterministic seam also validates PATH candidates as Git for Windows installations.
    public static string Resolve(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            try
            {
                if (!System.IO.Path.IsPathFullyQualified(candidate) || !File.Exists(candidate)) continue;
                var full = System.IO.Path.GetFullPath(candidate);
                var directory = System.IO.Path.GetDirectoryName(full)!;
                if (!string.Equals(System.IO.Path.GetFileName(full), "git.exe", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(System.IO.Path.GetFileName(directory), "cmd", StringComparison.OrdinalIgnoreCase)) continue;
                var install = Directory.GetParent(directory)!.FullName;
                if (!File.Exists(System.IO.Path.Combine(install, "mingw64", "bin", "git.exe")) &&
                    !File.Exists(System.IO.Path.Combine(install, "mingw32", "bin", "git.exe"))) continue;
                if (!Directory.Exists(System.IO.Path.Combine(install, "etc"))) continue;
                var safe = true;
                for (var parent = new DirectoryInfo(directory); parent != null; parent = parent.Parent)
                    if ((parent.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        Directory.Exists(System.IO.Path.Combine(parent.FullName, ".git")) || File.Exists(System.IO.Path.Combine(parent.FullName, ".git"))) { safe = false; break; }
                if (!safe || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) continue;
                using var stream = File.OpenRead(full);
                if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') continue;
                return full;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        throw new PreparationException(PreparationFailure.GitUnavailable);
    }
}

public static class GitRunner
{
    public static async Task<(string Text, int ExitCode, string Error, bool Truncated)> RunAsync(
        string root, IReadOnlyList<string> args, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(GitExecutableLocator.Path)
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false)
        };
        foreach (var arg in new[] { "--no-optional-locks", "-c", "core.fsmonitor=false", "-c", "core.pager=cat", "-c", "diff.external=", "-c", "submodule.recurse=false" }.Concat(args))
            info.ArgumentList.Add(arg);
        if (info.ArgumentList.Sum(a => a.Length * 2 + 3) > 28_000) throw new PreparationException(PreparationFailure.ScopeTooLarge);
        foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        info.Environment["GIT_PAGER"] = "cat";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = new Process { StartInfo = info };
        try { if (!process.Start()) throw new PreparationException(PreparationFailure.GitStart); }
        catch (System.ComponentModel.Win32Exception) { throw new PreparationException(PreparationFailure.GitStart); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var stdout = ReadAsync(process.StandardOutput, limit, linked.Token);
        var stderr = ReadAsync(process.StandardError, 8192, linked.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            var output = await stdout;
            return (output.Text, process.ExitCode, (await stderr).Text, output.Truncated);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
            if (ct.IsCancellationRequested) throw;
            throw new PreparationException(PreparationFailure.Timeout);
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadAsync(StreamReader reader, int limit, CancellationToken ct)
    {
        var result = new StringBuilder(Math.Min(limit, 8192));
        var buffer = new char[8192];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            var remaining = limit - result.Length;
            if (remaining > 0) result.Append(buffer, 0, Math.Min(count, remaining));
            truncated |= count > remaining;
        }
        return (result.ToString(), truncated);
    }
}
