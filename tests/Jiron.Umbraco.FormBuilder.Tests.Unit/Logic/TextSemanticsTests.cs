using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// What the server's JSON reader hands the shared logic (ADR-0005): it refuses an escaped lone surrogate, which 6.1
/// answers with 400, and passes a noncharacter on, which clean replaces as the browser does. Clean copies a text only
/// when it changes it.
/// </summary>
[TestFixture]
public sealed class TextSemanticsTests
{
    private const char Backslash = (char)92;

    [Test]
    public void JsonReader_EscapedLoneSurrogate_IsRefused()
    {
        using var document = JsonDocument.Parse("\"a" + Backslash + "ud800\"");

        Assert.Throws<InvalidOperationException>(() => document.RootElement.GetString());
    }

    [Test]
    public void Clean_NoncharacterFromJson_BecomesTheReplacementCharacter()
    {
        using var document = JsonDocument.Parse("\"a" + Backslash + "ufffe\"");
        var text = document.RootElement.GetString()!;

        Assert.Multiple(() =>
        {
            Assert.That(text, Has.Length.EqualTo(2));
            Assert.That(TextSemantics.Clean(text), Is.EqualTo("a" + (char)0xFFFD));
        });
    }

    [Test]
    public void Clean_ReplacesEachLoneSurrogateAndNoncharacter_AndKeepsWhatIsAroundThem()
    {
        var pair = char.ConvertFromUtf32(0x1F600);
        var noncharacterPair = char.ConvertFromUtf32(0x1FFFE);
        var text = "a" + pair + (char)0xD800 + "b" + noncharacterPair + "c" + (char)0xDC00;

        Assert.That(TextSemantics.Clean(text), Is.EqualTo("a" + pair + (char)0xFFFD + "b" + (char)0xFFFD + "c" + (char)0xFFFD));
    }

    [Test]
    public void Clean_ReturnsATextWithNothingToChange_AsItIs()
    {
        var text = string.Concat(Enumerable.Repeat("Ada Lovelace 1815 ", 1_000)) + "end";

        Assert.That(TextSemantics.Clean(text), Is.SameAs(text), "no copy of a long answer that cleaning leaves alone (ROADMAP 4.8f)");
    }
}
