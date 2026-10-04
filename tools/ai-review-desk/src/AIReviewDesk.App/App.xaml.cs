using System.Diagnostics;
using System.Windows;
using AIReviewDesk.Core;
using AIReviewDesk.Infrastructure;
using Wpf.Ui.Appearance;

namespace AIReviewDesk.App;

public partial class App : Application
{
    private Mutex? instance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        StartupDiagnostics.Begin();
        var startupTimer = Stopwatch.StartNew();
        instance = new Mutex(true, "Local\\AIReviewDesk.Desktop", out var first);
        if (!first)
        {
            MessageBox.Show("AI Review Desk is already open. Switch to its window to continue.", "AI Review Desk");
            Shutdown();
            return;
        }

        RegistryLoadResult startupState;
        try
        {
            startupState = await new RegistryStore().LoadAsync();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            startupState = new RegistryLoadResult(new AppState(), "Saved settings could not be read. Default settings were loaded.");
        }
        StartupDiagnostics.Measure("registry settings loaded", startupTimer);

        var themeTimer = Stopwatch.StartNew();
        ApplyTheme(startupState.State.Theme, watchSystem: false);
        StartupDiagnostics.Measure("theme applied before window creation", themeTimer);
        var windowTimer = Stopwatch.StartNew();
        var window = new MainWindow(startupState: startupState);
        StartupDiagnostics.Measure("window created", windowTimer);
        MainWindow = window;
        MainWindow.Show();
        if (startupState.State.Theme == "System")
            SystemThemeWatcher.Watch(window);
        StartupDiagnostics.Mark("window shown");
    }

    internal void ApplyTheme(string theme)
    {
        ApplyTheme(theme, watchSystem: true);
    }

    private void ApplyTheme(string theme, bool watchSystem)
    {
        if (MainWindow != null)
            SystemThemeWatcher.UnWatch(MainWindow);

        if (theme == "Dark")
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        else if (theme == "Light")
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        else
        {
            ApplicationThemeManager.ApplySystemTheme();
            if (watchSystem && MainWindow != null)
                SystemThemeWatcher.Watch(MainWindow);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow != null)
            SystemThemeWatcher.UnWatch(MainWindow);
        instance?.Dispose();
        base.OnExit(e);
    }
}
