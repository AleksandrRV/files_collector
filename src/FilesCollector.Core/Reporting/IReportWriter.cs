namespace FilesCollector.Core.Reporting;

public interface IReportWriter
{
    ReportGenerationResult Write(ReportGenerationRequest request, IProgress<ReportGenerationProgress>? progress, CancellationToken cancellationToken);
}
