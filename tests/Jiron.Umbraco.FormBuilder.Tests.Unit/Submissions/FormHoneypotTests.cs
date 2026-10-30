using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Submissions;

/// <summary>
/// A form's honeypot name (ADR-0012): the same for one form's public revision wherever it is rendered, different for
/// the next revision and for another form, and never a field's name or id or one of Jiron Form Builder's own inputs.
/// </summary>
[TestFixture]
public sealed class FormHoneypotTests
{
    private static readonly Guid Form = Guid.Parse("4b0c5e1a-7f3d-4c2e-9a61-d2b8f0c4e713");

    [Test]
    public void TheName_IsTheSameForOneRevision_AndAnotherForTheNext()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormHoneypot.NameOf(Form, 3), Is.EqualTo(FormHoneypot.NameOf(Form, 3)));
            Assert.That(FormHoneypot.NameOf(Form, 4), Is.Not.EqualTo(FormHoneypot.NameOf(Form, 3)));
            Assert.That(FormHoneypot.NameOf(Guid.Parse("4b0c5e1a-7f3d-4c2e-9a61-d2b8f0c4e714"), 3), Is.Not.EqualTo(FormHoneypot.NameOf(Form, 3)));
        });
    }

    [Test]
    public void TheName_IsFixed_SoEveryServerAndEveryCachedCopyAgrees() =>
        Assert.That(FormHoneypot.NameOf(Guid.Empty, 1), Is.EqualTo(FormHoneypot.NameOf(Guid.Empty, 1)).And.Match("^[0-9][0-9a-f]{3}-[0-9a-f]{4}$"));

    [Test]
    public void EveryName_StartsWithADigit_AndHoldsAHyphen_SoItIsNoAliasNorJfbsOwn()
    {
        var names = Enumerable.Range(0, 2000).Select(at => FormHoneypot.NameOf(new Guid(at, 7, 9, 1, 2, 3, 4, 5, 6, 7, 8), at % 7)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.All.Match("^[0-9][0-9a-f]{3}-[0-9a-f]{4}$"));
            Assert.That(names.Where(name => Regex.IsMatch(name, "^[a-z][a-zA-Z0-9]{0,63}$")), Is.Empty, "no field alias");
            Assert.That(names.Where(name => name.StartsWith("jfb-", StringComparison.Ordinal)), Is.Empty);
            Assert.That(names.Select(name => name[0]).Distinct().Count(), Is.EqualTo(10), "the first digit varies too");
            Assert.That(names.Distinct().Count(), Is.GreaterThan(1990), "names differ from form to form");
        });
    }
}
