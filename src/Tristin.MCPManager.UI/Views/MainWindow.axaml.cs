using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Tristin.MCPManager.UI.ViewModels;

namespace Tristin.MCPManager.UI.Views;

/// <summary>
/// Main application window.
/// Uses manual AXAML loading to avoid Avalonia source generator version conflicts.
/// </summary>
public partial class MainWindow : Window
{
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
