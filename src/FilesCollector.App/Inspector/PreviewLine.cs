using FilesCollector.Core.Rules;

namespace FilesCollector.App.Inspector;

public enum PreviewLineKind
{
    Normal,
    Header,
    Meta,
    Item,
    Note
}

public sealed record PreviewLine(string Text, PreviewLineKind Kind, CollectionMode? Mode);
