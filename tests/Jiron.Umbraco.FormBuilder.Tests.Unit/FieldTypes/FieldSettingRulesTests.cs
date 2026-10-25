using System.ComponentModel.DataAnnotations;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

// NUnit has a [Range] of its own.
using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The rules of a setting, read from .NET's validation attributes on its property (ADR-0056): <c>[Required]</c>,
/// <c>[Range]</c>, <c>[StringLength]</c>, <c>[Length]</c> and <c>[AllowedValues]</c>, with the limits a setting without
/// them keeps. An attribute that does not fit its setting, any other validation attribute, and a default that breaks the
/// rules are refused with the class and the property named.
/// </summary>
public sealed class FieldSettingRulesTests
{
    public static IEnumerable<TestCaseData> Refusals()
    {
        static TestCaseData Case(string name, Type type, string says) => new TestCaseData(type, says).SetName($"Refuses({name})");

        yield return Case("[Required] that allows empty strings", typeof(RequiredEmpty), "RequiredEmpty.Title has [Required] with AllowEmptyStrings; a required text is never blank.");
        yield return Case("[Required] on a text with a default", typeof(RequiredWithDefault), "RequiredWithDefault.Title has [Required] and starts with a value; [Required] asks each field for a value of its own");
        yield return Case("[Required] on a whole number", typeof(RequiredNumber), "RequiredNumber.Count has [Required] and starts with a value");
        yield return Case("[Range] on a text", typeof(RangeOnText), "RangeOnText.Title has [Range], which bounds whole numbers and numbers, not a text.");
        yield return Case("[Range] with a fraction for a whole number", typeof(FractionForWhole), "FractionForWhole.Count has [Range] with a bound that is not a whole number of at most 15 digits.");
        yield return Case("[Range] with 16 digits", typeof(SixteenDigits), "SixteenDigits.Bytes has [Range] with a bound that is not a whole number of at most 15 digits.");
        yield return Case("[Range] with dates", typeof(DateRange), "DateRange.Count has [Range] with a bound that is not a whole number of at most 15 digits.");
        yield return Case("[Range] beyond an int", typeof(BeyondInt), "BeyondInt.Count has [Range] beyond what an int holds, -2147483648 to 2147483647.");
        yield return Case("[Range] upside down", typeof(UpsideDown), "UpsideDown.Count has [Range] from 5 to 1, which no value keeps.");
        yield return Case("[Range] of one value, without it", typeof(EmptyRange), "EmptyRange.Count has [Range] from 1 to 1, which no value keeps.");
        yield return Case("[StringLength] on a whole number", typeof(LengthOnNumber), "LengthOnNumber.Count has [StringLength], which measures texts, not a whole number.");
        yield return Case("[StringLength] on a number stored as text", typeof(LengthOnDecimal), "LengthOnDecimal.Ratio has [StringLength], which measures texts, not a number.");
        yield return Case("[StringLength] that no text keeps", typeof(LengthUpsideDown), "LengthUpsideDown.Title has [StringLength] of 5 to 2 characters, which no text keeps.");
        yield return Case("[Length] on a text", typeof(CountOnText), "CountOnText.Title has [Length], which counts the items of a list; a text's length is [StringLength].");
        yield return Case("[Length] that no list keeps", typeof(CountUpsideDown), "CountUpsideDown.Tags has [Length] of 3 to 1 items, which no list keeps.");
        yield return Case("[Length] with a minimum and an empty start", typeof(CountWithoutStart), "CountWithoutStart.Tags has at least 2 items and starts with none; give it [Required], or a default of 2 items.");
        yield return Case("[AllowedValues] on a whole number", typeof(AllowedNumbers), "AllowedNumbers.Count has [AllowedValues], which lists the texts a text may be; a setting of a few choices is an enum.");
        yield return Case("[AllowedValues] with a number", typeof(AllowedMixed), "AllowedMixed.Side has [AllowedValues] with no values, or with one that is not a text.");
        yield return Case("[AllowedValues] longer than [StringLength]", typeof(AllowedTooLong), "AllowedTooLong.Side allows 'sideways', which is longer or shorter than its [StringLength] allows.");
        yield return Case("[MinLength]", typeof(WithMinLength), "WithMinLength.Tags has [MinLength], which settings do not read: their rules are [Required], [Range], [StringLength], [Length] and [AllowedValues].");
        yield return Case("[RegularExpression]", typeof(WithPattern), "WithPattern.Title has [RegularExpression], which settings do not read");
        yield return Case("an attribute derived from [Required]", typeof(WithDerived), "WithDerived.Title has [StrictRequired], which settings do not read");
        yield return Case("a default below its range", typeof(DefaultBelow), "DefaultBelow.Count starts as 0, which breaks its rules: The setting 'count' is 1 to 10, not 0.");
        yield return Case("a default list too long", typeof(DefaultTooMany), "DefaultTooMany.Tags starts as [\"a\",\"b\"], which breaks its rules: The setting 'tags' has at most 1 item, not 2.");
        yield return Case("a default list with a value twice", typeof(DefaultTwice), "DefaultTwice.Tags starts as [\"a\",\"a\"], which breaks its rules: The setting 'tags' holds each value once; \"a\" is there before.");
        yield return Case("a default text too long", typeof(DefaultTooLong), "DefaultTooLong.Title starts as \"abcd\", which breaks its rules: The setting 'title' has at most 3 characters, not 4.");
        yield return Case("a default object that lacks a required setting", typeof(StartsWithoutRequired), "StartsWithoutRequired.Box starts as {\"size\":1}, which breaks its rules: The setting 'label' needs a value.");
    }

    [Test]
    public void EachAttribute_GivesItsRule()
    {
        var schema = FieldSettingsReader.Read(typeof(Ruled));
        FieldSettingRules RulesOf(string name) => schema.Find(name)!.Rules;

        Assert.Multiple(() =>
        {
            Assert.That(RulesOf("title"), Is.EqualTo(new FieldSettingRules { Required = true, MaximumLength = FieldSettingRules.TextLength }).Using<FieldSettingRules>(Same));
            Assert.That(RulesOf("count"), Is.EqualTo(new FieldSettingRules { Minimum = "1", Maximum = "10" }).Using<FieldSettingRules>(Same));
            Assert.That(RulesOf("ratio"), Is.EqualTo(new FieldSettingRules { Minimum = "0.5", MinimumIsExclusive = true, Maximum = "2.5" }).Using<FieldSettingRules>(Same), "a double's bounds in the wire form");
            Assert.That(RulesOf("share"), Is.EqualTo(new FieldSettingRules { Minimum = "-1.25", Maximum = "1.25", MaximumIsExclusive = true }).Using<FieldSettingRules>(Same), "bounds given as text");
            Assert.That(RulesOf("bytes"), Is.EqualTo(new FieldSettingRules { Minimum = "1", Maximum = "999999999999999" }).Using<FieldSettingRules>(Same), "a long's bounds as text");
            Assert.That(RulesOf("code"), Is.EqualTo(new FieldSettingRules { MinimumLength = 2, MaximumLength = 20 }).Using<FieldSettingRules>(Same));
            Assert.That(RulesOf("tags"), Is.EqualTo(new FieldSettingRules { MinimumCount = 1, MaximumCount = 3, MaximumLength = 5 }).Using<FieldSettingRules>(Same), "a list's count, and its items' length");
            Assert.That(RulesOf("side"), Is.EqualTo(new FieldSettingRules { AllowedValues = ["left", "right"], MaximumLength = FieldSettingRules.TextLength }).Using<FieldSettingRules>(Same));
            Assert.That(RulesOf("sizes"), Is.EqualTo(new FieldSettingRules { Minimum = "1", Maximum = "5", MaximumCount = FieldSettingRules.ListCount }).Using<FieldSettingRules>(Same), "an item's bounds");
            Assert.That(RulesOf("prompt"), Is.EqualTo(new FieldSettingRules { MaximumLength = 10 }).Using<FieldSettingRules>(Same), "a length in each culture");
            Assert.That(schema.Find("box")!.Settings.Single(setting => setting.Name == "label").Rules.Required, Is.True, "inside an object");
        });
    }

    [Test]
    public void ASettingWithoutAttributes_KeepsTheLimitsOfItsKind()
    {
        var schema = FieldSettingsReader.Read(typeof(Unruled));
        FieldSettingRules RulesOf(string name) => schema.Find(name)!.Rules;

        Assert.Multiple(() =>
        {
            Assert.That(RulesOf("on"), Is.EqualTo(FieldSettingRules.None).Using<FieldSettingRules>(Same));
            Assert.That(RulesOf("count"), Is.EqualTo(new FieldSettingRules { Minimum = "-2147483648", Maximum = "2147483647" }).Using<FieldSettingRules>(Same), "what an int holds");
            Assert.That(RulesOf("maybe"), Is.EqualTo(new FieldSettingRules { Minimum = "-2147483648", Maximum = "2147483647" }).Using<FieldSettingRules>(Same), "a nullable int too");
            Assert.That(RulesOf("big"), Is.EqualTo(FieldSettingRules.None).Using<FieldSettingRules>(Same), "a long holds every number settings store");
            Assert.That(RulesOf("title"), Is.EqualTo(new FieldSettingRules { MaximumLength = FieldSettingRules.TextLength }).Using<FieldSettingRules>(Same));
            Assert.That(RulesOf("label"), Is.EqualTo(new FieldSettingRules { MaximumLength = FieldSettingRules.TextLength }).Using<FieldSettingRules>(Same));
            Assert.That(RulesOf("body"), Is.EqualTo(new FieldSettingRules { MaximumLength = FieldSettingRules.HtmlLength }).Using<FieldSettingRules>(Same), "rich text");
            Assert.That(RulesOf("ratio"), Is.EqualTo(FieldSettingRules.None).Using<FieldSettingRules>(Same), "a number as text is no text");
            Assert.That(RulesOf("tags"), Is.EqualTo(new FieldSettingRules { MaximumCount = FieldSettingRules.ListCount, MaximumLength = FieldSettingRules.TextLength }).Using<FieldSettingRules>(Same));
            Assert.That(RulesOf("counts"), Is.EqualTo(new FieldSettingRules { Minimum = "-2147483648", Maximum = "2147483647", MaximumCount = FieldSettingRules.ListCount }).Using<FieldSettingRules>(Same));
        });
    }

    [TestCaseSource(nameof(Refusals))]
    public void AnAttributeThatDoesNotFit_IsRefused_NamingThePropertyAndTheReason(Type type, string says)
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => FieldSettingsReader.Read(type));

        Assert.That(refusal!.Message, Does.Contain(says));
    }

    [Test]
    public void EveryReason_IsGivenAtOnce()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() => FieldSettingsReader.Read(typeof(TwoReasons)));

        Assert.Multiple(() =>
        {
            Assert.That(refusal!.Message, Does.Contain("TwoReasons.Title has [Range]"));
            Assert.That(refusal.Message, Does.Contain("TwoReasons.Count has [StringLength]"));
        });
    }

    // The record's own equality compares the allowed values by reference.
    private static bool Same(FieldSettingRules a, FieldSettingRules b) =>
        a with { AllowedValues = [] } == b with { AllowedValues = [] } && a.AllowedValues.SequenceEqual(b.AllowedValues);

    public sealed class Ruled
    {
        [Required]
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }

        [Range(1, 10)]
        [FormBuilderSetting("Count")]
        public int Count { get; init; } = 5;

        [Range(0.5, 2.5, MinimumIsExclusive = true)]
        [FormBuilderSetting("Ratio", Kind = FieldSettingKind.Number)]
        public string Ratio { get; init; } = "1";

        [Range(typeof(decimal), "-1.250", "1.25", MaximumIsExclusive = true)]
        [FormBuilderSetting("Share", Kind = FieldSettingKind.Number)]
        public string? Share { get; init; }

        [Range(typeof(long), "1", "999999999999999")]
        [FormBuilderSetting("Bytes")]
        public long? Bytes { get; init; }

        [StringLength(20, MinimumLength = 2)]
        [FormBuilderSetting("Code")]
        public string Code { get; init; } = "ab";

        [Length(1, 3)]
        [StringLength(5)]
        [FormBuilderSetting("Tags")]
        public IReadOnlyList<string> Tags { get; init; } = ["a"];

        [AllowedValues("left", "right")]
        [FormBuilderSetting("Side")]
        public string Side { get; init; } = "left";

        [Range(1, 5)]
        [FormBuilderSetting("Sizes")]
        public IReadOnlyList<int> Sizes { get; init; } = [];

        [StringLength(10)]
        [FormBuilderSetting("Prompt")]
        public LocalizedText? Prompt { get; init; }

        [FormBuilderSetting("Box")]
        public LabelBox? Box { get; init; }
    }

    public sealed class LabelBox
    {
        [Required]
        [FormBuilderSetting("Label")]
        public string? Label { get; init; }

        [FormBuilderSetting("Size")]
        public int? Size { get; init; }
    }

    public sealed class Unruled
    {
        [FormBuilderSetting("On")]
        public bool On { get; init; }

        [FormBuilderSetting("Count")]
        public int Count { get; init; }

        [FormBuilderSetting("Maybe")]
        public int? Maybe { get; init; }

        [FormBuilderSetting("Big")]
        public long Big { get; init; }

        [FormBuilderSetting("Title")]
        public string? Title { get; init; }

        [FormBuilderSetting("Label")]
        public LocalizedText? Label { get; init; }

        [FormBuilderSetting("Body")]
        public LocalizedHtml? Body { get; init; }

        [FormBuilderSetting("Ratio", Kind = FieldSettingKind.Number)]
        public string? Ratio { get; init; }

        [FormBuilderSetting("Tags")]
        public IReadOnlyList<string> Tags { get; init; } = [];

        [FormBuilderSetting("Counts")]
        public IReadOnlyList<int> Counts { get; init; } = [];
    }

    public sealed class RequiredEmpty
    {
        [Required(AllowEmptyStrings = true)]
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }
    }

    public sealed class RequiredWithDefault
    {
        [Required]
        [FormBuilderSetting("Title")]
        public string Title { get; init; } = "Hello";
    }

    public sealed class RequiredNumber
    {
        [Required]
        [FormBuilderSetting("Count")]
        public int Count { get; init; }
    }

    public sealed class RangeOnText
    {
        [Range(1, 2)]
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }
    }

    public sealed class FractionForWhole
    {
        [Range(0.5, 2)]
        [FormBuilderSetting("Count")]
        public int Count { get; init; } = 1;
    }

    public sealed class SixteenDigits
    {
        [Range(typeof(long), "1", "1000000000000000")]
        [FormBuilderSetting("Bytes")]
        public long? Bytes { get; init; }
    }

    public sealed class DateRange
    {
        [Range(typeof(DateTime), "2020-01-01", "2030-01-01")]
        [FormBuilderSetting("Count")]
        public int? Count { get; init; }
    }

    public sealed class BeyondInt
    {
        [Range(typeof(long), "0", "3000000000")]
        [FormBuilderSetting("Count")]
        public int Count { get; init; }
    }

    public sealed class UpsideDown
    {
        [Range(5, 1)]
        [FormBuilderSetting("Count")]
        public int? Count { get; init; }
    }

    public sealed class EmptyRange
    {
        [Range(1, 1, MaximumIsExclusive = true)]
        [FormBuilderSetting("Count")]
        public int? Count { get; init; }
    }

    public sealed class LengthOnNumber
    {
        [StringLength(5)]
        [FormBuilderSetting("Count")]
        public int Count { get; init; }
    }

    public sealed class LengthOnDecimal
    {
        [StringLength(5)]
        [FormBuilderSetting("Ratio", Kind = FieldSettingKind.Number)]
        public string? Ratio { get; init; }
    }

    public sealed class LengthUpsideDown
    {
        [StringLength(2, MinimumLength = 5)]
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }
    }

    public sealed class CountOnText
    {
        [Length(1, 2)]
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }
    }

    public sealed class CountUpsideDown
    {
        [Length(3, 1)]
        [FormBuilderSetting("Tags")]
        public IReadOnlyList<string> Tags { get; init; } = [];
    }

    public sealed class CountWithoutStart
    {
        [Length(2, 5)]
        [FormBuilderSetting("Tags")]
        public IReadOnlyList<string> Tags { get; init; } = [];
    }

    public sealed class AllowedNumbers
    {
        [AllowedValues(1, 2)]
        [FormBuilderSetting("Count")]
        public int Count { get; init; } = 1;
    }

    public sealed class AllowedMixed
    {
        [AllowedValues("left", 2)]
        [FormBuilderSetting("Side")]
        public string? Side { get; init; }
    }

    public sealed class AllowedTooLong
    {
        [AllowedValues("up", "sideways")]
        [StringLength(4)]
        [FormBuilderSetting("Side")]
        public string? Side { get; init; }
    }

    public sealed class WithMinLength
    {
        [MinLength(1)]
        [FormBuilderSetting("Tags")]
        public IReadOnlyList<string> Tags { get; init; } = [];
    }

    public sealed class WithPattern
    {
        [RegularExpression("[a-z]+")]
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }
    }

    public sealed class WithDerived
    {
        [StrictRequired]
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class StrictRequiredAttribute : RequiredAttribute;

    public sealed class DefaultBelow
    {
        [Range(1, 10)]
        [FormBuilderSetting("Count")]
        public int Count { get; init; }
    }

    public sealed class DefaultTooMany
    {
        [Length(0, 1)]
        [FormBuilderSetting("Tags")]
        public IReadOnlyList<string> Tags { get; init; } = ["a", "b"];
    }

    public sealed class DefaultTwice
    {
        [FormBuilderSetting("Tags")]
        public IReadOnlyList<string> Tags { get; init; } = ["a", "a"];
    }

    public sealed class DefaultTooLong
    {
        [StringLength(3)]
        [FormBuilderSetting("Title")]
        public string Title { get; init; } = "abcd";
    }

    public sealed class StartsWithoutRequired
    {
        [FormBuilderSetting("Box")]
        public LabelBox Box { get; init; } = new() { Size = 1 };
    }

    public sealed class TwoReasons
    {
        [Range(1, 2)]
        [FormBuilderSetting("Title")]
        public string? Title { get; init; }

        [StringLength(3)]
        [FormBuilderSetting("Count")]
        public int Count { get; init; }
    }
}
