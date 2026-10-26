using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// What a field type receives and gives back (ADR-0058): a field's answers, the longest answer a type takes, a value, a
/// reading and a validation, each keeping what the contract allows and refusing the rest.
/// </summary>
public sealed class FieldAnswersTests
{
    private static readonly Guid RowKey = Guid.NewGuid();

    private static readonly FormField Grid = new() { Key = Guid.NewGuid(), Type = "grid", Rows = [new GridRow { Key = RowKey }] };

    [Test]
    public void Answers_KeepTheTexts_AndThePartsByName()
    {
        var answers = new FieldAnswers(["b", "a"], [Part("street", "Main Street 1"), Part("city", "Bern", "Thun"), Part("region")]);

        Assert.Multiple(() =>
        {
            Assert.That(answers.Values, Is.EqualTo(new[] { "b", "a" }), "in the order given");
            Assert.That(answers.Parts.Keys, Is.EqualTo(new[] { "city", "street" }), "a part without texts is left out; the names sort ordinally");
            Assert.That(answers.Part("city"), Is.EqualTo(new[] { "Bern", "Thun" }));
            Assert.That(answers.Part("region"), Is.Empty);
            Assert.That(answers.Part("country"), Is.Empty);
            Assert.That(answers.Count, Is.EqualTo(5), "values and parts' texts together");
            Assert.That(answers.LongestText, Is.EqualTo("Main Street 1".Length));
            Assert.That(answers.IsEmpty, Is.False);
        });
    }

    [Test]
    public void Answers_WithoutAnyText_AreEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FieldAnswers.None.IsEmpty, Is.True);
            Assert.That(FieldAnswers.None.Count, Is.Zero);
            Assert.That(FieldAnswers.None.LongestText, Is.Zero);
            Assert.That(new FieldAnswers([], [Part("first")]).IsEmpty, Is.True, "parts without texts count for nothing");
            Assert.That(new FieldAnswers([]).Parts, Is.Empty);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public void Answers_RefuseATextThatIsNullOrEmpty(string? text)
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new FieldAnswers(["a", text!]), Throws.ArgumentException);
            Assert.That(() => new FieldAnswers([], [Part("first", "a", text!)]), Throws.ArgumentException);
        });
    }

    [TestCase("")]
    [TestCase(" ")]
    public void Answers_RefuseAPartWithoutAName(string name) =>
        Assert.That(() => new FieldAnswers([], [Part(name, "a")]), Throws.ArgumentException);

    [Test]
    public void Answers_RefuseAPartGivenTwice()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new FieldAnswers([], [Part("first", "a"), Part("first", "b")]), Throws.ArgumentException);
            Assert.That(() => new FieldAnswers([], [Part("first"), Part("first", "b")]), Throws.ArgumentException, "also when one of them is empty");
        });
    }

    [Test]
    public void Answers_RefuseNull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new FieldAnswers(null!), Throws.ArgumentNullException);
            Assert.That(() => new FieldAnswers([], [new KeyValuePair<string, IEnumerable<string>>("first", null!)]), Throws.ArgumentNullException);
            Assert.That(() => FieldAnswers.None.Part(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Limits_HoldTheLongestTextAndTheMostTexts_AtTheirEdges()
    {
        var limits = new FieldAnswerLimits(3, 2);

        Assert.Multiple(() =>
        {
            Assert.That(limits.TakesAnswers, Is.True);
            Assert.That(limits.Allows(new FieldAnswers(["abc", "de"])), Is.True, "at both limits");
            Assert.That(limits.Allows(new FieldAnswers(["abcd"])), Is.False, "a text one too long");
            Assert.That(limits.Allows(new FieldAnswers(["a", "b", "c"])), Is.False, "one text too many");
            Assert.That(limits.Allows(new FieldAnswers(["a"], [Part("other", "b", "c")])), Is.False, "parts count with the values");
            Assert.That(limits.Allows(FieldAnswers.None), Is.True);
        });
    }

    [Test]
    public void Limits_None_TakeNoAnswer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FieldAnswerLimits.None.TakesAnswers, Is.False);
            Assert.That(FieldAnswerLimits.None.Allows(FieldAnswers.None), Is.True);
            Assert.That(FieldAnswerLimits.None.Allows(new FieldAnswers(["a"])), Is.False);
        });
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(-1, 1)]
    public void Limits_RefuseLessThanOne(int longestText, int mostTexts) =>
        Assert.That(() => new FieldAnswerLimits(longestText, mostTexts), Throws.InstanceOf<ArgumentOutOfRangeException>());

    [Test]
    public void Limits_ByDefault_AreThoseOfThePlainTextsAndTheOptions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FieldAnswerLimits.DefaultTextLength, Is.EqualTo(2000));
            Assert.That(FieldAnswerLimits.DefaultManyTexts, Is.EqualTo(10000));
        });
    }

    [Test]
    public void Value_KeepsItsText_ItsRow_AndTheTextItStores()
    {
        var value = new FieldValue("true") { StoredText = "hash" };
        var row = new FieldValue("good", RowKey);

        Assert.Multiple(() =>
        {
            Assert.That(value.Text, Is.EqualTo("true"));
            Assert.That(value.Row, Is.Null);
            Assert.That(value.StoredText, Is.EqualTo("hash"));
            Assert.That(row.Row, Is.EqualTo(RowKey));
            Assert.That(row.StoredText, Is.Null);
        });
    }

    [Test]
    public void Value_RefusesAnEmptyText_AnEmptyRow_AndAnEmptyStoredText()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new FieldValue(string.Empty), Throws.ArgumentException);
            Assert.That(() => new FieldValue(null!), Throws.ArgumentNullException);
            Assert.That(() => new FieldValue("a", Guid.Empty), Throws.ArgumentException);
            Assert.That(() => new FieldValue("a") { StoredText = string.Empty }, Throws.ArgumentException);
        });
    }

    [Test]
    public void Reading_KeepsTheValues_OfTheFieldsRows()
    {
        var reading = new FieldReading(Grid, FieldAnswers.None);
        reading.Add("first");
        reading.Add("good", RowKey);
        reading.Add(new FieldValue("true") { StoredText = "hash" });

        Assert.Multiple(() =>
        {
            Assert.That(reading.IsInvalid, Is.False);
            Assert.That(reading.Values.Select(value => (value.Text, value.Row, value.StoredText)), Is.EqualTo(new[]
            {
                ("first", (Guid?)null, (string?)null),
                ("good", RowKey, null),
                ("true", null, "hash"),
            }));
        });
    }

    [Test]
    public void Reading_RefusesARowTheFieldDoesNotHave()
    {
        var reading = new FieldReading(Grid, FieldAnswers.None);

        Assert.Multiple(() =>
        {
            Assert.That(() => reading.Add("good", Guid.NewGuid()), Throws.ArgumentException);
            Assert.That(() => reading.Add(new FieldValue("good", Guid.NewGuid())), Throws.ArgumentException);
            Assert.That(() => reading.Add(string.Empty), Throws.ArgumentException);
            Assert.That(() => reading.Add((FieldValue)null!), Throws.ArgumentNullException);
            Assert.That(reading.Values, Is.Empty);
        });
    }

    [Test]
    public void Reading_ThatIsInvalid_HasNoValues()
    {
        var reading = new FieldReading(Grid, FieldAnswers.None);
        reading.Add("first");
        reading.Invalid();
        reading.Add("second");

        Assert.Multiple(() =>
        {
            Assert.That(reading.IsInvalid, Is.True);
            Assert.That(reading.Values, Is.Empty, "the values read before and after count for nothing");
        });
    }

    [Test]
    public void Validation_KeepsEachErrorOnce_InTheOrderFound()
    {
        var validation = Validation(FieldMessageKinds.Required | FieldMessageKinds.MinLength | FieldMessageKinds.Max);
        validation.Fail(FieldMessageKinds.Max);
        validation.Fail(FieldMessageKinds.MinLength);
        validation.Fail(FieldMessageKinds.Max);

        Assert.That(validation.Errors, Is.EqualTo(new[] { FieldMessageKinds.Max, FieldMessageKinds.MinLength }));
    }

    [Test]
    public void Validation_TakesInvalid_FromAnyType() =>
        Assert.That(() => Validation(FieldMessageKinds.None).Fail(FieldMessageKinds.Invalid), Throws.Nothing);

    [Test]
    public void Validation_RefusesAnErrorKindTheTypeDoesNotDeclare()
    {
        var validation = Validation(FieldMessageKinds.Required | FieldMessageKinds.MinLength);

        Assert.Multiple(() =>
        {
            Assert.That(() => validation.Fail(FieldMessageKinds.MaxLength), Throws.ArgumentException);
            Assert.That(validation.Errors, Is.Empty);
        });
    }

    [TestCase(FieldMessageKinds.None)]
    [TestCase(FieldMessageKinds.MinLength | FieldMessageKinds.MaxLength)]
    [TestCase((FieldMessageKinds)(1 << 30))]
    public void Validation_RefusesWhatIsNotExactlyOneErrorKind(FieldMessageKinds kind) =>
        Assert.That(() => Validation(FieldMessageKinds.MinLength | FieldMessageKinds.MaxLength | (FieldMessageKinds)(1 << 30)).Fail(kind), Throws.ArgumentException);

    private static FieldValidation Validation(FieldMessageKinds declared) =>
        new(Grid, new FieldAnswers(["a"]), [new FieldValue("a")], declared, isRequired: false, new DateOnly(2026, 10, 26));

    private static KeyValuePair<string, IEnumerable<string>> Part(string name, params string[] texts) => new(name, texts.ToImmutableArray());
}
