using FilesCollector.Core.Rules;
using FluentAssertions;
using Xunit;

namespace FilesCollector.Tests;

public sealed class RuleSetTests
{
    [Fact]
    public void Exact_file_rule_has_priority_over_directory_rule()
    {
        var rules = new RuleSet();
        rules.SetRule("app", PathRuleKind.Directory, CollectionMode.Excluded);
        rules.SetRule("app/Program.cs", PathRuleKind.File, CollectionMode.Full);

        var resolution = rules.Resolve("app/Program.cs", PathRuleKind.File);

        resolution.Mode.Should().Be(CollectionMode.Full);
        resolution.Source.Should().Be(RuleSource.Local);
    }

    [Fact]
    public void Most_specific_directory_rule_is_inherited()
    {
        var rules = new RuleSet();
        rules.SetRule("app", PathRuleKind.Directory, CollectionMode.Excluded);
        rules.SetRule("app/src", PathRuleKind.Directory, CollectionMode.Signatures);

        var resolution = rules.Resolve("app/src/Services/OrderService.cs", PathRuleKind.File);

        resolution.Mode.Should().Be(CollectionMode.Signatures);
        resolution.Source.Should().Be(RuleSource.Inherited);
    }

    [Fact]
    public void Removing_exact_file_rule_restores_the_inherited_rule()
    {
        var rules = new RuleSet();
        rules.SetRule("app", PathRuleKind.Directory, CollectionMode.Listed);
        rules.SetRule("app/settings.json", PathRuleKind.File, CollectionMode.Excluded);

        rules.RemoveRule("app/settings.json", PathRuleKind.File);
        var resolution = rules.Resolve("app/settings.json", PathRuleKind.File);

        resolution.Mode.Should().Be(CollectionMode.Listed);
        resolution.Source.Should().Be(RuleSource.Inherited);
    }

    [Fact]
    public void System_exclusion_cannot_be_overridden_by_a_user_rule()
    {
        var rules = new RuleSet();
        rules.SetRule("files-collector", PathRuleKind.Directory, CollectionMode.Full);

        var resolution = rules.Resolve("files-collector/FilesCollector.exe", PathRuleKind.File, true);

        resolution.Mode.Should().Be(CollectionMode.Excluded);
        resolution.Source.Should().Be(RuleSource.System);
    }

    [Fact]
    public void Directory_rule_applies_to_the_directory_and_all_descendants()
    {
        var rules = new RuleSet();
        rules.SetRule("app/assets", PathRuleKind.Directory, CollectionMode.Listed);

        rules.Resolve("app/assets", PathRuleKind.Directory).Mode.Should().Be(CollectionMode.Listed);
        rules.Resolve("app/assets/icons/logo.svg", PathRuleKind.File).Mode.Should().Be(CollectionMode.Listed);
    }

    [Theory]
    [InlineData("app\\src\\Program.cs", "app/src/Program.cs")]
    [InlineData("/app/src/", "app/src")]
    [InlineData(".", "")]
    public void Relative_paths_are_normalized(string path, string expected)
    {
        RuleSet.NormalizeRelativePath(path).Should().Be(expected);
    }
}
