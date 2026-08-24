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
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _serviceProvider = ConfigureServices();
            ConfigureExceptionHandling(_serviceProvider.GetRequiredService<ILogger<App>>());
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            WriteStartupFailure(exception);
            ShowFatalError(exception);
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

    private void ConfigureExceptionHandling(ILogger<App> logger)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            logger.LogCritical(args.Exception, "An unhandled UI exception occurred.");
            args.Handled = true;
            ShowFatalError(args.Exception);
            Shutdown(-1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception ?? new InvalidOperationException("An unknown unhandled exception occurred.");
            logger.LogCritical(exception, "An unhandled application-domain exception occurred.");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogError(args.Exception, "An unobserved task exception occurred.");
            args.SetObserved();
        };
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

    private static void ShowFatalError(Exception exception)
    {
        MessageBox.Show(
            $"The application could not continue. Details were written to the log when possible.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
            "Files Collector",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
