using System.Text.Json;
using System.Text.Json.Serialization;
using AIReviewDesk.Core;

namespace AIReviewDesk.Infrastructure;

public sealed record RegistryLoadResult(AppState State, string? Warning);

public sealed class RegistryStore
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _filePath;
    private readonly string _backupPath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public RegistryStore(string? directory = null)
    {
        DirectoryPath = string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIReviewDesk")
            : Path.GetFullPath(directory);
        _filePath = Path.Combine(DirectoryPath, "app-state.json");
        _backupPath = _filePath + ".bak";
    }

    public string DirectoryPath { get; }

    public async Task<RegistryLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath) && !File.Exists(_backupPath))
            return new RegistryLoadResult(new AppState(), null);

        string? primaryError = null;
        if (File.Exists(_filePath))
        {
            try
            {
                var state = await ReadStateAsync(_filePath, cancellationToken);
                var normalized = Normalize(state, rejectDuplicatePaths: false, out var warning);
                return new RegistryLoadResult(normalized, warning);
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                primaryError = ex.Message;
            }
        }

        if (File.Exists(_backupPath))
        {
            try
            {
                var recovered = Normalize(await ReadStateAsync(_backupPath, cancellationToken), rejectDuplicatePaths: false, out var recoveryWarning);
                return new RegistryLoadResult(recovered,
                    CombineWarnings("The saved settings file was damaged; settings were recovered from its backup.", recoveryWarning));
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                return new RegistryLoadResult(new AppState(),
                    "The saved settings and its backup could not be read. Default settings were loaded.");
            }
        }

        return new RegistryLoadResult(new AppState(),
            "The saved settings file could not be read. Default settings were loaded.");
    }

    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidOperationException($"Cannot save settings schema version {state.SchemaVersion}; expected {CurrentSchemaVersion}.");
        if (TryGetSchemaVersion(_filePath) is { } savedVersion && savedVersion > CurrentSchemaVersion)
            throw new InvalidOperationException($"The saved settings use newer schema version {savedVersion}; refusing to overwrite them.");
        var normalized = Normalize(state, rejectDuplicatePaths: true, out _);
        Directory.CreateDirectory(DirectoryPath);
        var tempPath = Path.Combine(DirectoryPath, $"app-state.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_filePath) && TryGetSchemaVersion(_filePath) == CurrentSchemaVersion)
                File.Copy(_filePath, _backupPath, overwrite: true);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private static async Task<AppState> ReadStateAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var state = await JsonSerializer.DeserializeAsync<AppState>(stream, JsonOptions, cancellationToken);
        if (state is null)
            throw new InvalidDataException("The settings file was empty.");
        if (state.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported settings version {state.SchemaVersion}.");
        return state;
    }

    private static AppState Normalize(AppState state, bool rejectDuplicatePaths, out string? warning)
    {
        var projects = new List<ProjectRegistration>();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<Guid>();
        var corrections = new List<string>();
        foreach (var project in state.Projects ?? [])
        {
            if (project is null || string.IsNullOrWhiteSpace(project.RepositoryPath))
            {
                if (!rejectDuplicatePaths) corrections.Add("An invalid project entry was removed.");
                continue;
            }
            string fullPath;
            try { fullPath = NormalizePath(project.RepositoryPath); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                if (!rejectDuplicatePaths) corrections.Add("An invalid project path was removed.");
                continue;
            }

            if (!roots.Add(fullPath))
            {
                if (rejectDuplicatePaths)
                    throw new InvalidOperationException($"The project list contains the repository more than once: {fullPath}");
                corrections.Add("A duplicate repository entry was removed.");
                continue;
            }

            var id = project.Id;
            if (id == Guid.Empty || !ids.Add(id))
            {
                if (rejectDuplicatePaths && id != Guid.Empty)
                    throw new InvalidOperationException($"The project list contains a duplicate project identifier: {id}");
                do { id = Guid.NewGuid(); } while (!ids.Add(id));
            }
            if (id != project.Id && !rejectDuplicatePaths)
                corrections.Add("A duplicate or empty project identifier was repaired.");

            var profileId = BuiltInProfiles.Contains(project.DefaultProfileId) ? project.DefaultProfileId : "standard";
            if (profileId != project.DefaultProfileId && !rejectDuplicatePaths)
                corrections.Add("An unknown project review profile was reset to Standard implementation.");

            projects.Add(project with
            {
                Id = id,
                DisplayName = string.IsNullOrWhiteSpace(project.DisplayName) ? Path.GetFileName(fullPath) : project.DisplayName.Trim(),
                RepositoryPath = fullPath,
                DefaultBase = string.IsNullOrWhiteSpace(project.DefaultBase) ? null : project.DefaultBase.Trim(),
                DefaultProfileId = profileId
            });
        }

        Guid? selected = state.SelectedProjectId is { } selectedId && projects.Any(p => p.Id == selectedId)
            ? selectedId : (Guid?)null;
        if (state.SelectedProjectId is not null && selected is null && !rejectDuplicatePaths)
            corrections.Add("The selected project was unavailable and was cleared.");
        var theme = state.Theme is "System" or "Light" or "Dark" ? state.Theme : "System";
        var defaultProfile = BuiltInProfiles.Contains(state.DefaultProfileId) ? state.DefaultProfileId : "standard";
        if (theme != state.Theme && !rejectDuplicatePaths)
            corrections.Add("An unknown theme was reset to System.");
        if (defaultProfile != state.DefaultProfileId && !rejectDuplicatePaths)
            corrections.Add("An unknown default review profile was reset to Standard implementation.");
        warning = corrections.Count == 0 ? null : string.Join(" ", corrections.Distinct(StringComparer.Ordinal));
        return new AppState
        {
            SchemaVersion = CurrentSchemaVersion,
            Projects = projects,
            SelectedProjectId = selected,
            Theme = theme,
            DefaultProfileId = defaultProfile,
            RefreshOnActivate = state.RefreshOnActivate
        };
    }

    private static bool IsRecoverable(Exception ex) => ex is IOException or UnauthorizedAccessException or
        JsonException or InvalidDataException or NotSupportedException;

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path.Trim());
        var pathRoot = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static int? TryGetSchemaVersion(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "SchemaVersion", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.TryGetInt32(out var number))
                    return number;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? CombineWarnings(string? first, string? second) =>
        string.IsNullOrWhiteSpace(second) ? first : $"{first} {second}";
}
