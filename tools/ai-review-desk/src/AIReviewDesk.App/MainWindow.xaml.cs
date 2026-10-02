using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AIReviewDesk.Core;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AIReviewDesk.App;

public partial class MainWindow : FluentWindow
{
    public DeskViewModel ViewModel { get; } = new();
    private bool initialized;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExecuteAsync(ViewModel.InitializeAsync);
        ApplyTheme();
        initialized = true;
    }

    private async void OnActivated(object? sender, EventArgs e)
    {
        if (initialized && ViewModel.Interactive && ViewModel.State.RefreshOnActivate && ViewModel.Selected != null)
            await ViewModel.ExecuteAsync(ViewModel.RefreshAsync);
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string area }) ViewModel.Area = area;
    }

    private async void OnProjectSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.Interactive && e.AddedItems.Count > 0 && e.AddedItems[0] is ProjectRegistration project && project.Id != ViewModel.Selected?.Id)
            await ViewModel.ExecuteAsync(() => ViewModel.SelectAsync(project));
    }

    private async void OnAdd(object sender, RoutedEventArgs e) => await ViewModel.ExecuteAsync(async () =>
    {
        var picker = new OpenFolderDialog { Title = "Choose a Git repository folder", Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        var snapshot = await ViewModel.DetectAsync(picker.FolderName);
        var project = new ProjectRegistration
        {
            DisplayName = new DirectoryInfo(snapshot.RootPath).Name,
            RepositoryPath = snapshot.RootPath,
            DefaultBase = snapshot.BaseRef,
            DefaultProfileId = ViewModel.State.DefaultProfileId
        };
        var dialog = new ProjectDialog(project, snapshot.BaseCandidates, true) { Owner = this };
        if (dialog.ShowDialog() == true) await ViewModel.SaveProjectAsync(dialog.Result!, true);
    });

    private async void OnEdit(object sender, RoutedEventArgs e) => await ViewModel.ExecuteAsync(async () =>
    {
        if (ViewModel.Selected == null) return;
        var dialog = new ProjectDialog(ViewModel.Selected, ViewModel.Snapshot?.BaseCandidates ?? [], false) { Owner = this };
        if (dialog.ShowDialog() == true) await ViewModel.SaveProjectAsync(dialog.Result!, false);
    });

    private async void OnRemove(object sender, RoutedEventArgs e) => await ViewModel.ExecuteAsync(async () =>
    {
        if (ViewModel.Selected == null) return;
        if (System.Windows.MessageBox.Show(this,
            $"Remove “{ViewModel.Selected.DisplayName}” from AI Review Desk?\n\nOnly this project's registration and defaults will be removed. The repository and all its files will stay in place.",
            "Remove project", System.Windows.MessageBoxButton.OKCancel, MessageBoxImage.Question, System.Windows.MessageBoxResult.Cancel) == System.Windows.MessageBoxResult.OK)
            await ViewModel.RemoveAsync();
    });

    private async void OnRefresh(object sender, RoutedEventArgs e) => await ViewModel.ExecuteAsync(ViewModel.RefreshAsync);
    private async void OnOpenProject(object sender, RoutedEventArgs e) => await ViewModel.ExecuteAsync(() =>
    {
        OpenFolder(ViewModel.Selected?.RepositoryPath);
        return Task.CompletedTask;
    });
    private async void OnOpenData(object sender, RoutedEventArgs e) => await ViewModel.ExecuteAsync(() =>
    {
        Directory.CreateDirectory(ViewModel.DataDirectory);
        OpenFolder(ViewModel.DataDirectory);
        return Task.CompletedTask;
    });

    private static void OpenFolder(string? path)
    {
        if (path == null || !Directory.Exists(path)) throw new InvalidOperationException("This folder is no longer available. Check that its drive is connected.");
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add(Path.GetFullPath(path));
        Process.Start(start)?.Dispose();
    }

    private async void OnSaveSettings(object sender, RoutedEventArgs e) => await ViewModel.ExecuteAsync(async () =>
    {
        SettingsSaved.Text = "";
        await ViewModel.SaveSettingsAsync(ThemeSetting.SelectedValue as string ?? "System", ProfileSetting.SelectedValue as string ?? "standard", RefreshSetting.IsChecked == true);
        ApplyTheme();
        SettingsSaved.Text = "Settings saved.";
    });

    private void ApplyTheme()
    {
        SystemThemeWatcher.UnWatch(this);
        if (ViewModel.State.Theme == "System")
        {
            ApplicationThemeManager.ApplySystemTheme();
            SystemThemeWatcher.Watch(this);
        }
        else ApplicationThemeManager.Apply(ViewModel.State.Theme == "Dark" ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }
}
