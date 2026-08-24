using FilesCollector.Extractors;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class AdditionalSignatureExtractorTests
{
    [Fact]
    public void JavaScript_extractor_returns_exported_declarations()
    {
        var extractor = new JavaScriptTypeScriptSignatureExtractor();
        var result = extractor.Extract("service.ts", "export interface Service { }\nexport async function run(value: string) { return value; }");

        result.IsSuccessful.Should().BeTrue();
        result.ExtractorId.Should().Be("javascript-typescript-lexical-v1");
        result.Content.Should().Contain("export interface Service");
        result.Content.Should().Contain("export async function run(value: string)");
    }

    [Fact]
    public void Python_extractor_excludes_private_methods()
    {
        var extractor = new PythonSignatureExtractor();
        var result = extractor.Extract("service.py", "class Service:\n    def public(self, value):\n        return value\n    def _private(self):\n        return 0\n");

        result.IsSuccessful.Should().BeTrue();
        result.Content.Should().Contain("class Service");
        result.Content.Should().Contain("def public(self, value)");
        result.Content.Should().NotContain("_private");
    }

    [Fact]
    public void Json_extractor_returns_key_structure_without_string_values()
    {
        var extractor = new StructuredDataSignatureExtractor();
        var result = extractor.Extract("settings.json", "{\"token\": \"secret-value\", \"options\": { \"enabled\": true }}");

        result.IsSuccessful.Should().BeTrue();
        result.Content.Should().Contain("token : string");
        result.Content.Should().Contain("options {}");
        result.Content.Should().NotContain("secret-value");
    }
}
