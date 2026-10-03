using System.Diagnostics;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.Tests;

public sealed class GitReviewContextTests
{
    [Fact]
    public async Task Prepares_staged_unstaged_and_bounded_untracked_working_context()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("tracked.txt", "base\n");
        repo.Commit("initial");
        repo.Write("tracked.txt", "staged\n");
        repo.Git("add", "tracked.txt");
        repo.Write("tracked.txt", "working\n");
        repo.Write("new file.txt", "untracked text\n");

        var input = await new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.WorkingChanges);

        Assert.Contains("### Staged changes", input.Context);
        Assert.Contains("### Unstaged changes", input.Context);
        Assert.Contains("### Untracked file: new file.txt", input.Context);
        Assert.Contains("untracked text", input.Context);
        Assert.Equal(input.Fingerprint, await new GitReviewContext().FingerprintAsync(repo.Root));
        Assert.Contains("new file.txt", input.Snapshot.ChangedPaths);
    }

    [Fact]
    public async Task Selected_paths_must_be_changed_normalized_and_are_deterministic()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("one.txt", "one\n");
        repo.Write("two.txt", "two\n");
        repo.Commit("initial");
        repo.Write("one.txt", "changed\n");
        repo.Write("two.txt", "changed\n");

        var input = await new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.SelectedPaths, ["two.txt", "one.txt", "one.txt"]);

        Assert.Equal(new[] { "one.txt", "two.txt" }, input.SelectedPaths);
        Assert.Contains("one.txt", input.Context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.SelectedPaths, ["../outside.txt"]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.SelectedPaths, ["unchanged.txt"]));
    }

    [Fact]
    public async Task Working_scope_rejects_sensitive_files_without_exposing_content()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write(".env", "SYNTHETIC_TEST_SECRET=never-expose\n");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.WorkingChanges));

        Assert.Contains("sensitive file", error.Message);
        Assert.DoesNotContain("never-expose", error.Message);
    }

    [Fact]
    public async Task Large_untracked_files_are_omitted_with_a_clear_note()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("large.txt", new string('x', 128 * 1024 + 1));

        var input = await new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.WorkingChanges);

        Assert.Contains("Untracked content omitted: large.txt (larger than 128 KiB)", input.Context);
        Assert.DoesNotContain(new string('x', 1024), input.Context);
    }

    [Fact]
    public async Task Branch_scope_uses_local_merge_base_against_configured_base()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("base.txt", "base\n");
        repo.Commit("initial");
        repo.Git("branch", "topic");
        repo.Git("checkout", "topic");
        repo.Write("branch.txt", "topic change\n");
        repo.Commit("topic change");

        var input = await new GitReviewContext().PrepareAsync(repo.Project with { DefaultBase = "main" }, ReviewScope.BranchVsBase);

        Assert.Equal(ReviewScope.BranchVsBase, input.Scope);
        Assert.Contains("merge-base", input.Context);
        Assert.Contains("branch.txt", input.Context);
        Assert.Equal("main", input.Snapshot.BaseRef);
    }

    [Fact]
    public async Task Fingerprint_tracks_head_refs_index_tracked_and_nonignored_untracked_content()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("tracked.txt", "base\n");
        repo.Commit("initial");
        var contexts = new GitReviewContext();
        var initial = await contexts.FingerprintAsync(repo.Root);

        repo.Write("tracked.txt", "changed\n");
        var tracked = await contexts.FingerprintAsync(repo.Root);
        Assert.NotEqual(initial, tracked);
        repo.Write(".gitignore", "ignored.txt\n");
        repo.Git("add", ".gitignore");
        repo.Commit("ignore pattern");
        repo.Write("ignored.txt", "ignored\n");
        var beforeIgnoredEdit = await contexts.FingerprintAsync(repo.Root);
        repo.Write("ignored.txt", "ignored changed\n");
        Assert.Equal(beforeIgnoredEdit, await contexts.FingerprintAsync(repo.Root));
        repo.Write("untracked.txt", "one\n");
        var untracked = await contexts.FingerprintAsync(repo.Root);
        repo.Write("untracked.txt", "two\n");
        Assert.NotEqual(untracked, await contexts.FingerprintAsync(repo.Root));
    }

    [Fact]
    public async Task Fingerprinting_fails_closed_for_submodule_index_entries()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("tracked.txt", "base\n");
        var commit = repo.Commit("initial");
        repo.Git("update-index", "--add", "--cacheinfo", $"160000,{commit},nested-module");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new GitReviewContext().FingerprintAsync(repo.Root));

        Assert.Contains("Submodule", error.Message);
    }

    private sealed class TemporaryGitRepository : IDisposable
    {
        private readonly string _parent = Path.Combine(Path.GetTempPath(), "AIReviewDesk.Tests", Guid.NewGuid().ToString("N"));
        public string Root { get; }
        public ProjectRegistration Project => new() { DisplayName = "Fixture", RepositoryPath = Root };

        public TemporaryGitRepository()
        {
            Root = Path.Combine(_parent, "repository");
            Directory.CreateDirectory(Root);
            Git("init", "-b", "main");
            Git("config", "user.name", "Review Desk Tests");
            Git("config", "user.email", "review-desk-tests@example.invalid");
        }

        public void Write(string path, string content)
        {
            var fullPath = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        public string Commit(string message)
        {
            Git("add", "--all");
            Git("commit", "-m", message);
            return Git("rev-parse", "HEAD").StandardOutput.Trim();
        }

        public (int ExitCode, string StandardOutput, string StandardError) Git(params string[] args)
        {
            var info = new ProcessStartInfo("git.exe")
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in args) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var result = (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
            Assert.True(result.ExitCode == 0, $"git {string.Join(' ', args)} failed: {result.Item3}");
            return result;
        }

        public void Dispose()
        {
            if (!Directory.Exists(_parent)) return;
            foreach (var file in Directory.EnumerateFiles(_parent, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            foreach (var directory in Directory.EnumerateDirectories(_parent, "*", SearchOption.AllDirectories))
                File.SetAttributes(directory, FileAttributes.Directory);
            Directory.Delete(_parent, recursive: true);
        }
    }
}
