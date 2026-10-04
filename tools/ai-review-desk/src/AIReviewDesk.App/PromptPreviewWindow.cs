using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AIReviewDesk.App;

public sealed class PromptPreviewWindow : Window
{
    public PromptPreviewWindow(string prompt, string executionDisplay = "")
    {
        Title = "Preview final review prompt";
        Width = 900;
        Height = 720;
        MinWidth = 600;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = SystemColors.WindowBrush;
        Foreground = SystemColors.WindowTextBrush;

        var layout = new DockPanel { Margin = new Thickness(20) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 12, 0, 0), Foreground = SystemColors.ControlTextBrush, Background = SystemColors.ControlBrush };
        var copy = new Button { Content = "Copy prompt", MinWidth = 110, Margin = new Thickness(0, 12, 0, 0), Foreground = SystemColors.ControlTextBrush, Background = SystemColors.ControlBrush };
        copy.Click += (_, _) => Clipboard.SetText(prompt);
        buttons.Children.Add(copy);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        layout.Children.Add(buttons);
        var settings = new TextBlock { Text = executionDisplay, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(settings, Dock.Top);
        layout.Children.Add(settings);

        var text = new TextBox
        {
            Text = prompt,
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Background = SystemColors.WindowBrush,
            Foreground = SystemColors.WindowTextBrush,
            Padding = new Thickness(12)
        };
        layout.Children.Add(text);
        Content = layout;
    }
}
