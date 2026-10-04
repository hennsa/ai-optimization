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
    public DeskViewModel ViewModel { get; }
    private bool initialized;
    private bool closeAfterCompatibility;

    public MainWindow(string? dataDirectory = null)
    {
        ViewModel = new DeskViewModel(dataDirectory);
        InitializeComponent();
        DataContext = ViewModel;
        ViewModel.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is nameof(DeskViewModel.Area) or nameof(DeskViewModel.ShowNewReview) or nameof(DeskViewModel.ShowReviewDetail)) MainContentScroll.ScrollToTop();
            if (closeAfterCompatibility && change.PropertyName == nameof(DeskViewModel.IsCompatibilityTesting) && !ViewModel.IsCompatibilityTesting)
                Dispatcher.BeginInvoke(new Action(Close));
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (ViewModel.IsCompatibilityTesting)
        {
            e.Cancel = true; closeAfterCompatibility = true;
            ViewModel.CancelCompatibility(); // Await process-tree termination and fixture cleanup before closing.
        }
        base.OnClosing(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScopePicker.ItemsSource = new[]
        {
            new ScopeChoice(ReviewScope.WorkingChanges, "Working changes"),
            new ScopeChoice(ReviewScope.BranchVsBase, "Branch versus configured base"),
            new ScopeChoice(ReviewScope.SelectedPaths, "Selected changed paths")
        };
        ScopePicker.DisplayMemberPath = nameof(ScopeChoice.Name);
        ScopePicker.SelectedValuePath = nameof(ScopeChoice.Scope);
        // Items/value paths are configured after XAML binding initialization.
        // Restore the current value without replacing the binding used by setup reuse.
        ScopePicker.SetCurrentValue(ComboBox.SelectedValueProperty, ViewModel.Scope);
        ProfileDetails.SelectedIndex = 0;
        await ViewModel.ExecuteAsync(ViewModel.InitializeAsync);
        ApplyTheme();
        initialized = true;
    }

    private async void OnActivated(object? sender, EventArgs e)
    {
        if (initialized) await ViewModel.RefreshOnActivateAsync();
    }

    private void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string area }) ViewModel.Area = area;
    }

    private void OnProjectTab(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string area }) ViewModel.SelectProjectTab(area == "Reviews");
    }

    private void OnReviewInputChanged(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.Interactive) return;
        if (sender == ScopePicker && ScopePicker.SelectedValue is ReviewScope scope)
        {
            ViewModel.SetReviewScope(scope);
            ViewModel.InvalidateReviewInput();
            if (ViewModel.HasProject)
                _ = ViewModel.ExecuteAsync(() => ViewModel.LoadChangedPathsAsync(scope));
        }
        else ViewModel.InvalidateReviewInput();
    }

    private void OnProfileSelected(object sender, SelectionChangedEventArgs e)
    {
        ViewModel.SelectedProfile = ProfileDetails.SelectedItem as ReviewProfile;
    }

    private void OnHistorySelected(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: ReviewHistoryItem item }) ViewModel.ShowHistoryRecord(item.Record);
    }

    private void OnNewReview(object sender, RoutedEventArgs e) => ViewModel.ShowNewReviewForm();
    private void OnShowHistory(object sender, RoutedEventArgs e)
    {
        HistoryList.SelectedItem = null;
        ViewModel.ShowReviewHistory();
    }
    private async void OnReviewAgain(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExecuteAsync(async () =>
        {
            var setup = await ViewModel.UsePreviousSetupAsync();
            if (setup == null) return;
            ChangedPaths.SelectedItems.Clear();
            foreach (var path in ViewModel.ReviewPaths.Where(p => setup.SelectedPaths.Contains(p.Path))) ChangedPaths.SelectedItems.Add(path);
        });
    }

    private void OnClearFilters(object sender, RoutedEventArgs e)
    {
        ViewModel.SeverityFilter = ViewModel.CertaintyFilter = ViewModel.CategoryFilter = "All";
        ViewModel.FindingSearch = "";
    }

    private void OnCopyLocation(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedFinding?.Finding.File == null) return;
        try { Clipboard.SetText(ViewModel.SelectedFinding.Location); ViewModel.SetReviewProgress("Copied finding location."); }
        catch (Exception ex) { ViewModel.SetError(ex.Message); }
    }

    private async void OnPreviewPrompt(object sender, RoutedEventArgs e)
    {
        string? prompt = null;
        await ViewModel.ExecuteAsync(async () =>
        {
            prompt = await ViewModel.PreparePromptAsync(CurrentScope(), SelectedPaths());
        });
        if (!string.IsNullOrWhiteSpace(prompt)) new PromptPreviewWindow(prompt, ViewModel.ExecutionDisplay) { Owner = this }.ShowDialog();
    }

    private async void OnStartReview(object sender, RoutedEventArgs e) => await ViewModel.StartReviewAsync(CurrentScope(), SelectedPaths());
    private void OnCancelReview(object sender, RoutedEventArgs e) => ViewModel.CancelReview();

    private void OnCopyResult(object sender, RoutedEventArgs e) => CopyHandoff(ViewModel.LatestReview, HandoffFormatter.FormatResult);
    private void OnCopyChatGPT(object sender, RoutedEventArgs e) => CopyHandoff(ViewModel.LatestReview, HandoffFormatter.FormatForChatGPT);
    private void OnCopyCodex(object sender, RoutedEventArgs e) => CopyHandoff(ViewModel.LatestReview, HandoffFormatter.FormatForCodex);

    private void CopyHandoff(ReviewRecord? record, Func<ReviewRecord, string> formatter)
    {
        if (record == null) return;
        try { Clipboard.SetText(formatter(record)); ViewModel.SetReviewProgress("Copied review handoff to the clipboard."); }
        catch (Exception ex) { ViewModel.SetError(ex.Message); }
    }

    private ReviewScope CurrentScope() => ScopePicker.SelectedValue is ReviewScope scope ? scope : ReviewScope.WorkingChanges;
    private IReadOnlyList<string> SelectedPaths() => ChangedPaths.SelectedItems.Cast<ReviewPathChoice>().Select(path => path.Path).ToArray();

    private async void OnRefreshAccount(object sender, RoutedEventArgs e) => await ViewModel.RefreshAccountAsync();
    private async void OnTestCompatibility(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsCompatibilityTesting || sender is not FrameworkElement { Tag: ModelCompatibility model }) return;
        if (System.Windows.MessageBox.Show(this,
            $"Test {model.Model.Name} in a disposable AI Review Desk fixture?\n\nThis makes small Copilot model calls and can consume account allowance. Exact cost is not known beforehand. Your project defaults will stay as they are.",
            model.TestLabel, System.Windows.MessageBoxButton.OKCancel, MessageBoxImage.Information, System.Windows.MessageBoxResult.Cancel) == System.Windows.MessageBoxResult.OK)
            await ViewModel.TestCompatibilityAsync(model);
    }
    private void OnCancelCompatibility(object sender, RoutedEventArgs e) => ViewModel.CancelCompatibility();
    private async void OnSignIn(object sender, RoutedEventArgs e) => await ViewModel.SignInAsync();
    private async void OnSignOut(object sender, RoutedEventArgs e) => await ViewModel.SignOutAsync();
    private async void OnSwitchAccount(object sender, RoutedEventArgs e) => await ViewModel.SwitchAccountAsync();

    private void OnProjectClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ViewModel.Interactive && sender is ListBox list &&
            ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem { DataContext: ProjectRegistration project } &&
            project.Id == ViewModel.Selected?.Id && (ViewModel.ShowProfiles || ViewModel.ShowSettings))
            ViewModel.ShowSelectedProject();
    }

    private void OnProjectKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ViewModel.Interactive && e.Key == System.Windows.Input.Key.Enter && ViewModel.HasProject)
        { ViewModel.ShowSelectedProject(); e.Handled = true; }
    }

    private async void OnProjectSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.Interactive && e.AddedItems.Count > 0 && e.AddedItems[0] is ProjectRegistration project && project.Id != ViewModel.Selected?.Id)
            await ViewModel.ExecuteAsync(() => ViewModel.SelectAsync(project));
    }

    private async void OnAdd(object sender, RoutedEventArgs e)
    {
        // Show the native modal picker before entering ExecuteAsync: that gate disables
        // the workspace Grid, and doing so immediately before ShowDialog caused the
        // first-click picker to disappear on some Windows focus transitions.
        var picker = new OpenFolderDialog { Title = "Choose a Git repository folder", Multiselect = false };
        while (picker.ShowDialog(this) == true)
        {
            var path = picker.FolderName;
            RepositorySnapshot? snapshot = null;
            var invalidRepository = false;
            await ViewModel.ExecuteAsync(async () =>
            {
                try { snapshot = await ViewModel.DetectAsync(path); }
                catch (InvalidOperationException ex) when (IsNotRepositoryError(ex)) { invalidRepository = true; }
            });
            if (invalidRepository)
            {
                if (!ShowInvalidRepositoryDialog(path)) return;
                picker.InitialDirectory = Directory.Exists(path) ? path : null;
                continue;
            }
            if (snapshot == null) return;
            var project = new ProjectRegistration
            {
                DisplayName = new DirectoryInfo(snapshot.RootPath).Name,
                RepositoryPath = snapshot.RootPath,
                DefaultBase = snapshot.BaseRef,
                DefaultProfileId = (ViewModel.State.DefaultProfileIds ?? [ViewModel.State.DefaultProfileId]).FirstOrDefault() ?? "standard",
                DefaultProfileIds = (ViewModel.State.DefaultProfileIds ?? [ViewModel.State.DefaultProfileId]).ToList()
            };
            var dialog = new ProjectDialog(project, snapshot.BaseCandidates, true, ViewModel.Models) { Owner = this };
            if (dialog.ShowDialog() == true)
                await ViewModel.ExecuteAsync(() => ViewModel.SaveProjectAsync(dialog.Result!, true));
            return;
        }
    }

    private static bool IsNotRepositoryError(InvalidOperationException ex) =>
        ex.Message.StartsWith("This folder is not inside a Git repository.", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("not a git repository", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("not a git working tree", StringComparison.OrdinalIgnoreCase);

    private bool ShowInvalidRepositoryDialog(string path)
    {
        var dialog = new Window
        {
            Title = "Not a Git repository", Owner = this, Width = 450, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = "Not a Git repository", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = path, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = "The selected folder is not inside a Git repository.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var retry = new System.Windows.Controls.Button { Content = "Choose another", IsDefault = true, MinWidth = 120 };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        retry.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(retry); content.Children.Add(buttons);
        dialog.Content = content;
        return dialog.ShowDialog() == true;
    }

    private async void OnEdit(object sender, RoutedEventArgs e) => await ViewModel.ExecuteAsync(async () =>
    {
        if (ViewModel.Selected == null) return;
        var dialog = new ProjectDialog(ViewModel.Selected, ViewModel.Snapshot?.BaseCandidates ?? [], false, ViewModel.Models) { Owner = this };
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
        await ViewModel.SaveSettingsAsync(ThemeSetting.SelectedValue as string ?? "System", RefreshSetting.IsChecked == true);
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
