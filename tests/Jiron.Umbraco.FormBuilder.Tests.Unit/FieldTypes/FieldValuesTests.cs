using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The contract of reading and checking answers (ADR-0058) as the pipeline runs it (<see cref="FieldValues"/>): through
/// a site's own type written against the interface alone, one with settings written on the base class, a built-in
/// type, and types that break the contract, which the pipeline refuses.
/// </summary>
public sealed class FieldValuesTests
{
    private const FieldMessageKinds CodeMessages = FieldMessageKinds.Required | FieldMessageKinds.MinLength | FieldMessageKinds.MaxLength;

    private static readonly DateOnly Today = new(2026, 10, 26);

    [Test]
    public void APlainType_TakesOneTextOfTheDefaultLength()
    {
        IFormBuilderFieldType type = new PlainFieldType(Declare("plain", FieldStorageKind.Text, FieldKind.Text));
        var field = Field("plain");

        Assert.Multiple(() =>
        {
            Assert.That(type.AnswerLimits(field), Is.EqualTo(new FieldAnswerLimits(FieldAnswerLimits.DefaultTextLength, 1)));
            Assert.That(Values(FieldValues.Read(type, field, new FieldAnswers([" Same as posted "]))), Is.EqualTo(new[] { " Same as posted " }));
            Assert.That(FieldValues.Read(type, field, new FieldAnswers(["a", "b"])).IsUnreadable, Is.True, "two values of a type that takes one");
            Assert.That(FieldValues.Read(type, field, new FieldAnswers(["a"], [new("part", ["b"])])).Values, Has.Length.EqualTo(1), "parts are left to the types that have them");
            Assert.That(FieldValues.Check(type, field, FieldValues.Read(type, field, new FieldAnswers(["a"])), required: true, Today), Is.Empty, "nothing to check by default");
        });
    }

    [Test]
    public void APlainTypeOfChoices_TakesAsManyAsAFieldHasOptions()
    {
        IFormBuilderFieldType type = new PlainFieldType(Declare("tags", FieldStorageKind.Text, FieldKind.Choices));
        var field = Field("tags");

        Assert.Multiple(() =>
        {
            Assert.That(type.AnswerLimits(field), Is.EqualTo(new FieldAnswerLimits(200, 10000)));
            Assert.That(Values(FieldValues.Read(type, field, new FieldAnswers(["b", "a"]))), Is.EqualTo(new[] { "b", "a" }));
            Assert.That(FieldValues.Read(type, field, new FieldAnswers(["a", "a"])).IsUnreadable, Is.True, "a value given twice");
        });
    }

    [TestCase("""{"multiple":true}""", 10000)]
    [TestCase("""{"multiple":false}""", 1)]
    [TestCase(null, 1)]
    public void APlainTypeWithAMultipleSetting_TakesSeveral_WhenTheFieldTurnsItOn(string? settings, int most)
    {
        IFormBuilderFieldType type = new PlainFieldType(Declare("pick", FieldStorageKind.Text, FieldKind.Choice) with { MultipleSetting = true });
        var field = Field("pick", settings);

        Assert.Multiple(() =>
        {
            Assert.That(type.AnswerLimits(field).MostTexts, Is.EqualTo(most));
            Assert.That(FieldValues.Read(type, field, new FieldAnswers(["a", "b"])).IsUnreadable, Is.EqualTo(most == 1));
        });
    }

    [Test]
    public void APlainTypeThatHoldsNoValue_TakesNoAnswer()
    {
        IFormBuilderFieldType type = new PlainFieldType(Declare("note", FieldStorageKind.None, null) with { HoldsValue = false });
        var field = Field("note");
        var answers = new FieldAnswers(["posted anyway"]);

        Assert.Multiple(() =>
        {
            Assert.That(type.AnswerLimits(field), Is.SameAs(FieldAnswerLimits.None));
            Assert.That(FieldValues.CheckLimits(type, field, answers), Is.Null, "the pipeline ignores what is posted for it");
            Assert.That(FieldValues.Read(type, field, answers).Values, Is.Empty);
            Assert.That(FieldValues.Read(type, field, answers).IsUnreadable, Is.False);
        });
    }

    [Test]
    public void ATypeWithSettings_TakesReadsAndChecks_WithTheFieldsSettings()
    {
        var type = new CodeFieldType();
        var field = Field("code", """{"capitals":true,"length":4}""");

        var read = FieldValues.Read(type, field, new FieldAnswers(["abcd"]));
        Assert.Multiple(() =>
        {
            Assert.That(type.AnswerLimits(field), Is.EqualTo(new FieldAnswerLimits(4, 1)));
            Assert.That(read.Values.Single().Text, Is.EqualTo("ABCD"), "what rules compare, in capitals as the settings say");
            Assert.That(read.Values.Single().StoredText, Is.EqualTo("abcd"), "what the entry stores");
            Assert.That(FieldValues.Check(type, field, read, required: true, Today), Is.Empty);
            Assert.That(FieldValues.Check(type, field, FieldValues.Read(type, field, new FieldAnswers(["abc"])), required: false, Today), Is.EqualTo(new[] { FieldMessageKinds.MinLength }));
            Assert.That(FieldValues.Read(type, field, new FieldAnswers(["ab1d"])).IsUnreadable, Is.True);
        });
    }

    [Test]
    public void CheckLimits_ATextTooLong_FailsWithMaxLength_WhenTheTypeDeclaresIt()
    {
        var field = Field("code", """{"length":4}""");
        var number = TestFieldTypes.Registry.FindType("number")!;

        Assert.Multiple(() =>
        {
            Assert.That(FieldValues.CheckLimits(new CodeFieldType(), field, new FieldAnswers(["abcd"])), Is.Null, "at the limit");
            Assert.That(FieldValues.CheckLimits(new CodeFieldType(), field, new FieldAnswers(["abcde"])), Is.EqualTo(FieldMessageKinds.MaxLength));
            Assert.That(FieldValues.CheckLimits(number, Field("number"), new FieldAnswers([new string('1', 65)])), Is.EqualTo(FieldMessageKinds.Invalid), "a type without maxLength");
        });
    }

    [Test]
    public void CheckLimits_TooManyTexts_IsInvalid() =>
        Assert.That(FieldValues.CheckLimits(new CodeFieldType(), Field("code"), new FieldAnswers(["a", "b"])), Is.EqualTo(FieldMessageKinds.Invalid));

    [Test]
    public void Check_FindsAnUnreadableAnswerFirst_ThenAMissingRequiredValue_ThenTheTypesOwnErrors()
    {
        var type = new CountingFieldType();
        var field = Field("counting");
        var unreadable = FieldValues.Read(type, field, new FieldAnswers(["unreadable"]));
        var empty = FieldValues.Read(type, field, FieldAnswers.None);
        var value = FieldValues.Read(type, field, new FieldAnswers(["value"]));

        Assert.Multiple(() =>
        {
            Assert.That(FieldValues.Check(type, field, unreadable, required: true, Today), Is.EqualTo(new[] { FieldMessageKinds.Invalid }));
            Assert.That(FieldValues.Check(type, field, empty, required: true, Today), Is.EqualTo(new[] { FieldMessageKinds.Required }));
            Assert.That(FieldValues.Check(type, field, empty, required: false, Today), Is.Empty);
            Assert.That(type.Validated, Is.Zero, "the type checks values only");
            Assert.That(FieldValues.Check(type, field, value, required: true, Today), Is.EqualTo(new[] { FieldMessageKinds.Invalid, FieldMessageKinds.Required }));
            Assert.That(type.Validated, Is.EqualTo(1));
        });
    }

    [Test]
    public void ABuiltInType_ReadsAndChecks_ThroughTheSameContract()
    {
        var number = TestFieldTypes.Registry.FindType("number")!;
        var field = Field("number", """{"decimals":2}""");

        Assert.Multiple(() =>
        {
            Assert.That(Values(FieldValues.Read(number, field, new FieldAnswers(["1.50e1"]))), Is.EqualTo(new[] { "15" }), "converted exactly to the wire form");
            Assert.That(FieldValues.Check(number, field, FieldValues.Read(number, field, new FieldAnswers(["-0.25"])), required: true, Today), Is.Empty);
            Assert.That(FieldValues.Check(number, field, FieldValues.Read(number, field, new FieldAnswers(["1,5"])), required: false, Today), Is.EqualTo(new[] { FieldMessageKinds.Invalid }));
            Assert.That(FieldValues.Check(number, field, FieldValues.Read(number, field, new FieldAnswers(["1234567890123456"])), required: false, Today), Is.EqualTo(new[] { FieldMessageKinds.Invalid }), "past 15 digits");
            Assert.That(FieldValues.Check(number, field, FieldValues.Read(number, field, new FieldAnswers(["1", "2"])), required: false, Today), Is.EqualTo(new[] { FieldMessageKinds.Invalid }));
            Assert.That(FieldValues.Check(number, field, FieldValues.Read(number, field, FieldAnswers.None), required: true, Today), Is.EqualTo(new[] { FieldMessageKinds.Required }));
        });
    }

    [Test]
    public void ATypeWithoutLimits_IsRefused_ByName()
    {
        var type = new BrokenFieldType("limitless", FieldStorageKind.Text) { HasLimits = false };

        Assert.Multiple(() =>
        {
            Assert.That(() => FieldValues.CheckLimits(type, Field("limitless"), FieldAnswers.None), Throws.InvalidOperationException.With.Message.Contains("'limitless'"));
            Assert.That(() => FieldValues.Read(type, Field("limitless"), FieldAnswers.None), Throws.InvalidOperationException.With.Message.Contains("'limitless'"));
        });
    }

    [Test]
    public void ATypeThatFailsWithAnUndeclaredKind_IsRefused()
    {
        var type = new BrokenFieldType("loose", FieldStorageKind.Text)
        {
            OnRead = reading => reading.Add("a"),
            OnValidate = validation => validation.Fail(FieldMessageKinds.Max),
        };
        var field = Field("loose");

        Assert.That(() => FieldValues.Check(type, field, FieldValues.Read(type, field, new FieldAnswers(["a"])), required: false, Today), Throws.ArgumentException);
    }

    [TestCase(FieldStorageKind.Number, "1e3")]
    [TestCase(FieldStorageKind.Number, "007")]
    [TestCase(FieldStorageKind.Date, "2026-02-30")]
    [TestCase(FieldStorageKind.Date, "2026-10-25 08:30")]
    [TestCase(FieldStorageKind.Bool, "yes")]
    [TestCase(FieldStorageKind.Json, "[\"a\"]")]
    [TestCase(FieldStorageKind.Json, "{\"a\":1}")]
    [TestCase(FieldStorageKind.Json, "{}")]
    [TestCase(FieldStorageKind.None, "a")]
    public void ATypeThatReadsAValueItsStorageCannotHold_IsRefused_WithoutTheAnswer(FieldStorageKind storage, string text)
    {
        var type = new BrokenFieldType("careless", storage) { OnRead = reading => reading.Add("secret-" + text) };
        var stored = new BrokenFieldType("careless", storage) { OnRead = reading => reading.Add(new FieldValue("1") { StoredText = text }) };

        Assert.Multiple(() =>
        {
            Assert.That(() => FieldValues.Read(type, Field("careless"), new FieldAnswers(["a"])), Throws.InvalidOperationException
                .With.Message.Contains("'careless'").And.Message.Not.Contains("secret"));
            Assert.That(() => FieldValues.Read(stored, Field("careless"), new FieldAnswers(["a"])), Throws.InvalidOperationException, "the stored text is what is held");
        });
    }

    [TestCase(FieldStorageKind.Number, "-12.5")]
    [TestCase(FieldStorageKind.Date, "2026-10-25")]
    [TestCase(FieldStorageKind.Date, "2026-10-25T08:30")]
    [TestCase(FieldStorageKind.Bool, "true")]
    [TestCase(FieldStorageKind.Bool, "false")]
    [TestCase(FieldStorageKind.Json, "{\"first\":\"Ada\"}")]
    [TestCase(FieldStorageKind.Text, "anything")]
    [TestCase(FieldStorageKind.LongText, "anything")]
    [TestCase(FieldStorageKind.File, "an-upload-token")]
    public void ATypeThatReadsAValueItsStorageHolds_IsTaken(FieldStorageKind storage, string text)
    {
        var type = new BrokenFieldType("careful", storage) { OnRead = reading => reading.Add(text) };

        Assert.That(Values(FieldValues.Read(type, Field("careful"), new FieldAnswers(["a"]))), Is.EqualTo(new[] { text }));
    }

    [Test]
    public void ATypeWhosePartsHoldItsValues_ReadsValuesOfItsRows()
    {
        var row = Guid.NewGuid();
        var type = new BrokenFieldType("rows", FieldStorageKind.None) { OnRead = reading => reading.Add("good", row) };
        var field = Field("rows") with { Rows = [new GridRow { Key = row }] };

        Assert.That(FieldValues.Read(type, field, new FieldAnswers(["a"])).Values.Single().Row, Is.EqualTo(row));
    }

    [Test]
    public void TheSteps_RefuseNull()
    {
        var type = new CodeFieldType();
        var field = Field("code");
        var read = FieldValues.Read(type, field, FieldAnswers.None);

        Assert.Multiple(() =>
        {
            Assert.That(() => FieldValues.CheckLimits(null!, field, FieldAnswers.None), Throws.ArgumentNullException);
            Assert.That(() => FieldValues.CheckLimits(type, null!, FieldAnswers.None), Throws.ArgumentNullException);
            Assert.That(() => FieldValues.CheckLimits(type, field, null!), Throws.ArgumentNullException);
            Assert.That(() => FieldValues.Read(type, field, null!), Throws.ArgumentNullException);
            Assert.That(() => FieldValues.Check(null!, field, read, required: false, Today), Throws.ArgumentNullException);
            Assert.That(() => FieldValues.Check(type, null!, read, required: false, Today), Throws.ArgumentNullException);
            Assert.That(() => FieldValues.Check(type, field, null!, required: false, Today), Throws.ArgumentNullException);
            Assert.That(() => type.AnswerLimits(null!), Throws.ArgumentNullException);
            Assert.That(() => type.Read(null!), Throws.ArgumentNullException);
            Assert.That(() => type.Validate(null!), Throws.ArgumentNullException);
        });
    }

    private static string[] Values(FieldRead read) => [.. read.Values.Select(value => value.Text)];

    private static FormField Field(string type, string? settings = null) =>
        new() { Key = Guid.NewGuid(), Type = type, Alias = type, Settings = settings is null ? null : SettingsObject.Parse(settings) };

    private static FieldTypeDeclaration Declare(string alias, FieldStorageKind storage, FieldKind? kind, FieldMessageKinds messages = FieldMessageKinds.Required) => new()
    {
        Alias = alias,
        Name = alias,
        Icon = "icon-settings",
        PaletteGroup = FieldPaletteGroup.Text,
        HoldsValue = true,
        Storage = storage,
        RuleKind = kind,
        Features = FieldFeatures.Label | FieldFeatures.Required,
        Messages = messages,
    };

    // A type written against the interface alone: every step is the interface's default.
    private sealed class PlainFieldType(FieldTypeDeclaration declaration) : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = declaration;

        public FieldSettingsSchema SettingsSchema { get; } = FieldSettingsReader.Read(typeof(MultipleSettings));
    }

    public sealed class MultipleSettings
    {
        [FormBuilderSetting("Multiple")]
        public bool Multiple { get; init; }
    }

    // A code of letters as long as its settings say, compared in capitals when they say so, and stored as typed.
    private sealed class CodeFieldType() : FormBuilderFieldTypeBase<CodeSettings>(Declare("code", FieldStorageKind.Text, FieldKind.Text, CodeMessages))
    {
        protected override FieldAnswerLimits AnswerLimits(CodeSettings settings, FormField field) => new(settings.Length, 1);

        protected override void Read(CodeSettings settings, FieldReading reading)
        {
            if (reading.Answers.Values is [var code] && code.All(char.IsAsciiLetter))
            {
                reading.Add(new FieldValue(settings.Capitals ? code.ToUpperInvariant() : code) { StoredText = code });
            }
            else if (!reading.Answers.IsEmpty)
            {
                reading.Invalid();
            }
        }

        protected override void Validate(CodeSettings settings, FieldValidation validation)
        {
            if (validation.Values[0].Text.Length < settings.Length)
            {
                validation.Fail(FieldMessageKinds.MinLength);
            }
        }
    }

    public sealed class CodeSettings
    {
        [Range(1, 10)]
        [FormBuilderSetting("Length")]
        public int Length { get; init; } = 3;

        [FormBuilderSetting("Capitals")]
        public bool Capitals { get; init; }
    }

    // Reads "unreadable" as unreadable and anything else as it is; fails every value with invalid and required, so the
    // order of its errors and whether it ran show.
    private sealed class CountingFieldType : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = Declare("counting", FieldStorageKind.Text, FieldKind.Text);

        public FieldSettingsSchema SettingsSchema { get; } = FieldSettingsReader.Read(typeof(NoFieldSettings));

        public int Validated { get; private set; }

        public void Read(FieldReading reading)
        {
            foreach (var text in reading.Answers.Values)
            {
                if (text == "unreadable")
                {
                    reading.Invalid();
                }
                else
                {
                    reading.Add(text);
                }
            }
        }

        public void Validate(FieldValidation validation)
        {
            Validated++;
            validation.Fail(FieldMessageKinds.Invalid);
            validation.Fail(FieldMessageKinds.Required);
        }
    }

    // A type whose steps a test sets, to break the contract one way at a time.
    private sealed class BrokenFieldType(string alias, FieldStorageKind storage) : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = Declare(alias, storage, FieldKind.Text);

        public FieldSettingsSchema SettingsSchema { get; } = FieldSettingsReader.Read(typeof(NoFieldSettings));

        public bool HasLimits { get; init; } = true;

        public Action<FieldReading>? OnRead { get; init; }

        public Action<FieldValidation>? OnValidate { get; init; }

        public FieldAnswerLimits AnswerLimits(FormField field) => HasLimits ? new(100, 2) : null!;

        public void Read(FieldReading reading) => OnRead?.Invoke(reading);

        public void Validate(FieldValidation validation) => OnValidate?.Invoke(validation);
    }
}
