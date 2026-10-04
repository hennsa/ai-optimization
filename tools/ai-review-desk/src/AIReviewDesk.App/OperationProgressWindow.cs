using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AIReviewDesk.App;

/// <summary>One owned modal shell for review and certification; ShowDialog pumps the
/// dispatcher while the ContentRendered handler awaits work asynchronously.</summary>
public sealed class OperationProgressWindow : FluentWindow
{
    private readonly OperationProgress operation;
    private readonly Func<Task> run;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool started;
    public OperationProgressWindow(OperationProgress operation, Func<Task> run)
    {
        this.operation = operation; this.run = run; DataContext = operation;
        Title = operation.Title; Width = 600; Height = 460; MinWidth = 420; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        var layout = new DockPanel { Margin = new Thickness(24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "_Cancel", MinWidth = 90, Margin = new Thickness(0, 0, 12, 0) };
        cancel.SetBinding(IsEnabledProperty, new Binding(nameof(OperationProgress.CanCancel)));
        cancel.Click += (_, _) => operation.Cancel();
        var close = new Button { MinWidth = 100, IsDefault = true };
        close.SetBinding(ContentProperty, new Binding(nameof(OperationProgress.ActionLabel)));
        close.SetBinding(IsEnabledProperty, new Binding(nameof(OperationProgress.Finished)));
        close.Click += (_, _) => Close();
        buttons.Children.Add(cancel); buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom); layout.Children.Add(buttons);
        var content = new StackPanel();
        Add(nameof(OperationProgress.Identity), 22); Add(nameof(OperationProgress.Context), 14);
        Add(nameof(OperationProgress.Stage), 18); Add(nameof(OperationProgress.Elapsed), 14);
        var bar = new ProgressBar { Height = 4, IsIndeterminate = true, Margin = new Thickness(0, 12, 0, 12) };
        bar.SetBinding(VisibilityProperty, new Binding(nameof(OperationProgress.Finished)) { Converter = new RunningVisibilityConverter() });
        content.Children.Add(bar); Add(nameof(OperationProgress.Explanation), 14);
        if (operation.Stages.Count > 0) content.Children.Add(new ItemsControl { ItemsSource = operation.Stages, Margin = new Thickness(0, 4, 0, 8) });
        layout.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
        timer.Tick += (_, _) => operation.Tick();
        Closed += (_, _) => timer.Stop();
        ContentRendered += RunOnce;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { if (operation.Finished) Close(); else operation.Cancel(); e.Handled = true; } };

        void Add(string property, double size)
        {
            var text = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Focusable = true };
            text.SetBinding(TextBlock.TextProperty, new Binding(property));
            content.Children.Add(text);
        }
    }
    private async void RunOnce(object? sender, EventArgs e)
    {
        if (started) return;
        started = true; timer.Start();
        await Dispatcher.Yield(DispatcherPriority.Background); // Paint before any synchronous preparation.
        if (operation.CancellationRequested) operation.Complete("Cancelled", "Cancelled before execution started.");
        if (operation.Finished) { timer.Stop(); return; }
        try { await run(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { operation.Complete("Failed", "The operation could not complete safely. No accepted result is available."); }
        timer.Stop();
        if (!operation.Finished) operation.Complete("Failed", "The operation did not produce a completed outcome.");
        if (operation.ShouldTransition) { await Task.Delay(650); Close(); }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!operation.Finished) { e.Cancel = true; operation.Cancel(); }
        base.OnClosing(e);
    }
    private sealed class RunningVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
