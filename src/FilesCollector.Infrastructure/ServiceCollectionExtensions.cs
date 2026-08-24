using FilesCollector.Core;
using FilesCollector.Core.FileSystem;
using FilesCollector.Core.Inventory;
using FilesCollector.Core.Presets;
using FilesCollector.Core.Planning;
using FilesCollector.Core.Prefixes;
using FilesCollector.Core.Reporting;
using FilesCollector.Core.Settings;
using FilesCollector.Infrastructure.FileSystem;
using FilesCollector.Infrastructure.Inventory;
using FilesCollector.Infrastructure.Settings;
using FilesCollector.Infrastructure.Prefixes;
using FilesCollector.Infrastructure.Presets;
using FilesCollector.Infrastructure.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace FilesCollector.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFilesCollectorInfrastructure(this IServiceCollection services, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        services.AddSingleton<IAppPaths>(_ => new AppPaths(executablePath));
        services.AddSingleton<IScanRootProvider, ScanRootProvider>();
        services.AddSingleton<IFileSystem, WindowsFileSystem>();
        services.AddSingleton<IFileInventoryStore, JsonFileInventoryStore>();
        services.AddSingleton<CollectionPlanner>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IReportWriter, MarkdownReportWriter>();
        services.AddSingleton<IPresetRepository, JsonPresetRepository>();
        services.AddSingleton<IPrefixPresetRepository, JsonPrefixPresetRepository>();
        services.AddSingleton<IAppSessionStore, JsonAppSessionStore>();
        return services;
    }
}
