using FilesCollector.Core.Planning;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class GlobMatcherTests
{
    [Theory]
    [InlineData("Program.cs")]
    [InlineData("src/Services/OrderService.cs")]
    public void Pattern_without_a_slash_matches_the_file_name_at_any_depth(string relativePath)
    {
        GlobMatcher.IsMatch(relativePath, ["*.cs"]).Should().BeTrue();
    }

    [Fact]
    public void Pattern_without_a_slash_does_not_match_other_extensions()
    {
        GlobMatcher.IsMatch("src/app.ts", ["*.cs"]).Should().BeFalse();
    }

    [Fact]
    public void Pattern_with_a_slash_is_anchored_to_the_relative_path()
    {
        GlobMatcher.IsMatch("src/Program.cs", ["src/*.cs"]).Should().BeTrue();
        GlobMatcher.IsMatch("src/deep/Program.cs", ["src/*.cs"]).Should().BeFalse();
        GlobMatcher.IsMatch("other/Program.cs", ["src/*.cs"]).Should().BeFalse();
    }

    [Fact]
    public void Double_asterisk_directories_match_nested_paths()
    {
        GlobMatcher.IsMatch("app/node_modules/lib/index.js", ["**/node_modules/**"]).Should().BeTrue();
        GlobMatcher.IsMatch("node_modules/a.js", ["**/node_modules/**"]).Should().BeTrue();
        GlobMatcher.IsMatch("app/index.js", ["**/node_modules/**"]).Should().BeFalse();
    }

    [Fact]
    public void Question_mark_matches_exactly_one_character()
    {
        GlobMatcher.IsMatch("a.txt", ["?.txt"]).Should().BeTrue();
        GlobMatcher.IsMatch("ab.txt", ["?.txt"]).Should().BeFalse();
    }

    [Fact]
    public void Matching_is_case_insensitive_and_blank_patterns_are_ignored()
    {
        GlobMatcher.IsMatch("README.MD", ["readme.md"]).Should().BeTrue();
        GlobMatcher.IsMatch("anything.txt", ["", "   "]).Should().BeFalse();
    }
}
