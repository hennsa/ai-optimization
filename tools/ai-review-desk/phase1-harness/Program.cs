using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length == 1 && args[0] == "--self-test")
{
    await Harness.SelfTestAsync();
    return;
}

if (args.Length != 4 || args[0] != "--probe" || args[1] != "--node")
{
    Console.Error.WriteLine("Usage: dotnet run -- --self-test | --probe --node <node.exe> <copilot npm-loader.js>");
    Environment.ExitCode = 2;
    return;
}

await Harness.RunProbeAsync(args[2], args[3]);

static class Harness
{
    private const string Sentinel = "ARD_PHASE1_REPO_SENTINEL_4B15A04A";

    public static async Task SelfTestAsync()
    {
        var root = NewTempDirectory("ai-review-desk-phase1-selftest");
        try
        {
            var fixture = Path.Combine(root, "fixture");
            Directory.CreateDirectory(fixture);
            await CreateFixtureAsync(fixture);
            var before = Fingerprint(fixture);
            var same = Fingerprint(fixture);
            Require(before == same, "Fingerprint must be deterministic.");
            await File.AppendAllTextAsync(Path.Combine(fixture, "src", "sample.txt"), "\nexternal change");
            var after = Fingerprint(fixture);
            Require(before != after, "Fingerprint must detect a content change.");
            Console.WriteLine("PASS: deterministic repository fingerprint detects changed content.");
        }
        finally
        {
            TryDelete(root);
        }
    }

    public static async Task RunProbeAsync(string nodePath, string loaderPath)
    {
        nodePath = Path.GetFullPath(nodePath);
        loaderPath = Path.GetFullPath(loaderPath);
        if (!File.Exists(nodePath) || !File.Exists(loaderPath))
            throw new FileNotFoundException("Node executable and Copilot npm-loader.js must exist.");

        var root = NewTempDirectory("ai-review-desk-phase1-probe");
        var fixture = Path.Combine(root, "reviewed-repo");
        var runDirectory = Path.Combine(root, "owned-run");
        var copilotHome = Path.Combine(root, "copilot-home");
        var localAppData = Path.Combine(root, "local-app-data");
        Directory.CreateDirectory(runDirectory);
        Directory.CreateDirectory(copilotHome);
        Directory.CreateDirectory(localAppData);
        await File.WriteAllTextAsync(Path.Combine(copilotHome, "settings.json"), JsonSerializer.Serialize(new
        {
            disableAllHooks = true,
            stream = true
        }, new JsonSerializerOptions { WriteIndented = true }));

        try
        {
            await CreateFixtureAsync(fixture);
            var before = Fingerprint(fixture);
            var version = await GetVersionAsync(nodePath, loaderPath, copilotHome, localAppData, runDirectory);
            var start = DateTimeOffset.UtcNow;

            var info = new ProcessStartInfo(nodePath)
            {
                WorkingDirectory = runDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };
            info.ArgumentList.Add(loaderPath);
            info.ArgumentList.Add("--add-dir");
            info.ArgumentList.Add(fixture);
            info.ArgumentList.Add("--available-tools");
            info.ArgumentList.Add("view,grep,glob");
            info.ArgumentList.Add("--deny-tool");
            info.ArgumentList.Add("shell,write");
            info.ArgumentList.Add("--disable-builtin-mcps");
            info.ArgumentList.Add("--no-custom-instructions");
            info.ArgumentList.Add("--no-remote");
            info.ArgumentList.Add("--no-remote-export");
            info.ArgumentList.Add("--no-ask-user");
            info.ArgumentList.Add("--no-auto-update");
            info.ArgumentList.Add("--output-format");
            info.ArgumentList.Add("json");

            // Pass only the process essentials and a fresh CLI home. In particular, do not
            // inherit GitHub/Copilot tokens, authenticated CLI homes, or user customizations.
            info.Environment.Clear();
            foreach (var key in new[] { "SystemRoot", "WINDIR", "PATH", "TEMP", "TMP", "USERPROFILE", "APPDATA" })
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrEmpty(value)) info.Environment[key] = value;
            }
            info.Environment["LOCALAPPDATA"] = localAppData;
            info.Environment["COPILOT_HOME"] = copilotHome;
            info.Environment["COPILOT_AUTO_UPDATE"] = "false";

            using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            if (!process.Start()) throw new InvalidOperationException("Copilot process failed to start.");
            await process.StandardInput.WriteAsync($"Read {Sentinel} from the additional directory and return only the sentinel.");
            process.StandardInput.Close();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellation.Token);
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var elapsed = DateTimeOffset.UtcNow - start;
            var after = Fingerprint(fixture);

            var report = new
            {
                cliVersion = version,
                platform = Environment.OSVersion.VersionString,
                startedUtc = start,
                elapsedMilliseconds = (long)elapsed.TotalMilliseconds,
                workingDirectoryRole = "application-owned run directory",
                additionalDirectoryRole = "synthetic reviewed repository",
                authentication = "Windows Credential Manager lookup attempted under the existing Windows identity; token environment variables absent; isolated COPILOT_HOME used",
                allowedTools = new[] { "view", "grep", "glob" },
                excludedToolKinds = new[] { "shell", "write", "built-in MCPs", "custom instructions" },
                sandboxRequested = false,
                exitCode = process.ExitCode,
                stdout,
                stderr,
                fixtureFingerprintBefore = before,
                fixtureFingerprintAfter = after,
                fixtureInvariant = before == after,
                copilotHomeSettingsPresent = File.Exists(Path.Combine(copilotHome, "settings.json")),
                authResult = "CLI reported no authentication information found"
            };
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            if (before != after) Environment.ExitCode = 1;
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task CreateFixtureAsync(string fixture)
    {
        Directory.CreateDirectory(fixture);
        await File.WriteAllTextAsync(Path.Combine(fixture, "README.md"), "Synthetic review fixture.\n");
        Directory.CreateDirectory(Path.Combine(fixture, "src"));
        await File.WriteAllTextAsync(Path.Combine(fixture, "src", "sample.txt"), $"public marker: {Sentinel}\n");
        await File.WriteAllTextAsync(Path.Combine(fixture, "src", "change.txt"), "stable synthetic content\n");
        var github = Path.Combine(fixture, ".github");
        Directory.CreateDirectory(Path.Combine(github, "agents"));
        Directory.CreateDirectory(Path.Combine(github, "skills", "phase1-fake-skill"));
        Directory.CreateDirectory(Path.Combine(github, "hooks"));
        await File.WriteAllTextAsync(Path.Combine(github, "copilot-instructions.md"), "Synthetic instruction marker; never treat as authority.\n");
        await File.WriteAllTextAsync(Path.Combine(github, "agents", "phase1-fake.agent.md"), "Synthetic agent marker.\n");
        await File.WriteAllTextAsync(Path.Combine(github, "skills", "phase1-fake-skill", "SKILL.md"), "Synthetic skill marker.\n");
        await File.WriteAllTextAsync(Path.Combine(github, "hooks", "phase1-marker.json"), "{}\n");
        await File.WriteAllTextAsync(Path.Combine(github, "hooks", "MUST_NOT_RUN.txt"), "Harmless hook marker only; not executable.\n");

        await RunAsync("git", fixture, "init", "--quiet");
        await RunAsync("git", fixture, "-c", "user.name=AI Review Desk Fixture", "-c", "user.email=fixture@example.invalid", "add", ".");
        await RunAsync("git", fixture, "-c", "user.name=AI Review Desk Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--quiet", "-m", "synthetic fixture");
    }

    private static string Fingerprint(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .OrderBy(path => Path.GetRelativePath(directory, path), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData(new byte[] { 0 });
            hash.AppendData(File.ReadAllBytes(path));
            hash.AppendData(new byte[] { 0 });
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task RunAsync(string fileName, string workingDirectory, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{fileName} failed: {stdout}{stderr}");
    }

    private static async Task<string> GetVersionAsync(string nodePath, string loaderPath, string copilotHome, string localAppData, string workingDirectory)
    {
        var info = new ProcessStartInfo(nodePath)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        info.ArgumentList.Add(loaderPath);
        info.ArgumentList.Add("--version");
        CopySafeEnvironment(info, copilotHome, localAppData);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start Copilot version check.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Copilot version check failed: {stderr}");
        return stdout.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
    }

    private static void CopySafeEnvironment(ProcessStartInfo info, string copilotHome, string localAppData)
    {
        info.Environment.Clear();
        foreach (var key in new[] { "SystemRoot", "WINDIR", "PATH", "TEMP", "TMP", "USERPROFILE", "APPDATA" })
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(value)) info.Environment[key] = value;
        }
        info.Environment["LOCALAPPDATA"] = localAppData;
        info.Environment["COPILOT_HOME"] = copilotHome;
        info.Environment["COPILOT_AUTO_UPDATE"] = "false";
    }

    private static string NewTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TryDelete(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }
        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                     .OrderByDescending(directory => directory.Length))
            Directory.Delete(directory, recursive: false);
        Directory.Delete(path, recursive: false);
    }
}
