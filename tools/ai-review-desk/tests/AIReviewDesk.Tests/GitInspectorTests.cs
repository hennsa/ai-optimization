using System.Diagnostics;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.Tests;

public sealed class GitInspectorTests
{
    [Fact]
    public async Task Inspects_staged_unstaged_untracked_rename_and_conflict_counts()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("old name.txt", "base\n");
        repo.Commit("initial");
        repo.Git("mv", "old name.txt", "renamed file.txt");
        repo.Write("edited.txt", "base\n");
        repo.Git("add", "edited.txt");
        repo.Write("edited.txt", "base\nchanged\n");
        repo.Write("untracked file.txt", "new\n");

        var snapshot = await new GitInspector().InspectAsync(repo.Root);

        Assert.Equal(2, snapshot.StagedCount);
        Assert.Equal(1, snapshot.UnstagedCount);
        Assert.Equal(1, snapshot.UntrackedCount);
        Assert.Equal(0, snapshot.ConflictCount);
        Assert.Equal(3, snapshot.ChangedFileCount);
        Assert.False(snapshot.IsClean);
        Assert.Contains("untracked file", snapshot.DiffSummary);
    }

    [Fact]
    public async Task Detects_merge_conflicts_and_configured_base_precedence()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("shared.txt", "base\n");
        repo.Commit("initial");
        repo.Git("branch", "topic");
        repo.Write("shared.txt", "main side\n");
        repo.Commit("main edit");
        repo.Git("checkout", "topic");
        repo.Write("shared.txt", "topic side\n");
        repo.Commit("topic edit");
        var conflict = repo.GitAllowFailure("merge", "main");
        Assert.NotEqual(0, conflict.ExitCode);

        var snapshot = await new GitInspector().InspectAsync(repo.Root, "main");

        Assert.Equal("main", snapshot.BaseRef);
        Assert.NotNull(snapshot.MergeBaseSha);
        Assert.Equal(1, snapshot.ConflictCount);
        Assert.Equal(1, snapshot.ChangedFileCount);
    }

    [Fact]
    public async Task Missing_configured_base_is_reported_without_falling_back()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("readme.txt", "ready\n");
        repo.Commit("initial");

        var snapshot = await new GitInspector().InspectAsync(repo.Root, "missing-base");

        Assert.Null(snapshot.BaseRef);
        Assert.Contains("not available locally", snapshot.BaseWarning);
        Assert.Contains("main", snapshot.BaseCandidates);
    }

    [Fact]
    public async Task Handles_detached_head_and_unborn_branch()
    {
        using (var repo = new TemporaryGitRepository())
        {
            repo.Write("readme.txt", "ready\n");
            var sha = repo.Commit("initial");
            repo.Git("checkout", "--detach", sha);
            var detached = await new GitInspector().InspectAsync(repo.Root);
            Assert.StartsWith("Detached at ", detached.Branch);
            Assert.Equal(sha, detached.HeadSha);
        }

        using (var repo = new TemporaryGitRepository())
        {
            var unborn = await new GitInspector().InspectAsync(repo.Root);
            Assert.StartsWith("Unborn branch (main)", unborn.Branch);
            Assert.Null(unborn.HeadSha);
        }
    }

    [Fact]
    public async Task Rejects_non_git_and_deleted_repository_paths_with_clear_errors()
    {
        using var repo = new TemporaryGitRepository();
        var inspector = new GitInspector();

        var nonGit = await Assert.ThrowsAsync<InvalidOperationException>(() => inspector.InspectAsync(repo.NonGitFolder));
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => inspector.InspectAsync(repo.MissingFolder));

        Assert.Contains("not inside a Git repository", nonGit.Message);
        Assert.Contains("does not exist", missing.Message);
    }

    [Fact]
    public async Task Finds_repository_root_when_inspecting_nested_folder()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("src/deep/file.txt", "contents\n");
        repo.Commit("initial");

        var snapshot = await new GitInspector().InspectAsync(Path.Combine(repo.Root, "src", "deep"));

        Assert.Equal(Path.GetFullPath(repo.Root), snapshot.RootPath);
        Assert.Equal("main", snapshot.Branch);
    }

    [Fact]
    public async Task Reports_clean_state_and_empty_diff_summary()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("readme.txt", "ready\n");
        repo.Commit("initial");

        var snapshot = await new GitInspector().InspectAsync(repo.Root);

        Assert.True(snapshot.IsClean);
        Assert.Equal(0, snapshot.ChangedFileCount);
        Assert.Equal("No changes", snapshot.DiffSummary);
    }

    [Fact]
    public async Task Uses_remote_default_guess_but_honors_explicit_configured_base()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("readme.txt", "ready\n");
        var head = repo.Commit("initial");
        repo.Git("update-ref", "refs/remotes/origin/release", head);
        repo.Git("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/release");

        var inferred = await new GitInspector().InspectAsync(repo.Root);
        var configured = await new GitInspector().InspectAsync(repo.Root, "main");

        Assert.Equal("origin/release", inferred.BaseRef);
        Assert.Equal("main", configured.BaseRef);
        Assert.Contains("origin/release", inferred.BaseCandidates);
    }

    [Fact]
    public async Task Staged_change_reversed_in_working_tree_still_has_a_diff_summary()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("file.txt", "base\n");
        repo.Commit("initial");
        repo.Write("file.txt", "staged edit\n");
        repo.Git("add", "file.txt");
        repo.Write("file.txt", "base\n");

        var snapshot = await new GitInspector().InspectAsync(repo.Root);

        Assert.Equal(1, snapshot.StagedCount);
        // The working tree has been restored to the committed content, but it still
        // differs from the staged index content, so Git correctly reports one
        // unstaged path as well as one staged path.
        Assert.Equal(1, snapshot.UnstagedCount);
        Assert.False(snapshot.IsClean);
        Assert.DoesNotContain("No changes", snapshot.DiffSummary);
        Assert.Contains("1 tracked file", snapshot.DiffSummary);
    }

    [Fact]
    public async Task Inspection_leaves_git_index_and_working_files_untouched()
    {
        using var repo = new TemporaryGitRepository();
        repo.Write("tracked.txt", "initial\n");
        repo.Commit("initial");
        repo.Write("tracked.txt", "working change\n");
        repo.Write("untracked.txt", "new file\n");
        var trackedPath = Path.Combine(repo.Root, "tracked.txt");
        var untrackedPath = Path.Combine(repo.Root, "untracked.txt");
        var indexPath = Path.Combine(repo.Root, ".git", "index");
        var indexBytesBefore = await File.ReadAllBytesAsync(indexPath);
        var indexTimeBefore = File.GetLastWriteTimeUtc(indexPath);
        var trackedBytesBefore = await File.ReadAllBytesAsync(trackedPath);
        var trackedTimeBefore = File.GetLastWriteTimeUtc(trackedPath);
        var untrackedBytesBefore = await File.ReadAllBytesAsync(untrackedPath);
        var untrackedTimeBefore = File.GetLastWriteTimeUtc(untrackedPath);

        await new GitInspector().InspectAsync(repo.Root);

        Assert.Equal(indexBytesBefore, await File.ReadAllBytesAsync(indexPath));
        Assert.Equal(indexTimeBefore, File.GetLastWriteTimeUtc(indexPath));
        Assert.Equal(trackedBytesBefore, await File.ReadAllBytesAsync(trackedPath));
        Assert.Equal(trackedTimeBefore, File.GetLastWriteTimeUtc(trackedPath));
        Assert.Equal(untrackedBytesBefore, await File.ReadAllBytesAsync(untrackedPath));
        Assert.Equal(untrackedTimeBefore, File.GetLastWriteTimeUtc(untrackedPath));
    }

    [Fact]
    public async Task Honors_an_already_canceled_inspection_token()
    {
        using var repo = new TemporaryGitRepository();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GitInspector().InspectAsync(repo.Root, cancellationToken: cancellation.Token));
    }

    private sealed class TemporaryGitRepository : IDisposable
    {
        private readonly string _parent = Path.Combine(Path.GetTempPath(), "AIReviewDesk.Tests", Guid.NewGuid().ToString("N"));
        public string Root { get; }
        public string NonGitFolder { get; }
        public string MissingFolder { get; }

        public TemporaryGitRepository()
        {
            Root = Path.Combine(_parent, "repository");
            NonGitFolder = Path.Combine(_parent, "ordinary-folder");
            MissingFolder = Path.Combine(_parent, "deleted-repository");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(NonGitFolder);
            Git("init", "-b", "main");
            Git("config", "user.name", "Review Desk Tests");
            Git("config", "user.email", "review-desk-tests@example.invalid");
        }

        public void Write(string name, string content)
        {
            var file = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        public string Commit(string message)
        {
            Git("add", "--all");
            Git("commit", "-m", message);
            return Git("rev-parse", "HEAD").StandardOutput.Trim();
        }

        public (int ExitCode, string StandardOutput, string StandardError) GitAllowFailure(params string[] args)
        {
            var info = new ProcessStartInfo("git.exe")
            {
                WorkingDirectory = Root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, stdout, stderr);
        }

        public (int ExitCode, string StandardOutput, string StandardError) Git(params string[] args)
        {
            var result = GitAllowFailure(args);
            Assert.True(result.ExitCode == 0, $"git {string.Join(' ', args)} failed: {result.StandardError}");
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
