using System.Windows;

namespace AIReviewDesk.App;

public partial class App : Application
{
    private Mutex? instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\AIReviewDesk.Desktop", out var first);
        if (!first)
        {
            MessageBox.Show("AI Review Desk is already open. Switch to its window to continue.", "AI Review Desk");
            Shutdown();
            return;
        }
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
