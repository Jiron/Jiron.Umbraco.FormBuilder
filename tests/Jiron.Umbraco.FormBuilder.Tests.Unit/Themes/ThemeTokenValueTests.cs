using Jiron.Umbraco.FormBuilder.Core.Themes;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Themes;

/// <summary>
/// A theme token's value (16.1's review): what colours, sizes, shadows, easings and font stacks need passes, and nothing
/// that could load anything does, however it is spelled, since a token feeds properties such as <c>background</c>.
/// </summary>
[TestFixture]
public sealed class ThemeTokenValueTests
{
    [Test]
    public void EveryBuiltInThemesValue_IsOne()
    {
        var values = BuiltInThemes.All
            .Select(type => (IFormBuilderTheme)Activator.CreateInstance(type)!)
            .SelectMany(theme => theme.Tokens.Values.Concat(theme.DarkTokens.Values))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(values, Is.Not.Empty);
            Assert.That(values.Where(value => !ThemeTokenValue.IsValid(value)), Is.Empty);
        });
    }

    [TestCase("#1a5fd1")]
    [TestCase("12px")]
    [TestCase(".875em")]
    [TestCase("inherit")]
    [TestCase("0 1px 2px rgb(29 36 51 / .08), 0 4px 16px -2px rgb(29 36 51 / .12)")]
    [TestCase("oklch(70% 0.1 200)")]
    [TestCase("color-mix(in srgb, #fff 20%, var(--jfb-color-accent))")]
    [TestCase("light-dark(#fff, #1d232d)")]
    [TestCase("calc((var(--jfb-radius, 12px) + 2px) * 2)")]
    [TestCase("clamp(1rem, 2.5vw, 2rem)")]
    [TestCase("linear-gradient(90deg, #fff, #000 80%)")]
    [TestCase("cubic-bezier(.23, 1, .32, 1)")]
    [TestCase("steps(4, jump-end)")]
    [TestCase("\"Segoe UI\", system-ui, sans-serif")]
    [TestCase("'Noto Sans JP', 'Source_Han-Sans', sans-serif")]
    [TestCase("\"Hiragino Kaku Gothic ProN\", \"\u6e38\u30b4\u30b7\u30c3\u30af\", sans-serif")]
    public void WhatAColourASizeAShadowAnEasingOrAFontStackNeeds_IsAValue(string value) =>
        Assert.That(ThemeTokenValue.IsValid(value), Is.True);

    [TestCase(null, TestName = "Nothing")]
    [TestCase("", TestName = "Empty")]
    [TestCase("   ", TestName = "Spaces")]
    [TestCase("url(https://evil.example/x.png)", TestName = "An address")]
    [TestCase("URL(x.png)", TestName = "An address in capitals")]
    [TestCase("image-set(\"https://evil.example/x.png\" 1x)", TestName = "An image set")]
    [TestCase("-webkit-image-set(\"x.png\" 1x)", TestName = "A prefixed image set")]
    [TestCase("u\\72l(https://evil.example/x.png)", TestName = "An escaped address")]
    [TestCase("image-s\\65t(\"x.png\" 1x)", TestName = "An escaped image set")]
    [TestCase("#fff\\9", TestName = "An escape outside a function")]
    [TestCase("\\72 ed", TestName = "An escaped keyword")]
    [TestCase("linear-gradient(red, blue), image-set(\"x.png\" 1x)", TestName = "An image set beside a gradient")]
    [TestCase("element(#logo)", TestName = "An element as an image")]
    [TestCase("attr(data-x)", TestName = "An attribute")]
    [TestCase("expression(alert(1))", TestName = "An expression")]
    [TestCase("src(\"x.woff2\")", TestName = "A source")]
    [TestCase("\"https://evil.example/x.png\"", TestName = "An address as a string")]
    [TestCase("\"a\\\"b\"", TestName = "An escaped quote in a string")]
    [TestCase("\"unterminated", TestName = "A string that does not end")]
    [TestCase("red; background: blue", TestName = "A second declaration")]
    [TestCase("red } body { color: red", TestName = "A block's end")]
    [TestCase("<b>", TestName = "Markup")]
    [TestCase("red !important", TestName = "A priority")]
    [TestCase("a /* note */ b", TestName = "A comment")]
    [TestCase("u/**/rl(x)", TestName = "A comment that splits a name")]
    [TestCase("calc(1px", TestName = "A parenthesis that does not close")]
    [TestCase("1px)", TestName = "A parenthesis that does not open")]
    [TestCase("a:b", TestName = "A colon")]
    [TestCase("red\u0007", TestName = "A control character")]
    [TestCase("r\u00e9d", TestName = "A letter outside ASCII and a string")]
    public void WhatCouldLoadOrBreakOut_IsNoValue(string? value) =>
        Assert.That(ThemeTokenValue.IsValid(value), Is.False);
}
