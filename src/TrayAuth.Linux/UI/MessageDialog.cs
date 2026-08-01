using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TrayAuth.Linux.UI;

public enum MessageResult
{
    Ok,
    Yes,
    No,
    Cancel,
}

/// <summary>
/// Avalonia has no built-in message box, so this is the one used everywhere: text plus a row
/// of buttons, dark-themed like the rest of the app.
/// </summary>
public static class MessageDialog
{
    public static Task<MessageResult> ShowOk(Window owner, string title, string text) =>
        Show(owner, title, text, [("OK", MessageResult.Ok, true)]);

    public static Task<MessageResult> ShowYesNo(Window owner, string title, string text) =>
        Show(owner, title, text, [("Yes", MessageResult.Yes, true), ("No", MessageResult.No, false)]);

    public static Task<MessageResult> ShowYesNoCancel(Window owner, string title, string text) =>
        Show(owner, title, text,
        [
            ("Yes", MessageResult.Yes, true),
            ("No", MessageResult.No, false),
            ("Cancel", MessageResult.Cancel, false),
        ]);

    private static async Task<MessageResult> Show(
        Window owner,
        string title,
        string text,
        (string Label, MessageResult Result, bool IsDefault)[] buttons)
    {
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 460,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = LinuxTheme.BackgroundBrush,
            ShowInTaskbar = false,
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        foreach ((string label, MessageResult result, bool isDefault) in buttons)
        {
            var button = new Button
            {
                Content = label,
                MinWidth = 84,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsDefault = isDefault,
            };

            MessageResult captured = result;
            button.Click += (_, _) => dialog.Close(captured);
            buttonRow.Children.Add(button);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = text,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = LinuxTheme.TextBrush,
                },
                buttonRow,
            },
        };

        MessageResult? outcome = await dialog.ShowDialog<MessageResult?>(owner);
        return outcome ?? (buttons.Length == 1 ? MessageResult.Ok : MessageResult.Cancel);
    }
}
