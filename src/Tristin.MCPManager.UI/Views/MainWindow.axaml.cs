using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Tristin.MCPManager.Core.Models;
using Tristin.MCPManager.UI.ViewModels;

namespace Tristin.MCPManager.UI.Views;

/// <summary>
/// Main application window.
/// Uses manual AXAML loading to avoid Avalonia source generator version conflicts.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly IBrush AvailableBrush = SolidColorBrush.Parse("#C5CBD5");
    private static readonly IBrush WorkingBrush = SolidColorBrush.Parse("#93C5FD");
    private static readonly IBrush ConnectedBrush = SolidColorBrush.Parse("#6EE7B7");
    private static readonly IBrush ErrorBrush = SolidColorBrush.Parse("#FCA5A5");

    public static IValueConverter EditorStateBrush { get; } =
        new FuncValueConverter<EditorState, IBrush>(state => state switch
        {
            EditorState.Connected => ConnectedBrush,
            EditorState.Error => ErrorBrush,
            EditorState.Connecting or EditorState.WaitingForCoplay or EditorState.Disconnecting => WorkingBrush,
            _ => AvailableBrush
        });

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void CopyEndpoint_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard && DataContext is MainViewModel viewModel)
            await clipboard.SetValueAsync(DataFormat.Text, viewModel.McpEndpoint);
    }
}
