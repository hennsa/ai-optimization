using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;

namespace AIReviewDesk.Tests;

public sealed class RegistryStoreTests
{
    [Fact]
    public async Task Saves_and_loads_settings_with_normalized_repository_paths()
    {
        using var directory = new TemporaryDirectory();
        var store = new RegistryStore(directory.Path);
        var project = new ProjectRegistration
        {
            DisplayName = "Sample",
            RepositoryPath = Path.Combine(directory.Path, "..", "repository"),
            DefaultProfileId = "security"
        };
        var state = new AppState
        {
            Projects = [project],
            SelectedProjectId = project.Id,
            Theme = "Dark",
            DefaultProfileId = "api",
            RefreshOnActivate = false
        };

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        Assert.Null(loaded.Warning);
        Assert.Equal(Path.GetFullPath(project.RepositoryPath), loaded.State.Projects.Single().RepositoryPath);
        Assert.Equal(project.Id, loaded.State.SelectedProjectId);
        Assert.Equal("Dark", loaded.State.Theme);
        Assert.Equal("api", loaded.State.DefaultProfileId);
        Assert.Equal(new[] { "security" }, loaded.State.Projects.Single().DefaultProfileIds);
        Assert.Equal(new[] { "api" }, loaded.State.DefaultProfileIds);
        Assert.False(loaded.State.RefreshOnActivate);
    }

    [Fact]
    public async Task Roundtrips_additive_defaults_without_inserting_standard()
    {
        using var directory = new TemporaryDirectory();
        var store = new RegistryStore(directory.Path);
        var project = new ProjectRegistration { RepositoryPath = Path.Combine(directory.Path, "repo"), DefaultProfileIds = ["database", "security", "security"] };
        await store.SaveAsync(new AppState { Projects = [project], DefaultProfileIds = ["security", "database"] });
        var loaded = await store.LoadAsync();
        Assert.Equal(new[] { "security", "database" }, loaded.State.Projects.Single().DefaultProfileIds);
        Assert.Equal(new[] { "security", "database" }, loaded.State.DefaultProfileIds);
    }

    [Fact]
    public async Task Invalid_additive_defaults_do_not_silently_save()
    {
        using var directory = new TemporaryDirectory();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RegistryStore(directory.Path).SaveAsync(new AppState { DefaultProfileIds = [] }));
    }

    [Fact]
    public async Task Recovers_previous_valid_state_when_primary_json_is_corrupt()
    {
        using var directory = new TemporaryDirectory();
        var store = new RegistryStore(directory.Path);
        await store.SaveAsync(new AppState { Theme = "Light" });
        await store.SaveAsync(new AppState { Theme = "Dark" });
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "app-state.json"), "{ broken");

        var loaded = await store.LoadAsync();

        Assert.Equal("Light", loaded.State.Theme);
        Assert.Contains("recovered", loaded.Warning);
    }

    [Fact]
    public async Task Rejects_duplicate_repository_roots_without_case_sensitivity()
    {
        using var directory = new TemporaryDirectory();
        var root = Path.GetFullPath(Path.Combine(directory.Path, "repo"));
        var state = new AppState
        {
            Projects =
            [
                new ProjectRegistration { RepositoryPath = root },
                new ProjectRegistration { RepositoryPath = root.ToUpperInvariant() }
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RegistryStore(directory.Path).SaveAsync(state));
    }

    [Fact]
    public async Task Load_normalizes_invalid_profile_theme_and_duplicate_entries()
    {
        using var directory = new TemporaryDirectory();
        var store = new RegistryStore(directory.Path);
        var root = Path.Combine(directory.Path, "repo");
        Directory.CreateDirectory(directory.Path);
        var jsonRoot = root.Replace("\\", "/", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "app-state.json"), $$"""
            {"SchemaVersion":1,"Projects":[
              {"Id":"{{Guid.NewGuid()}}","RepositoryPath":"{{jsonRoot}}","DefaultProfileId":"made-up"},
              {"Id":"{{Guid.NewGuid()}}","RepositoryPath":"{{jsonRoot.ToUpperInvariant()}}"}],
              "Theme":"Neon","DefaultProfileId":"made-up"}
            """);

        var loaded = await store.LoadAsync();

        Assert.Single(loaded.State.Projects);
        Assert.Equal("standard", loaded.State.Projects[0].DefaultProfileId);
        Assert.Equal("System", loaded.State.Theme);
        Assert.Equal("standard", loaded.State.DefaultProfileId);
        Assert.Contains("unknown", loaded.Warning);
    }

    [Fact]
    public async Task Preserves_good_backup_when_saving_after_primary_corruption()
    {
        using var directory = new TemporaryDirectory();
        var store = new RegistryStore(directory.Path);
        await store.SaveAsync(new AppState { Theme = "Light" });
        await store.SaveAsync(new AppState { Theme = "Dark" });
        var backupPath = Path.Combine(directory.Path, "app-state.json.bak");
        var backupBefore = await File.ReadAllTextAsync(backupPath);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "app-state.json"), "corrupt");

        await store.SaveAsync(new AppState { Theme = "System" });

        Assert.Equal(backupBefore, await File.ReadAllTextAsync(backupPath));
    }

    [Fact]
    public async Task Refuses_to_overwrite_a_future_schema_file()
    {
        using var directory = new TemporaryDirectory();
        var store = new RegistryStore(directory.Path);
        Directory.CreateDirectory(directory.Path);
        var path = Path.Combine(directory.Path, "app-state.json");
        const string future = "{\"SchemaVersion\":99,\"Projects\":[],\"Theme\":\"System\"}";
        await File.WriteAllTextAsync(path, future);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(new AppState()));
        Assert.Equal(future, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Rejects_duplicate_project_ids()
    {
        using var directory = new TemporaryDirectory();
        var id = Guid.NewGuid();
        var state = new AppState
        {
            Projects =
            [
                new ProjectRegistration { Id = id, RepositoryPath = Path.Combine(directory.Path, "one") },
                new ProjectRegistration { Id = id, RepositoryPath = Path.Combine(directory.Path, "two") }
            ]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => new RegistryStore(directory.Path).SaveAsync(state));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AIReviewDesk.RegistryTests", Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
