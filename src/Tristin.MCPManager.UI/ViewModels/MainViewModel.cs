using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Tristin.MCPManager.Core.Mcp;
using Tristin.MCPManager.Core.Models;
using Tristin.MCPManager.Unity;

namespace Tristin.MCPManager.UI.ViewModels;

/// <summary>
/// Coordinates Unity discovery, Coplay package injection, and the Hub endpoints.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private static readonly Uri CoplayEndpoint = new("http://127.0.0.1:8080/");
    private static readonly Uri HubEndpoint    = new("http://127.0.0.1:9000/");

    private readonly UnityProcessDetector  _detector;
    private readonly UnityPackageConnector _connector;
    private readonly CoplayPackageCache    _packageCache;
    private readonly CoplayMcpServer       _coplayServer;
    private readonly CoplayMcpClient       _coplayClient;
    private readonly HttpMcpReverseProxy   _mcpProxy;
    private readonly CancellationTokenSource _cts = new();
    private int _shutdownStarted;

    [ObservableProperty]
    private ObservableCollection<EditorInstance> _editorInstances = [];

    [ObservableProperty]
    private EditorInstance? _selectedEditor;

    public string McpEndpoint => new Uri(HubEndpoint, "mcp").AbsoluteUri;

    [ObservableProperty]
    private string _logText = "Select a Unity Editor to see its Coplay activity.";

    [ObservableProperty]
    private bool _isActivityOpen;

    [ObservableProperty]
    private string? _startupError;

    [ObservableProperty]
    private string _connectionStatus = string.Empty;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private bool _isDisconnecting;

    [ObservableProperty]
    private bool _isScanning;

    public AsyncRelayCommand ScanEditorsCommand { get; }
    public AsyncRelayCommand ConnectCommand     { get; }
    public AsyncRelayCommand DisconnectCommand  { get; }

    public MainViewModel()
    {
        _detector     = new UnityProcessDetector();
        _packageCache = new CoplayPackageCache();
        _connector    = new UnityPackageConnector(_packageCache, new InjectionRecoveryStore());
        _coplayServer = new CoplayMcpServer(CoplayEndpoint, line => AppendLog($"[Coplay] {line}"));
        _coplayClient = new CoplayMcpClient(CoplayEndpoint);
        _mcpProxy     = new HttpMcpReverseProxy();

        ScanEditorsCommand = new AsyncRelayCommand(ScanEditorsAsync, () => !IsScanning);
        ConnectCommand     = new AsyncRelayCommand(ConnectAsync, CanConnect);
        DisconnectCommand  = new AsyncRelayCommand(DisconnectAsync, CanDisconnect);
    }

    public async Task StartAsync()
    {
        _ = WatchActivityAsync(_cts.Token);
        try
        {
            foreach (var projectPath in await _connector.RecoverPendingAsync(_cts.Token))
                AppendLog($"[Recovery] Restored package state after an unclean shutdown: {projectPath}");

            AppendLog("[Info] Starting official Coplay MCP server ...");
            await _coplayServer.StartAsync(_cts.Token);
            await _mcpProxy.StartAsync(HubEndpoint, CoplayEndpoint, _cts.Token);
            AppendLog($"[Info] Unity MCP Hub ready at {McpEndpoint}");
        }
        catch (Exception ex)
        {
            StartupError = $"Startup failed: {ex.Message}";
            AppendLog($"[Error] Startup failed: {ex.Message}");
        }

        _ = _detector.StartWatchAsync(3000, OnEditorListChanged, _cts.Token);
        await ScanEditorsAsync();
    }

    partial void OnSelectedEditorChanged(EditorInstance? value)
    {
        LogText = value == null
            ? "Select a Unity Editor to see its Coplay activity."
            : $"Reading Coplay activity for {value.ProjectName}…";
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsConnectingChanged(bool value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDisconnectingChanged(bool value)
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsScanningChanged(bool value) => ScanEditorsCommand.NotifyCanExecuteChanged();

    private bool CanConnect()
        => !IsConnecting
           && SelectedEditor is { } editor
           && (editor.State == EditorState.Available
               || editor.State == EditorState.Error && !UnityManifestManager.HasBackup(editor.ProjectPath));

    private bool CanDisconnect()
        => !IsDisconnecting
           && SelectedEditor is { State: EditorState.Connected or EditorState.Error } editor
           && UnityManifestManager.HasBackup(editor.ProjectPath);

    private async Task ScanEditorsAsync()
    {
        if (IsScanning)
            return;

        try
        {
            IsScanning = true;
            var detected = await _detector.DetectAsync(_cts.Token);
            var existing = EditorInstances.ToDictionary(editor => editor.ProcessId);
            var selectedEditor = SelectedEditor;

            EditorInstances = new ObservableCollection<EditorInstance>(
                detected.Select(editor => existing.GetValueOrDefault(editor.ProcessId) ?? editor));

            SelectedEditor = selectedEditor != null && EditorInstances.Contains(selectedEditor)
                ? selectedEditor
                : EditorInstances.FirstOrDefault();

            await UpdateConnectionStatesAsync();
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppendLog($"[Error] Scan failed: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task ConnectAsync()
    {
        if (SelectedEditor is not { } target)
            return;

        try
        {
            ConnectionStatus   = string.Empty;
            IsConnecting       = true;
            target.State       = EditorState.Connecting;
            target.ErrorMessage = null;

            var progress = new Progress<(int percent, string message)>(value =>
            {
                ConnectionStatus = value.message;
                AppendLog($"[Connect] {value.percent}% {value.message}");
            });

            if (!await _connector.ConnectAsync(target, progress, _cts.Token))
                throw new InvalidOperationException(target.ErrorMessage ?? "Package connection failed.");

            target.State = EditorState.WaitingForCoplay;
            if (!await WaitForCoplayConnectionAsync(target, TimeSpan.FromMinutes(3), _cts.Token))
                throw new TimeoutException("Coplay bridge did not connect within 3 minutes. Check Unity Console and package resolution.");

            target.State = EditorState.Connected;
            AppendLog($"[OK] {target.ProjectName} connected. Use set_active_instance from each MCP client session when multiple projects are connected.");
        }
        catch (Exception ex)
        {
            target.State        = EditorState.Error;
            target.ErrorMessage = ex.Message;
            AppendLog($"[Error] Connect failed: {ex.Message}");
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task DisconnectAsync()
    {
        if (SelectedEditor is not { } target)
            return;

        try
        {
            IsDisconnecting     = true;
            target.State        = EditorState.Disconnecting;
            target.ErrorMessage = null;

            if (!await _connector.DisconnectAsync(target, _cts.Token))
                throw new InvalidOperationException(target.ErrorMessage ?? "Manifest restore failed.");

            UnityWindowActivator.ActivateUnityWindow(target.ProcessId);
            target.State = EditorState.Available;
            AppendLog($"[OK] Restored {target.ProjectName} package manifest.");
        }
        catch (Exception ex)
        {
            target.State        = EditorState.Error;
            target.ErrorMessage = ex.Message;
            AppendLog($"[Error] Disconnect failed: {ex.Message}");
        }
        finally
        {
            IsDisconnecting = false;
        }
    }

    private async Task UpdateConnectionStatesAsync()
    {
        IReadOnlyList<CoplayUnityInstance> connected = [];
        try { connected = await _coplayClient.ListInstancesAsync(_cts.Token); }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested) { return; }
        catch { }

        foreach (var editor in EditorInstances)
        {
            if (editor.State is EditorState.Connecting or EditorState.WaitingForCoplay or EditorState.Disconnecting)
                continue;

            var isManagedByHub = UnityManifestManager.HasBackup(editor.ProjectPath);
            if (isManagedByHub && connected.Any(instance => Matches(editor, instance)))
            {
                editor.State = EditorState.Connected;
                editor.ErrorMessage = null;
            }
            else if (isManagedByHub)
            {
                editor.State        = EditorState.Error;
                editor.ErrorMessage = "Hub package state exists, but Coplay is not connected. Disconnect to restore the project.";
            }
            else
            {
                editor.State        = EditorState.Available;
                editor.ErrorMessage = null;
            }
        }

        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> WaitForCoplayConnectionAsync(
        EditorInstance target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var instances = await _coplayClient.ListInstancesAsync(cancellationToken);
                if (instances.Any(instance => Matches(target, instance)))
                    return true;
            }
            catch (HttpRequestException) { }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    private Task OnEditorListChanged(IReadOnlyList<EditorInstance> _)
        => ScanEditorsAsync();

    private static bool Matches(EditorInstance editor, CoplayUnityInstance instance)
        => string.Equals(editor.ProjectName, instance.Project, StringComparison.OrdinalIgnoreCase);

    private static void AppendLog(string line) => Trace.WriteLine(line);

    private async Task WatchActivityAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!IsActivityOpen || SelectedEditor is not { } editor)
                    continue;

                string activity;
                try
                {
                    activity = await CoplayActivityLog.ReadRecentAsync(editor.ProjectPath, cancellationToken);
                    if (string.IsNullOrEmpty(activity))
                        activity = $"No Coplay activity recorded for {editor.ProjectName}. Enable Log Record in Coplay’s Advanced settings in Unity, then run an MCP tool.";
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    activity = $"Could not read the Coplay activity for {editor.ProjectName}. {ex.Message}";
                }

                if (ReferenceEquals(editor, SelectedEditor) && !cancellationToken.IsCancellationRequested)
                    LogText = activity;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    /// <summary>
    /// Restores injected projects and stops all Hub-owned processes and listeners.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
            return;

        _cts.Cancel();
        foreach (var editor in EditorInstances.Where(editor => UnityManifestManager.HasBackup(editor.ProjectPath)))
        {
            try
            {
                await _connector.DisconnectAsync(editor);
                UnityWindowActivator.ActivateUnityWindow(editor.ProcessId);
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] Failed to restore {editor.ProjectName}: {ex.Message}");
            }
        }

        _coplayClient.Dispose();
        _packageCache.Dispose();
        await _mcpProxy.DisposeAsync();
        await _coplayServer.DisposeAsync();
        _cts.Dispose();
    }
}
