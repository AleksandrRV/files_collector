namespace FilesCollector.Core;

public interface IClock
{
    DateTimeOffset Now { get; }
}
