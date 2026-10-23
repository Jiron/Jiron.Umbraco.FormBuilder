using Jiron.Umbraco.FormBuilder.Testing.People;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>The generated test passwords: new each time, and accepted by any Umbraco password policy.</summary>
[TestFixture]
public sealed class TestPeoplePasswordTests
{
    [Test]
    public void NewPassword_Always_HasEveryCharacterClassAndOnlySafeCharacters()
    {
        var password = TestPeople.NewPassword();

        Assert.Multiple(() =>
        {
            Assert.That(password, Has.Length.GreaterThanOrEqualTo(32));
            Assert.That(password, Does.Match("[A-Z]"));
            Assert.That(password, Does.Match("[a-z]"));
            Assert.That(password, Does.Match("[0-9]"));
            Assert.That(password, Does.Match("[^A-Za-z0-9]"));
            Assert.That(password, Does.Match("^[A-Za-z0-9!]+$"));
        });
    }

    [Test]
    public void NewPassword_TwoCalls_AreDifferent()
    {
        Assert.That(TestPeople.NewPassword(), Is.Not.EqualTo(TestPeople.NewPassword()));
    }
}
