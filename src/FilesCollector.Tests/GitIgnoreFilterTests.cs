using FilesCollector.Core.Ignore;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class GitIgnoreFilterTests
{
    // A real path is used so that the relative-path calculation behaves the same way
    // it does at run time; the folder is never read from disk.
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "FilesCollectorTests", "gitignore-root");

    private static GitIgnoreFilter Create(params string[] lines)
    {
        return GitIgnoreFilter.Create(Path.Combine(Root, ".gitignore"), Root, lines);
    }

    [Fact]
    public void Blank_lines_and_comments_are_not_rules()
    {
        var filter = Create(string.Empty, "   ", "# a comment");

        filter.IsEmpty.Should().BeTrue();
        filter.PatternCount.Should().Be(0);
        filter.IsIgnored("src/Program.cs", isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void Simple_name_matches_at_any_depth()
    {
        var filter = Create("bin");

        filter.IsIgnored("bin", isDirectory: true).Should().BeTrue();
        filter.IsIgnored("src/bin", isDirectory: true).Should().BeTrue();
        filter.IsIgnored("src/bin/app.dll", isDirectory: false).Should().BeTrue();
        filter.IsIgnored("src/binary.cs", isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void Extension_pattern_matches_files_in_every_directory()
    {
        var filter = Create("*.log");

        filter.IsIgnored("trace.log", isDirectory: false).Should().BeTrue();
        filter.IsIgnored("logs/app/trace.log", isDirectory: false).Should().BeTrue();
        filter.IsIgnored("logs/app/trace.txt", isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void Leading_slash_anchors_the_pattern_to_the_root()
    {
        var filter = Create("/build");

        filter.IsIgnored("build", isDirectory: true).Should().BeTrue();
        filter.IsIgnored("src/build", isDirectory: true).Should().BeFalse();
    }

    [Fact]
    public void Trailing_slash_restricts_the_pattern_to_directories()
    {
        var filter = Create("cache/");

        filter.IsIgnored("cache", isDirectory: true).Should().BeTrue();
        filter.IsIgnored("cache", isDirectory: false).Should().BeFalse();
        filter.IsIgnored("cache/data.bin", isDirectory: false).Should().BeTrue();
    }

    [Fact]
    public void Double_asterisk_matches_intermediate_directories()
    {
        var filter = Create("**/generated/**");

        filter.IsIgnored("generated/model.cs", isDirectory: false).Should().BeTrue();
        filter.IsIgnored("src/app/generated/deep/model.cs", isDirectory: false).Should().BeTrue();
        filter.IsIgnored("src/app/model.cs", isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void Single_asterisk_does_not_cross_directory_separators()
    {
        var filter = Create("src/*.cs");

        filter.IsIgnored("src/Program.cs", isDirectory: false).Should().BeTrue();
        filter.IsIgnored("src/app/Program.cs", isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void Question_mark_matches_exactly_one_character()
    {
        var filter = Create("file?.txt");

        filter.IsIgnored("file1.txt", isDirectory: false).Should().BeTrue();
        filter.IsIgnored("file.txt", isDirectory: false).Should().BeFalse();
        filter.IsIgnored("file12.txt", isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void Character_class_is_supported()
    {
        var filter = Create("temp[0-9].tmp");

        filter.IsIgnored("temp3.tmp", isDirectory: false).Should().BeTrue();
        filter.IsIgnored("tempX.tmp", isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void Negation_re_includes_a_previously_ignored_file()
    {
        var filter = Create("*.log", "!keep.log");

        filter.IsIgnored("trace.log", isDirectory: false).Should().BeTrue();
        filter.IsIgnored("keep.log", isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void Negation_cannot_re_include_a_file_inside_an_ignored_directory()
    {
        var filter = Create("node_modules/", "!node_modules/keep.js");

        filter.IsIgnored("node_modules/keep.js", isDirectory: false).Should().BeTrue();
    }

    [Fact]
    public void The_last_matching_pattern_wins()
    {
        var filter = Create("!*.log", "*.log");

        filter.IsIgnored("trace.log", isDirectory: false).Should().BeTrue();
    }

    [Fact]
    public void Patterns_are_relative_to_the_folder_that_contains_the_gitignore_file()
    {
        var filter = GitIgnoreFilter.Create(Path.Combine(Root, "src", ".gitignore"), Root, ["/bin"]);

        filter.IsIgnored("src/bin", isDirectory: true).Should().BeTrue();
        filter.IsIgnored("bin", isDirectory: true).Should().BeFalse();
        filter.IsIgnored("tests/bin", isDirectory: true).Should().BeFalse();
    }

    [Fact]
    public void The_scan_root_itself_is_never_ignored()
    {
        var filter = Create("*");

        filter.IsIgnored(string.Empty, isDirectory: true).Should().BeFalse();
    }

    [Fact]
    public void Backslash_separated_paths_are_normalized()
    {
        var filter = Create("obj/");

        filter.IsIgnored(@"src\obj\app.dll", isDirectory: false).Should().BeTrue();
    }

    [Fact]
    public void Escaped_special_characters_are_matched_literally()
    {
        var filter = Create(@"\#notes.txt");

        filter.IsIgnored("#notes.txt", isDirectory: false).Should().BeTrue();
        filter.IsIgnored("notes.txt", isDirectory: false).Should().BeFalse();
    }

    [Fact]
    public void Trailing_whitespace_is_ignored_unless_it_is_escaped()
    {
        var filter = Create("report.txt   ");

        filter.IsIgnored("report.txt", isDirectory: false).Should().BeTrue();
    }

    [Fact]
    public void A_gitignore_file_outside_the_scan_root_applies_from_the_root()
    {
        var filter = GitIgnoreFilter.Create(Path.Combine(Root, "..", "shared", ".gitignore"), Root, ["*.log"]);

        filter.IsIgnored("logs/trace.log", isDirectory: false).Should().BeTrue();
    }
}
