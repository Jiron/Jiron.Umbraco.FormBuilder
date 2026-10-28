using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The reader of a stored value with parts (ADR-0058, ADR-0083) on what a client may store but Core never writes: a
/// text that holds a lone surrogate itself, outside any escape, has no parts, as the website's reader gives none, and
/// never throws. A shared vector cannot hold such a text, since a JSON file holds none, so each runtime tests it alone.
/// </summary>
public sealed class FieldPartsJsonTests
{
    private static readonly string High = ((char)0xd83d).ToString();
    private static readonly string Low = ((char)0xde00).ToString();

    [TestCase("text", Description = "a lone high surrogate in a text")]
    [TestCase("name", Description = "a lone low surrogate in a name")]
    [TestCase("joined", Description = "an escaped high surrogate before a lone low one, which the website's JSON.parse would join")]
    public void ALoneSurrogateInTheTextItself_GivesNoParts(string where)
    {
        var json = where switch
        {
            "text" => "{\"first\":\"A" + High + "\"}",
            "name" => "{\"f" + Low + "\":\"Ada\"}",
            _ => "{\"first\":\"" + (char)92 + "ud83d" + Low + "\"}",
        };

        Assert.That(FieldPartsJson.Read(json), Is.Null);
    }

    [Test]
    public void AWellFormedPair_IsRead()
    {
        var parts = FieldPartsJson.Read("{\"first\":\"A" + High + Low + "\"}");

        Assert.That(parts, Is.EqualTo(new[] { new KeyValuePair<string, string>("first", "A" + High + Low) }));
    }
}
