using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// What the vectors of tests/vectors/messages.json do not reach (ROADMAP 6.2g, ADR-0099), the same as the runtime's
/// messages.test.ts: a template's name without a value and a brace left open stay as written, a value is never read as a
/// template, and a culture takes its own words, its more general culture's or English's.
/// </summary>
[TestFixture]
public sealed class VisitorMessagesTests
{
    [Test]
    public void Fill_KeepsANameItHasNoValueFor_AsWritten() =>
        Assert.That(VisitorMessages.Fill("Enter {min} to {max}", ("min", "1")), Is.EqualTo("Enter 1 to {max}"));

    [Test]
    public void Fill_KeepsABraceLeftOpen_AndWhatFollowsIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VisitorMessages.Fill("Enter {min} or {more", ("min", "1")), Is.EqualTo("Enter 1 or {more"));
            Assert.That(VisitorMessages.Fill("Enter } {min}", ("min", "1")), Is.EqualTo("Enter } 1"));
        });
    }

    [Test]
    public void Fill_NeverReadsAValueAsATemplate() =>
        Assert.That(VisitorMessages.Fill("{a} and {b}", ("a", "{b}"), ("b", "x")), Is.EqualTo("{b} and x"));

    [Test]
    public void Of_TakesACulturesOwnWords_ThenItsMoreGeneralCultures_InAnyLetterCase_ThenEnglish()
    {
        var german = VisitorMessageWords.Of("de");

        Assert.Multiple(() =>
        {
            Assert.That(VisitorMessageWords.Of("DE-ch"), Is.SameAs(german));
            Assert.That(german, Is.Not.SameAs(VisitorMessageWords.Of("en")));
            Assert.That(VisitorMessageWords.Of("fr-FR"), Is.SameAs(VisitorMessageWords.Of("en")));
            Assert.That(VisitorMessageWords.Of(null), Is.SameAs(VisitorMessageWords.Of("en")));
        });
    }
}
