using System.Windows;
using System.Windows.Threading;
using Relay.Data;
using Relay.Data.Models;
using Relay.UI;

namespace Relay.Services;

public sealed class OverlayService(LoggingService logger)
{
    public OverlaySession StartOverlay(Config config, string initialDisplayName)
    {
        var overlayWindow = new LaunchOverlayWindow(logger, config.Overlay.ShowDetails);
        overlayWindow.ApplyUpdate(new LaunchOverlayUpdate
        {
            Stage = LaunchOverlayStage.Resolving,
            DisplayName = initialDisplayName,
            Message = "Resolving paths...",
            TokenSummary = "none"
        });
        overlayWindow.Show();
        logger.Info("Overlay shown.");

        var progress = new Progress<LaunchOverlayUpdate>(update =>
        {
            overlayWindow.ApplyUpdate(update);
            logger.Info($"Overlay update: stage={update.Stage}; message={update.Message}");
        });

        return new OverlaySession(overlayWindow, progress, config.Overlay.AutoCloseSeconds, logger);
    }
}

public sealed class OverlaySession(
    LaunchOverlayWindow window,
    IProgress<LaunchOverlayUpdate> progress,
    int autoCloseSeconds,
    LoggingService logger)
{
    public IProgress<LaunchOverlayUpdate> Progress { get; } = progress;

    public Task CompleteAsync(bool success, string finalMessage, CancellationToken ct = default)
    {
        return window.Dispatcher.InvokeAsync(async () =>
        {
            if (!window.IsVisible)
            {
                return;
            }

            if (!success)
            {
                logger.Info("Overlay retained due to failure.");
                return;
            }

            var seconds = Math.Max(1, autoCloseSeconds);
            logger.Info($"Overlay auto-close in {seconds}s.");
            await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
            if (window.IsVisible)
            {
                window.Close();
                logger.Info("Overlay closed.");
            }
        }, DispatcherPriority.Background).Task.Unwrap();
    }
}
