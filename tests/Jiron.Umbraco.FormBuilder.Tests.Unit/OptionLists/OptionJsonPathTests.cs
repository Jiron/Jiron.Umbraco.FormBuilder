using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// The paths of a URL list (ADR-0076): <c>$</c>, then <c>.name</c>, <c>['name']</c> and <c>[index]</c>, at most 256
/// characters; where a path leads in an answer, and how a value there reads as an option's value or label.
/// </summary>
[TestFixture]
public sealed class OptionJsonPathTests
{
    private const string Answer = """
        {
          "a": {"b_c-9": [10, {"d": "deep"}]},
          "with space": "spaced",
          "it's": "quoted",
          "back\\slash": "escaped",
          "": "empty name",
          "list": [[0, 1], [2, 3]]
        }
        """;

    [TestCase("$", Description = "the answer itself")]
    [TestCase("$.a")]
    [TestCase("$.a.b_c-9")]
    [TestCase("$.a.b_c-9[1].d")]
    [TestCase("$['with space']")]
    [TestCase(@"$['it\'s']")]
    [TestCase(@"$['back\\slash']")]
    [TestCase("$['']")]
    [TestCase("$.list[1][0]")]
    [TestCase("$[0]")]
    [TestCase("$[9999999]")]
    public void APath_IsRead(string path) =>
        Assert.That(OptionJsonPath.Parse(path), Is.Not.Null);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("a")]
    [TestCase("$a")]
    [TestCase("$.")]
    [TestCase("$..a")]
    [TestCase("$.a.")]
    [TestCase("$.a b")]
    [TestCase("$.*")]
    [TestCase("$[*]")]
    [TestCase("$[01]", Description = "a leading zero")]
    [TestCase("$[10000000]", Description = "eight digits")]
    [TestCase("$[-1]")]
    [TestCase("$[]")]
    [TestCase("$[0")]
    [TestCase("$[ 0]")]
    [TestCase("$[a]")]
    [TestCase("$['a'")]
    [TestCase("$['a]")]
    [TestCase("$['a']x")]
    [TestCase("$['it's']", Description = "a quote inside quotes is written as \\'")]
    [TestCase(@"$['a\x']", Description = "only \\' and \\\\ are escapes")]
    [TestCase(@"$['a\']")]
    [TestCase("$[\"a\"]", Description = "names are quoted with '")]
    [TestCase(" $.a")]
    public void AnythingElse_IsNoPath(string? path) =>
        Assert.That(OptionJsonPath.Parse(path), Is.Null);

    [Test]
    public void APathOfAtMost256Characters_IsRead()
    {
        var longest = "$" + string.Concat(Enumerable.Repeat(".a", 127)) + "b";

        Assert.Multiple(() =>
        {
            Assert.That(longest, Has.Length.EqualTo(OptionJsonPath.MaxLength));
            Assert.That(OptionJsonPath.Parse(longest), Is.Not.Null);
            Assert.That(OptionJsonPath.Parse(longest + "c"), Is.Null);
            Assert.That(OptionJsonPath.MaxLength, Is.EqualTo(256));
        });
    }

    [TestCase("$.a.b_c-9[1].d", "deep")]
    [TestCase("$.a.b_c-9[0]", "10")]
    [TestCase("$['with space']", "spaced")]
    [TestCase(@"$['it\'s']", "quoted")]
    [TestCase(@"$['back\\slash']", "escaped")]
    [TestCase("$['']", "empty name")]
    [TestCase("$.list[1][0]", "2")]
    public void APath_LeadsToItsValue(string path, string value)
    {
        using var answer = JsonDocument.Parse(Answer);

        Assert.Multiple(() =>
        {
            Assert.That(OptionJsonPath.Parse(path)!.TrySelect(answer.RootElement, out var found), Is.True);
            Assert.That(OptionJsonPath.Text(found), Is.EqualTo(value));
        });
    }

    [TestCase("$.missing")]
    [TestCase("$.a.missing")]
    [TestCase("$.a.b_c-9[2]", Description = "past the end")]
    [TestCase("$.a[0]", Description = "an index into an object")]
    [TestCase("$.list.a", Description = "a name in a list")]
    [TestCase("$.a.b_c-9[0].d", Description = "a name in a number")]
    [TestCase("$['A']", Description = "names match in their case")]
    public void APathThatLeadsNowhere_SelectsNothing(string path)
    {
        using var answer = JsonDocument.Parse(Answer);

        Assert.That(OptionJsonPath.Parse(path)!.TrySelect(answer.RootElement, out _), Is.False);
    }

    [Test]
    public void TheRoot_IsWherePathsStart()
    {
        using var answer = JsonDocument.Parse(Answer);

        Assert.Multiple(() =>
        {
            Assert.That(OptionJsonPath.Parse("$")!.TrySelect(answer.RootElement, out var root), Is.True);
            Assert.That(root.ValueKind, Is.EqualTo(JsonValueKind.Object));
        });
    }

    [TestCase("\"text\"", "text")]
    [TestCase("\"\"", "")]
    [TestCase("12", "12")]
    [TestCase("-0.50", "-0.50")]
    [TestCase("1e3", "1e3")]
    [TestCase("true", "true")]
    [TestCase("false", "false")]
    [TestCase("null", null)]
    [TestCase("{}", null)]
    [TestCase("[1]", null)]
    public void AValue_ReadsAsTheAnswerWritesIt_OrIsNone(string json, string? text)
    {
        using var value = JsonDocument.Parse(json);

        Assert.That(OptionJsonPath.Text(value.RootElement), Is.EqualTo(text));
    }
}
