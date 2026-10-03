using System.Windows;
using AIReviewDesk.Core;
using Wpf.Ui.Controls;

namespace AIReviewDesk.App;

public partial class ProjectDialog : FluentWindow
{
    private readonly ProjectRegistration original;
    public ProjectRegistration? Result { get; private set; }

    public ProjectDialog(ProjectRegistration project, IReadOnlyList<string> candidates, bool adding)
    {
        InitializeComponent();
        original = project;
        NameInput.Text = project.DisplayName;
        PathInput.Text = project.RepositoryPath;
        BaseInput.ItemsSource = candidates;
        BaseInput.Text = project.DefaultBase ?? "";
        var defaultIds = (project.DefaultProfileIds ?? [project.DefaultProfileId]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        ProfileInput.ItemsSource = BuiltInProfiles.All.Select(profile => new ProfileChoice(profile, defaultIds.Contains(profile.Id), false)).ToList();
        Heading.Text = adding ? "Add this project" : "Project defaults";
        SaveButton.Content = adding ? "Add project" : "Save defaults";
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = NameInput.Text.Trim();
        var baseRef = BaseInput.Text.Trim();
        if (name.Length == 0) { Validation.Text = "Give the project a display name."; NameInput.Focus(); return; }
        if (baseRef.StartsWith('-') || baseRef.Any(char.IsWhiteSpace) || baseRef.Any(char.IsControl))
        { Validation.Text = "Use a branch or ref without spaces, control characters or a leading dash."; return; }
        var profileIds = (ProfileInput.ItemsSource as IEnumerable<ProfileChoice>)?.Where(choice => choice.IsDefault).Select(choice => choice.Profile.Id).ToList() ?? [];
        if (profileIds.Count == 0) { Validation.Text = "Choose at least one default review profile."; return; }
        Result = original with { DisplayName = name, DefaultBase = baseRef.Length == 0 ? null : baseRef, DefaultProfileId = profileIds[0], DefaultProfileIds = profileIds };
        DialogResult = true;
    }
}
