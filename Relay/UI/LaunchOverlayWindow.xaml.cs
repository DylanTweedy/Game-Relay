using System.Diagnostics;
using System.IO;
using System.Windows;
using Relay.Data.Models;
using Relay.Services;

namespace Relay.UI;

public partial class LaunchOverlayWindow : Window
{
    private readonly LoggingService _logger;
    private readonly bool _showDetails;
    private string _resolvedTarget = string.Empty;
    private string _resolvedArgs = string.Empty;
    private string _resolvedWorkDir = string.Empty;

    public LaunchOverlayWindow(LoggingService logger, bool showDetails)
    {
        _logger = logger;
        _showDetails = showDetails;
        InitializeComponent();
        Opacity = 0;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Top + 16;
        Opacity = 1;
        if (!_showDetails)
        {
            Height = 230;
        }
    }

    public void ApplyUpdate(LaunchOverlayUpdate update)
    {
        TitleText.Text = string.IsNullOrWhiteSpace(update.DisplayName)
            ? "Relay Launch"
            : update.DisplayName;
        StatusText.Text = string.IsNullOrWhiteSpace(update.Message)
            ? update.Stage.ToString()
            : update.Message;
        TokenSummaryText.Text = $"Tokens: {update.TokenSummary}";

        _resolvedTarget = update.ResolvedTarget ?? string.Empty;
        _resolvedArgs = update.ResolvedArgs ?? string.Empty;
        _resolvedWorkDir = update.ResolvedWorkDir ?? string.Empty;

        RawTargetText.Text = update.RawTarget ?? string.Empty;
        RawArgsText.Text = update.RawArgs ?? string.Empty;
        RawWorkDirText.Text = update.RawWorkDir ?? string.Empty;
        ResolvedTargetText.Text = _resolvedTarget;
        ResolvedArgsText.Text = _resolvedArgs;
        ResolvedWorkDirText.Text = _resolvedWorkDir;

        TargetCheckText.Text = FormatCheck(update.TargetExists);
        WorkDirCheckText.Text = FormatCheck(update.WorkDirExists);

        ExceptionTextBlock.Text = string.IsNullOrWhiteSpace(update.ExceptionText)
            ? string.Empty
            : update.ExceptionText;
    }

    private static string FormatCheck(bool? value)
    {
        return value switch
        {
            true => "OK",
            false => "Missing",
            _ => "Unknown"
        };
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var logPath = _logger.GetLatestLogPath();
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = $"\"{logPath}\"",
            UseShellExecute = true
        });
    }

    private void CopyResolvedCommand_Click(object sender, RoutedEventArgs e)
    {
        var command = $"target=\"{_resolvedTarget}\" args=\"{_resolvedArgs}\" workdir=\"{_resolvedWorkDir}\"";
        System.Windows.Clipboard.SetText(command);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_resolvedTarget))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{_resolvedTarget}\"",
            UseShellExecute = true
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
