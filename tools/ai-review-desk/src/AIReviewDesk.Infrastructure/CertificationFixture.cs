using AIReviewDesk.Core;
using System.Text;

namespace AIReviewDesk.Infrastructure;

/// <summary>Only this owned fixture can be passed to the compatibility suite.</summary>
public sealed class CertificationFixture : IAsyncDisposable
{
    private readonly string parent;
    public string Root { get; }
    public string Repository => Path.Combine(Root, "repository");
    public string Outside => Path.Combine(Root, "sibling");
    public ProjectRegistration Project => new() { DisplayName = "Compatibility fixture", RepositoryPath = Repository, DefaultBase = "main" };
    private CertificationFixture(string parent) { this.parent = Path.GetFullPath(parent); Root = Path.Combine(this.parent, Guid.NewGuid().ToString("N")); }
    public static async Task<CertificationFixture> CreateAsync(string dataDirectory, CancellationToken ct = default)
    {
        var fixture = new CertificationFixture(Path.Combine(dataDirectory, "CompatibilityFixtures"));
        try
        {
            Directory.CreateDirectory(fixture.Repository); Directory.CreateDirectory(fixture.Outside);
            CopilotPreflight.AssertNoReparseAncestors(fixture.Root);
            await File.WriteAllTextAsync(Path.Combine(fixture.Outside, "canary.txt"), "ARD_FIXED_HARMLESS_OUTSIDE_CANARY", ct);
            Directory.CreateDirectory(Path.Combine(fixture.Repository, "nested"));
            await File.WriteAllTextAsync(Path.Combine(fixture.Repository, "Counter.cs"), "public static class Counter { public static int Increment(int value) => value + 1; }\n", ct);
            await File.WriteAllTextAsync(Path.Combine(fixture.Repository, "SyntheticCorpus.cs"), "// ARD app-owned compatibility fixture; expanded deterministically only for Large tests.\n", ct);
            await File.WriteAllTextAsync(Path.Combine(fixture.Repository, "nested", "marker.txt"), "ARD_SEARCH_MARKER\n", ct);
            foreach (var args in new string[][] { ["init", "-b", "main"], ["add", "."], ["-c", "core.hooksPath=NUL", "-c", "user.name=AI Review Desk fixture", "-c", "user.email=fixture@example.invalid", "commit", "-m", "Harmless certification baseline"] })
            {
                var result = await GitRunner.RunAsync(fixture.Repository, args, 8192, ct);
                if (result.ExitCode != 0) throw new InvalidOperationException("Could not create compatibility fixture.");
            }
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }
    public Task SetCaseAsync(bool defect, CancellationToken ct) => File.WriteAllTextAsync(Path.Combine(Repository, "Counter.cs"),
        defect ? "// Increment must return the next integer.\npublic static class Counter { public static int Increment(int value) => value - 1; }\n" :
            "// Increment returns the next integer.\npublic static class Counter { public static int Increment(int value) => value + 1; }\n", ct);

    public async Task<PromptSizeEvidence> PrepareLargeContextAsync(int targetPromptCharacters = ReviewContextCapability.MaximumPromptCharacterBound, CancellationToken ct = default)
    {
        if (targetPromptCharacters != ReviewContextCapability.MaximumPromptCharacterBound)
            throw new ArgumentOutOfRangeException(nameof(targetPromptCharacters));
        await SetCaseAsync(false, ct);
        // Distinct, source-like method declarations keep the fixture representative
        // without a repeated prose line, random data, or token-count assumptions.
        var source = new StringBuilder(targetPromptCharacters + 64_000)
            .AppendLine("// ARD app-owned deterministic synthetic repository source. No customer data.")
            .AppendLine("namespace SyntheticReviewFixture;")
            .AppendLine("public static class SyntheticCorpus")
            .AppendLine("{");
        for (var i = 0; source.Length < 1_700_000; i++)
        {
            ct.ThrowIfCancellationRequested();
            var multiplier = 17 + (i * 31 % 83);
            var offset = 11 + (i * 19 % 89);
            var modulus = 997 + (i % 7 * 2);
            source.Append("    public static int ReadMetric").Append(i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture))
                .Append("(int seed) => (seed * ").Append(multiplier).Append(" + ").Append(offset).Append(") % ").Append(modulus).AppendLine(";");
        }
        source.AppendLine("}");
        await File.WriteAllTextAsync(Path.Combine(Repository, "SyntheticCorpus.cs"), source.ToString(), ct);
        var input = await new GitReviewContext().PrepareAsync(Project, ReviewScope.WorkingChanges, ct: ct);
        var evidence = ReviewContextCapability.Measure(PromptComposer.Compose(input, ["standard"]));
        if (evidence.CharacterCount != targetPromptCharacters || evidence.ContextClass != ReviewContextClass.Large)
            throw new InvalidOperationException("Synthetic large-context fixture did not compose exactly to the Large v2 tier ceiling.");
        return evidence;
    }
    public ValueTask DisposeAsync()
    {
        if (!Path.GetFullPath(Root).StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid fixture cleanup target.");
        if (Directory.Exists(Root)) Delete(Root);
        return ValueTask.CompletedTask;
    }
    private static void Delete(string path)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(entry);
                else Delete(entry);
            }
            else { if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly); File.Delete(entry); }
        }
        Directory.Delete(path);
    }
}
