using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;
using Repo = AIReviewDesk.Tests.GitReviewContextTests.TemporaryGitRepository;

namespace AIReviewDesk.Tests;

public sealed class PreviewPreparationTests
{
    [Fact]
    public async Task Preview_skips_all_full_fingerprints_and_uses_production_composer_without_execution_authority()
    {
        using var repo = new Repo(); repo.Write("a.txt", "before"); repo.Commit("initial"); repo.Write("a.txt", "after");
        var stages = new List<string>();
        var fingerprints = 0;
        var context = new GitReviewContext((root, ct) => { fingerprints++; return new GitReviewContext().FingerprintAsync(root, ct); });
        var preview = await context.PrepareAsync(repo.Project, ReviewScope.SelectedPaths, ["a.txt"], preview: true, progress: new Callback(stages.Add));
        Assert.Equal(0, fingerprints);
        Assert.DoesNotContain("Fingerprinting repository", stages);
        Assert.True(preview.IsPreview); Assert.Empty(preview.Fingerprint);
        Assert.Contains("after", PromptComposer.Compose(preview, ["standard"]));
        await Assert.ThrowsAsync<ReviewValidationException>(() => new CopilotService().RunAsync(preview, ["standard"]));
        stages.Clear();
        var execution = await context.PrepareAsync(repo.Project, ReviewScope.SelectedPaths, ["a.txt"], progress: new Callback(stages.Add));
        Assert.Equal(2, fingerprints);
        Assert.False(execution.IsPreview); Assert.Equal(2, stages.Count(s => s == "Fingerprinting repository"));
        Assert.Equal(await new GitReviewContext().FingerprintAsync(repo.Root), execution.Fingerprint);
    }

    [Fact]
    public async Task Preview_retains_secret_path_validation_and_selected_scope_boundaries()
    {
        using var repo = new Repo(); repo.Write("a.txt", "selected"); repo.Write(".env", "synthetic");
        var error = await Assert.ThrowsAsync<PreparationException>(() => new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.WorkingChanges, preview: true));
        Assert.Equal(PreparationFailure.SensitiveFile, error.Reason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.SelectedPaths, ["../outside"], preview: true));
        var preview = await new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.SelectedPaths, ["a.txt"], preview: true);
        Assert.DoesNotContain("synthetic", preview.Context);
    }

    [Fact]
    public async Task Preview_detects_changed_Git_state_during_context_generation()
    {
        using var repo = new Repo(); repo.Write("a.txt", "before");
        var error = await Assert.ThrowsAsync<PreparationException>(() => new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.WorkingChanges, preview: true,
            progress: new Callback(stage => { if (stage == "Building review context") repo.Write("another.txt", "new file"); })));
        Assert.Equal(PreparationFailure.RepositoryChanged, error.Reason);
    }

    [Fact]
    public async Task Preview_conflicts_are_still_rejected()
    {
        using var repo = new Repo(); repo.Write("a.txt", "initial"); repo.Commit("initial");
        repo.Git("checkout", "-b", "other"); repo.Write("a.txt", "other"); repo.Commit("other");
        repo.Git("checkout", "main"); repo.Write("a.txt", "main"); repo.Commit("main");
        // Construct unresolved stages deterministically without a failed helper command.
        var hash = repo.Git("rev-parse", "HEAD:a.txt").StandardOutput.Trim();
        repo.Git("update-index", "--force-remove", "a.txt");
        var info = new System.Diagnostics.ProcessStartInfo("git.exe") { WorkingDirectory = repo.Root, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("update-index"); info.ArgumentList.Add("--index-info");
        using (var process = System.Diagnostics.Process.Start(info)!) { await process.StandardInput.WriteAsync($"100644 {hash} 1\ta.txt\n100644 {hash} 2\ta.txt\n100644 {hash} 3\ta.txt\n"); process.StandardInput.Close(); await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode); }
        var error = await Assert.ThrowsAsync<PreparationException>(() => new GitReviewContext().PrepareAsync(repo.Project, ReviewScope.WorkingChanges, preview: true));
        Assert.Equal(PreparationFailure.Conflicts, error.Reason);
    }
    private sealed class Callback(Action<string> callback) : IProgress<string> { public void Report(string value) => callback(value); }
}
