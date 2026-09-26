using System.Windows;
using System.Windows.Threading;
using FilesCollector.Core;
using FilesCollector.Infrastructure;
using FilesCollector.Extractors;
using FilesCollector.Core.Signatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FilesCollector.App;

public partial class App : Application
{
    private readonly UnhandledExceptionPolicy _exceptionPolicy = new();
    private ServiceProvider? _serviceProvider;
    private bool _isShowingError;

    /// <summary>
    /// Set when the application shuts down because of an unrecoverable error; the main
    /// window then closes without asking about unsaved changes.
    /// </summary>
    public static bool IsFatalShutdown { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _serviceProvider = ConfigureServices();
            var appPaths = _serviceProvider.GetRequiredService<IAppPaths>();
            ConfigureExceptionHandling(_serviceProvider.GetRequiredService<ILogger<App>>(), appPaths.LogsDirectory);
            SessionEnding += OnSessionEnding;
            ThemeManager.Initialize(appPaths.LocalDataDirectory);
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            WriteStartupFailure(exception);
            IsFatalShutdown = true;
            ShowFatalError(exception, null);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The process path could not be determined.");
        var appPaths = new AppPaths(executablePath);
        var fileLoggerProvider = new FileLoggerProvider(appPaths.LogsDirectory, DateTimeOffset.Now);

        services.AddFilesCollectorInfrastructure(executablePath);
        services.AddSingleton<ISignatureExtractor, CSharpSignatureExtractor>();
        services.AddSingleton<ISignatureExtractor, JavaScriptTypeScriptSignatureExtractor>();
        services.AddSingleton<ISignatureExtractor, PythonSignatureExtractor>();
        services.AddSingleton<ISignatureExtractor, StructuredDataSignatureExtractor>();
        services.AddLogging(builder => builder.AddProvider(fileLoggerProvider));
        services.AddSingleton<PrefixPresetsViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private void ConfigureExceptionHandling(ILogger<App> logger, string logsDirectory)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            var action = _exceptionPolicy.Decide(args.Exception);
            logger.LogCritical(args.Exception, "An unhandled UI exception occurred; action: {Action}.", action);

            if (action == UnhandledExceptionAction.Shutdown)
            {
                IsFatalShutdown = true;
                ShowFatalError(args.Exception, logsDirectory);
                Shutdown(-1);
                return;
            }

            // A second exception while the dialog is open (for example from a timer) is
            // logged only; nested message boxes would pile up.
            if (_isShowingError)
            {
                return;
            }

            _isShowingError = true;
            try
            {
                ShowRecoverableError(args.Exception, logsDirectory);
            }
            finally
            {
                _isShowingError = false;
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            // Exceptions on non-UI threads terminate the process; only logging is possible.
            var exception = args.ExceptionObject as Exception ?? new InvalidOperationException("An unknown unhandled exception occurred.");
            logger.LogCritical(exception, "An unhandled application-domain exception occurred (terminating: {IsTerminating}).", args.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogError(args.Exception, "An unobserved task exception occurred.");
            args.SetObserved();
        };
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        // Windows is logging off or shutting down. The windows are then closed by
        // Application.Shutdown, where Cancel in Closing is ignored, so unsaved changes are
        // handled here (the window remembers the answer and does not ask again).
        if (MainWindow is FilesCollector.App.MainWindow mainWindow && !mainWindow.ConfirmClose())
        {
            e.Cancel = true;
        }
    }

    private static void ShowRecoverableError(Exception exception, string logsDirectory)
    {
        MessageBox.Show(
            $"An unexpected error occurred. The last operation may not have completed, but the application keeps running.{Environment.NewLine}{Environment.NewLine}{exception.Message}{Environment.NewLine}{Environment.NewLine}Details were written to the log:{Environment.NewLine}{logsDirectory}",
            "Files Collector",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void WriteStartupFailure(Exception exception)
    {
        try
        {
            var executablePath = Environment.ProcessPath ?? throw new InvalidOperationException("The process path could not be determined.");
            var applicationDirectory = Path.GetDirectoryName(executablePath) ?? throw new InvalidOperationException("The application directory could not be determined.");
            var portableDataDirectory = Path.Combine(applicationDirectory, "outputs", "app-data", "logs");
            var localDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilesCollector", "logs");
            var logsDirectory = File.Exists(Path.Combine(applicationDirectory, "portable.mode")) ? portableDataDirectory : localDataDirectory;
            Directory.CreateDirectory(logsDirectory);
            var path = Path.Combine(logsDirectory, $"files-collector-startup-{DateTimeOffset.Now:yyyy-MM-dd}.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }

    private static void ShowFatalError(Exception exception, string? logsDirectory)
    {
        var logHint = logsDirectory is null
            ? "Details were written to the log when possible."
            : $"Details were written to the log:{Environment.NewLine}{logsDirectory}";
        MessageBox.Show(
            $"The application could not continue and will close.{Environment.NewLine}{Environment.NewLine}{exception.Message}{Environment.NewLine}{Environment.NewLine}{logHint}",
            "Files Collector",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
