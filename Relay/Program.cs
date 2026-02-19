using Relay.Core;
using Relay.Data;
using Relay.Data.Models;
using Relay.Services;
using Relay.UI;
using System.Windows;

namespace Relay;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var logger = new LoggingService();
        var configService = new ConfigService(logger);
        var registryService = new RegistryService(logger);

        logger.Info($"Process start. BaseDirectory={AppContext.BaseDirectory}");
        logger.Info($"Args: {string.Join(" ", args)}");

        configService.EnsureExistsAsync().GetAwaiter().GetResult();
        registryService.EnsureExistsAsync().GetAwaiter().GetResult();

        if (args.Length == 0)
        {
            return RunManagerWindow(logger);
        }

        var parsed = CliParser.Parse(args);
        logger.Info($"Parsed command={parsed.Command}; key={(parsed.GameKey?.ToString() ?? "null")}; tool={(parsed.ToolPath ?? "null")}; overlay={(parsed.OverlayEnabled?.ToString() ?? "null")}");
        switch (parsed.Command)
        {
            case CliCommand.Manager:
                return RunManagerWindow(logger);

            case CliCommand.Validate:
            {
                var config = configService.LoadAsync().GetAwaiter().GetResult();
                var isValid = Validator.ValidateConfig(config, logger);
                if (!isValid)
                {
                    logger.Error("Config validation failed.");
                    return (int)ExitCodes.ConfigInvalid;
                }

                logger.Info("Config validation succeeded.");
                return (int)ExitCodes.Success;
            }

            case CliCommand.Launch:
            {
                if (parsed.GameKey is null)
                {
                    logger.Error("Missing --key value for launch command.");
                    return (int)ExitCodes.ConfigInvalid;
                }

                var config = configService.LoadAsync().GetAwaiter().GetResult();
                var launcher = new GameLauncher(configService, registryService, logger);
                var overlayEnabled = parsed.OverlayEnabled ?? config.Overlay.Enabled;
                if (!overlayEnabled)
                {
                    return launcher.LaunchByKeyAsync(parsed.GameKey.Value, parsed.ToolPath, progress: null).GetAwaiter().GetResult();
                }

                return RunLaunchWithOverlay(launcher, logger, config, parsed.GameKey.Value, parsed.ToolPath);
            }

            default:
                logger.Error("Unknown CLI command.");
                return (int)ExitCodes.ConfigInvalid;
        }
    }

    private static int RunLaunchWithOverlay(GameLauncher launcher, LoggingService logger, Config config, Guid gameKey, string? toolPath)
    {
        try
        {
            var app = new App
            {
                ShutdownMode = ShutdownMode.OnLastWindowClose
            };

            var overlayService = new OverlayService(logger);
            OverlaySession? session = null;
            var finalExitCode = (int)ExitCodes.LaunchFailed;

            app.Startup += (_, _) =>
            {
                session = overlayService.StartOverlay(config, $"Launch {gameKey}");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        finalExitCode = await launcher.LaunchByKeyAsync(gameKey, toolPath, session.Progress);
                        var success = finalExitCode == (int)ExitCodes.Success;
                        await session.CompleteAsync(success, success ? "Started" : "Failed");
                    }
                    catch (Exception ex)
                    {
                        session?.Progress.Report(new LaunchOverlayUpdate
                        {
                            Stage = LaunchOverlayStage.Failed,
                            Message = "Failed",
                            ExceptionText = ex.ToString()
                        });
                        logger.Error(ex.ToString());
                    }
                });
            };

            app.Run();
            return finalExitCode;
        }
        catch (Exception ex)
        {
            logger.Error(ex.ToString());
            return (int)ExitCodes.LaunchFailed;
        }
    }

    private static int RunManagerWindow(LoggingService logger)
    {
        try
        {
            var app = new App();
            app.DispatcherUnhandledException += (_, e) =>
            {
                logger.Error(e.Exception.ToString());
                System.Windows.MessageBox.Show(
                    e.Exception.ToString(),
                    "Relay Manager Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                e.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is not Exception ex)
                {
                    return;
                }

                logger.Error(ex.ToString());
                System.Windows.MessageBox.Show(
                    ex.ToString(),
                    "Relay Manager Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                logger.Error(e.Exception.ToString());
                System.Windows.MessageBox.Show(
                    e.Exception.ToString(),
                    "Relay Manager Background Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                e.SetObserved();
            };

            var window = new MainWindow(logger);
            return app.Run(window);
        }
        catch (Exception ex)
        {
            logger.Error(ex.ToString());
            System.Windows.MessageBox.Show(
                ex.ToString(),
                "Relay Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return (int)ExitCodes.LaunchFailed;
        }
    }
}
