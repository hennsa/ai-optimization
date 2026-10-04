using AIReviewDesk.App;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.Tests;

public sealed class LargeContextCapabilityTests
{
    [Fact]
    public async Task Large_gate_blocks_explicit_and_auto_before_runner_and_selected_paths_reduce_scope()
    {
        var data = Path.Combine(Path.GetTempPath(), "ARD-LargeGate-" + Guid.NewGuid().ToString("N"));
        string fixtureRoot = "";
        try
        {
            await using var fixture = await CertificationFixture.CreateAsync(data);
            fixtureRoot = fixture.Root;
            var evidence = await fixture.PrepareLargeContextAsync();
            Assert.Equal(ReviewContextClass.Large, evidence.ContextClass);

            var model = new CopilotModelChoice("gpt-6-luna", "GPT-6 Luna", ["auto", "high"]);
            var certificate = new ModelCertificate
            {
                ModelId = model.Id, DisplayName = model.Name, CliVersion = "1.0.91", SuiteVersion = CertificationContract.SuiteVersion,
                AuthorityContract = CertificationContract.AuthorityVersion, OutputContract = CertificationContract.OutputVersion,
                TestedAt = DateTimeOffset.UtcNow, ReasoningEfforts = ["high"], ExpectedTools = ["view", "rg", "glob"],
                TechnicalTools = ["view", "grep", "glob"], Probes = CertificationContract.RequiredProbes.Select(name => new CertificationProbe(name, true)).ToArray(),
                Status = CertificationStatus.Certified
            };
            await new CertificationRegistry(data).SaveAsync(certificate);
            var vm = new DeskViewModel(data, projectInspector: _ => Task.FromResult(new RepositorySnapshot()),
                historyLoader: _ => Task.FromResult<IReadOnlyList<ReviewRecord>>([]), accountRefresher: () => Task.CompletedTask);
            await vm.InitializeAsync(new(new AppState { Projects = [fixture.Project], SelectedProjectId = fixture.Project.Id }, null));
            vm.SetMetadata(new([model], null, "synthetic", CliVersion: "1.0.91"));
            vm.SelectedModelId = model.Id;
            var launches = 0;
            vm.ReviewRunner = (input, _, _, _, _) =>
            {
                launches++;
                return Task.FromResult(new ReviewRecord { ProjectId = input.Project.Id, Status = ReviewStatus.Completed });
            };

            await vm.StartReviewAsync(ReviewScope.WorkingChanges, []);
            Assert.Equal(0, launches); Assert.Contains("not been certified", vm.Error); Assert.Contains("large context", vm.Error);
            Assert.Contains(evidence.CharacterCount.ToString("N0"), vm.PromptSizeDisplay); Assert.Contains("untracked files", vm.PromptSizeDisplay);

            vm.SelectedModelId = "auto";
            await vm.StartReviewAsync(ReviewScope.WorkingChanges, []);
            Assert.Equal(0, launches); Assert.Contains("Auto cannot be used", vm.Error);

            vm.SelectedModelId = model.Id;
            await vm.StartReviewAsync(ReviewScope.SelectedPaths, ["Counter.cs"]);
            Assert.Equal(1, launches); Assert.Equal(ReviewContextClass.Normal, vm.LatestReview!.PromptSize!.ContextClass);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot)) throw new InvalidOperationException("Synthetic fixture cleanup did not complete.");
            if (Directory.Exists(data)) Directory.Delete(data, true);
        }
    }
}
