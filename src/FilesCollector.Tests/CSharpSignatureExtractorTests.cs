using FilesCollector.Extractors;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class CSharpSignatureExtractorTests
{
    [Fact]
    public void Extractor_returns_visible_types_and_members_without_method_bodies()
    {
        const string source = """
using System;
namespace Sample;
public sealed class Service
{
    private void Hidden() { Console.WriteLine("hidden"); }
    public int Count { get; private set; }
    protected virtual string Run(string input)
    {
        return input + \" body \";
    }
}
""";
        var extractor = new CSharpSignatureExtractor();

        var result = extractor.Extract("Service.cs", source);

        result.IsSuccessful.Should().BeTrue();
        result.ExtractorId.Should().Be("csharp-roslyn-v1");
        result.Content.Should().Contain("public sealed class Service");
        result.Content.Should().Contain("public int Count");
        result.Content.Should().Contain("protected virtual string Run(string input)");
        result.Content.Should().NotContain("Hidden");
        result.Content.Should().NotContain("body");
    }
}
