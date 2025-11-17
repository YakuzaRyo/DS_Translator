using Configer.Utilities;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Configer.Views
{
public sealed partial class RunView : UserControl
{
    private readonly DispatcherQueue _dispatcherQueue;
    private PowerShellTerminalSession? _session;
    private bool _terminalReady;
    private bool _webViewInitialized;
    private bool _virtualHostMapped;
    private ushort _currentCols = 120;
    private ushort _currentRows = 30;
    private const string TerminalHostName = "terminal.configer.local";

    public RunView()
    {
        InitializeComponent();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        Loaded += RunView_Loaded;
        Unloaded += RunView_Unloaded;
        WorkingDirectoryText.Text = AppContext.BaseDirectory;
        UpdateControls(false);
    }

    private async void RunView_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeTerminalAsync();
    }

    private async void RunView_Unloaded(object sender, RoutedEventArgs e)
    {
        await DisposeSessionAsync();
    }

    private async Task InitializeTerminalAsync()
    {
        if (!_webViewInitialized)
        {
            if (TerminalWebView.CoreWebView2 == null)
            {
                await TerminalWebView.EnsureCoreWebView2Async();
            }

            TerminalWebView.CoreWebView2!.WebMessageReceived += CoreWebView2_WebMessageReceived;
            TerminalWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            _webViewInitialized = true;
        }

        if (!_virtualHostMapped)
        {
            var terminalAssetsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Terminal");
            if (!Directory.Exists(terminalAssetsPath))
            {
                ShowInfo($"找不到终端资源目录：{terminalAssetsPath}", InfoBarSeverity.Error, "终端");
                return;
            }

            TerminalWebView.CoreWebView2?.SetVirtualHostNameToFolderMapping(
                TerminalHostName,
                terminalAssetsPath,
                CoreWebView2HostResourceAccessKind.Allow);
            _virtualHostMapped = true;
        }

        TerminalWebView.Source = new Uri($"https://{TerminalHostName}/index.html");
    }

    private async void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var doc = JsonDocument.Parse(args.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty))
            {
                return;
            }

            switch (typeProperty.GetString())
            {
                case "ready":
                    _terminalReady = true;
                    _currentCols = (ushort)Math.Max(20, root.TryGetProperty("cols", out var colsEl) ? colsEl.GetInt32() : 120);
                    _currentRows = (ushort)Math.Max(5, root.TryGetProperty("rows", out var rowsEl) ? rowsEl.GetInt32() : 30);
                    UpdateControls(_session != null);
                    await EnsureSessionAsync();
                    break;

                case "input":
                    if (_session != null && root.TryGetProperty("data", out var dataEl))
                    {
                        var payload = dataEl.GetString();
                        if (!string.IsNullOrEmpty(payload))
                        {
                            _ = _session.SendInputAsync(payload);
                        }
                    }
                    break;

                case "resize":
                    if (root.TryGetProperty("cols", out var newCols))
                    {
                        _currentCols = (ushort)Math.Max(20, newCols.GetInt32());
                    }

                    if (root.TryGetProperty("rows", out var newRows))
                    {
                        _currentRows = (ushort)Math.Max(5, newRows.GetInt32());
                    }

                    _session?.Resize(_currentCols, _currentRows);
                    break;

                case "open-link":
                    if (root.TryGetProperty("uri", out var uriEl))
                    {
                        TryLaunchUri(uriEl.GetString());
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowInfo($"终端通信失败：{ex.Message}", InfoBarSeverity.Error, "终端");
        }
    }

    private void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            ShowInfo($"终端视图加载失败：{args.WebErrorStatus}", InfoBarSeverity.Error, "终端");
        }
    }

    private async Task EnsureSessionAsync()
    {
        if (!_terminalReady || _session != null)
        {
            return;
        }

        var session = new PowerShellTerminalSession();
        session.OutputReceived += Session_OutputReceived;
        session.Exited += Session_Exited;

        try
        {
            await session.StartAsync(_currentCols, _currentRows);
            _session = session;
            UpdateControls(true);
            ShowInfo("PowerShell 已启动，可直接输入命令。", InfoBarSeverity.Success, "PowerShell");
            PostToTerminal(new { type = "focus" });
        }
        catch (Exception ex)
        {
            session.OutputReceived -= Session_OutputReceived;
            session.Exited -= Session_Exited;
            ShowInfo($"PowerShell 启动失败：{ex.Message}", InfoBarSeverity.Error, "PowerShell");
            await session.DisposeAsync();
        }
    }

    private void Session_OutputReceived(object? sender, string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            PostToTerminal(new { type = "output", data });
        });
    }

    private void Session_Exited(object? sender, int exitCode)
    {
        _ = _dispatcherQueue.TryEnqueue(async () =>
        {
            await DisposeSessionAsync();
            var severity = exitCode == 0 ? InfoBarSeverity.Informational : InfoBarSeverity.Warning;
            ShowInfo($"PowerShell 已退出 (代码 {exitCode})。", severity, "PowerShell");
        });
    }

    private async Task DisposeSessionAsync()
    {
        if (_session == null)
        {
            UpdateControls(false);
            return;
        }

        var session = _session;
        _session = null;
        session.OutputReceived -= Session_OutputReceived;
        session.Exited -= Session_Exited;
        await session.DisposeAsync();
        UpdateControls(false);
    }

    private void PostToTerminal(object payload)
    {
        if (TerminalWebView.CoreWebView2 == null)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(payload);
            TerminalWebView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch
        {
            // ignore post errors
        }
    }

    private void UpdateControls(bool sessionRunning)
    {
        RestartButton.IsEnabled = _terminalReady;
        StopButton.IsEnabled = sessionRunning;
        CtrlCButton.IsEnabled = sessionRunning;
        ClearButton.IsEnabled = _terminalReady;
    }

    private void ShowInfo(string message, InfoBarSeverity severity, string title)
    {
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private static void TryLaunchUri(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore launch failures
        }
    }

    private async void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        await RestartTerminalAsync();
    }

    private async Task RestartTerminalAsync()
    {
        await DisposeSessionAsync();
        if (_terminalReady)
        {
            PostToTerminal(new { type = "reset" });
        }
        await EnsureSessionAsync();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null)
        {
            return;
        }

        StopButton.IsEnabled = false;
        try
        {
            await _session.StopAsync();
        }
        catch (Exception ex)
        {
            StopButton.IsEnabled = true;
            ShowInfo($"发送退出命令失败：{ex.Message}", InfoBarSeverity.Warning, "PowerShell");
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        PostToTerminal(new { type = "clear" });
    }

    private async void CtrlCButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session == null)
        {
            return;
        }

        try
        {
            await _session.SendCtrlCAsync();
        }
        catch (Exception ex)
        {
            ShowInfo($"Ctrl+C 发送失败：{ex.Message}", InfoBarSeverity.Warning, "PowerShell");
        }
    }
}
}

