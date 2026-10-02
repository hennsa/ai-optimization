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
        ProfileInput.ItemsSource = BuiltInProfiles.All;
        ProfileInput.SelectedValue = project.DefaultProfileId;
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
        if (ProfileInput.SelectedValue is not string profile) { Validation.Text = "Choose a review profile."; return; }
        Result = original with { DisplayName = name, DefaultBase = baseRef.Length == 0 ? null : baseRef, DefaultProfileId = profile };
        DialogResult = true;
    }
}
