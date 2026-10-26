using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The contract of reading and checking answers (ADR-0058) as the pipeline runs it (<see cref="FieldValues"/>): through
/// a site's own type written against the interface alone, one with settings written on the base class, a built-in
/// type, and types that break the contract, which the pipeline refuses; and what a check sees besides the answers, and
/// the field's own checks after its type's (ADR-0062).
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
            Assert.That(FieldValues.Check(type, field, FieldValues.Read(type, field, new FieldAnswers(["a"])), new FieldCheckInput(true, Today)).Errors, Is.Empty, "nothing to check by default");
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
            Assert.That(FieldValues.Check(type, field, read, new FieldCheckInput(true, Today)).Errors, Is.Empty);
            Assert.That(FieldValues.Check(type, field, FieldValues.Read(type, field, new FieldAnswers(["abc"])), new FieldCheckInput(false, Today)).Errors, Is.EqualTo(new[] { FieldMessageKinds.MinLength }));
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
            Assert.That(FieldValues.Check(type, field, unreadable, new FieldCheckInput(true, Today)).Errors, Is.EqualTo(new[] { FieldMessageKinds.Invalid }));
            Assert.That(FieldValues.Check(type, field, empty, new FieldCheckInput(true, Today)).Errors, Is.EqualTo(new[] { FieldMessageKinds.Required }));
            Assert.That(FieldValues.Check(type, field, empty, new FieldCheckInput(false, Today)).Errors, Is.Empty);
            Assert.That(type.Validated, Is.Zero, "the type checks values only");
            Assert.That(FieldValues.Check(type, field, value, new FieldCheckInput(true, Today)).Errors, Is.EqualTo(new[] { FieldMessageKinds.Invalid, FieldMessageKinds.Required }));
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
            Assert.That(FieldValues.Check(number, field, FieldValues.Read(number, field, new FieldAnswers(["-0.25"])), new FieldCheckInput(true, Today)).Errors, Is.Empty);
            Assert.That(FieldValues.Check(number, field, FieldValues.Read(number, field, new FieldAnswers(["1,5"])), new FieldCheckInput(false, Today)).Errors, Is.EqualTo(new[] { FieldMessageKinds.Invalid }));
            Assert.That(FieldValues.Check(number, field, FieldValues.Read(number, field, new FieldAnswers(["1234567890123456"])), new FieldCheckInput(false, Today)).Errors, Is.EqualTo(new[] { FieldMessageKinds.Invalid }), "past 15 digits");
            Assert.That(FieldValues.Check(number, field, FieldValues.Read(number, field, new FieldAnswers(["1", "2"])), new FieldCheckInput(false, Today)).Errors, Is.EqualTo(new[] { FieldMessageKinds.Invalid }));
            Assert.That(FieldValues.Check(number, field, FieldValues.Read(number, field, FieldAnswers.None), new FieldCheckInput(true, Today)).Errors, Is.EqualTo(new[] { FieldMessageKinds.Required }));
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

        Assert.That(() => FieldValues.Check(type, field, FieldValues.Read(type, field, new FieldAnswers(["a"])), new FieldCheckInput(false, Today)), Throws.ArgumentException);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ATypesCheck_SeesWhetherTheFieldIsRequired_AsTheRulesLeaveIt_AndToday(bool required)
    {
        (bool Required, DateOnly Today)? seen = null;
        var type = new BrokenFieldType("seeing", FieldStorageKind.Text)
        {
            OnRead = reading => reading.Add("a"),
            OnValidate = validation => seen = (validation.IsRequired, validation.Today),
        };
        // The field's own setting says optional; a rule may still require it.
        var field = Field("seeing") with { Required = false };

        FieldValues.Check(type, field, FieldValues.Read(type, field, new FieldAnswers(["a"])), new FieldCheckInput(required, Today));

        Assert.That(seen, Is.EqualTo((required, Today)));
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
    public void ATypesCheck_SeesTheFilesItsUploadTokensStandFor()
    {
        FieldFile[]? seen = null;
        var type = new BrokenFieldType("uploads", FieldStorageKind.File)
        {
            OnRead = reading =>
            {
                foreach (var token in reading.Answers.Values)
                {
                    reading.Add(token);
                }
            },
            OnValidate = validation => seen = [.. validation.Files],
        };
        var field = Field("uploads");
        FieldFile[] files = [new("plan.pdf", 2048), new("photo.jpg", 1024)];

        FieldValues.Check(type, field, FieldValues.Read(type, field, new FieldAnswers(["token-1", "token-2"])), new FieldCheckInput(false, Today) { Files = [.. files] });

        Assert.That(seen, Is.EqualTo(files));
    }

    [Test]
    public void AFieldThatStoresFiles_HasOneFileForEachValue_AndNoOtherFieldHasFiles()
    {
        var uploads = new BrokenFieldType("uploads", FieldStorageKind.File) { OnRead = reading => reading.Add("token-1") };
        var plain = new BrokenFieldType("plain", FieldStorageKind.Text) { OnRead = reading => reading.Add("a") };
        var uploadsField = Field("uploads");
        var plainField = Field("plain");
        var token = FieldValues.Read(uploads, uploadsField, new FieldAnswers(["token-1"]));
        var text = FieldValues.Read(plain, plainField, new FieldAnswers(["a"]));
        var file = new FieldFile("plan.pdf", 2048);

        Assert.Multiple(() =>
        {
            Assert.That(() => FieldValues.Check(uploads, uploadsField, token, new FieldCheckInput(false, Today)), Throws.ArgumentException, "no file");
            Assert.That(() => FieldValues.Check(uploads, uploadsField, token, new FieldCheckInput(false, Today) { Files = [file, file] }), Throws.ArgumentException, "two files");
            Assert.That(() => FieldValues.Check(plain, plainField, text, new FieldCheckInput(false, Today) { Files = [file] }), Throws.ArgumentException, "a text's file");
            Assert.That(FieldValues.Check(uploads, uploadsField, token, new FieldCheckInput(false, Today) { Files = [file] }).Errors, Is.Empty);
            Assert.That(new FieldCheckInput(false, Today) { Files = default }.Files, Is.Empty, "no files, rather than a default array");
        });
    }

    [Test]
    public void AFile_HasAName_AndASizeThatIsNotNegative()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new FieldFile(null!, 1), Throws.ArgumentNullException);
            Assert.That(() => new FieldFile("", 1), Throws.ArgumentException);
            Assert.That(() => new FieldFile("plan.pdf", -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(new FieldFile("empty.txt", 0).Size, Is.Zero);
        });
    }

    [TestCase("8001", true)]
    [TestCase("80012", false)]
    [TestCase("CH-8001", false)]
    public void APatternCheck_HoldsTheWholeAnswer(string answer, bool holds) =>
        Assert.That(Text(answer, [new PatternCheck { Pattern = "[0-9]{4}" }]).Checks, Is.EqualTo(holds ? Array.Empty<int>() : [0]));

    [Test]
    public void APatternCheck_HoldsEveryValue()
    {
        var type = new BrokenFieldType("several", FieldStorageKind.Text)
        {
            OnRead = reading =>
            {
                foreach (var text in reading.Answers.Values)
                {
                    reading.Add(text);
                }
            },
        };
        var field = Field("several") with { Validation = [new PatternCheck { Pattern = "[a-z]+" }] };

        Assert.Multiple(() =>
        {
            Assert.That(Checked(type, field, ["abc", "def"]).Checks, Is.Empty);
            Assert.That(Checked(type, field, ["abc", "DEF"]).Checks, Is.EqualTo(new[] { 0 }));
        });
    }

    [Test]
    public void APatternOutsideTheSafeSubset_ChecksNothing() =>
        Assert.That(Text("b", [new PatternCheck { Pattern = new string('a', SafePattern.MaxPatternLength + 1) }]).Checks, Is.Empty);

    [Test]
    public void TheFieldsOwnChecks_RunAfterItsTypesChecks_AndReportTheirIndexes()
    {
        var errors = new ValidationCase("text", new FieldAnswers(["8001"]))
        {
            Settings = """{"maxLength":3}""",
            Checks = [new UniquePerFormCheck(), new PatternCheck { Pattern = "[a-z]+" }, new MatchesFieldCheck { Field = OtherField }],
            Others = new Dictionary<Guid, ImmutableArray<string>> { [OtherField] = ["8001"] },
        }.Run();

        Assert.Multiple(() =>
        {
            Assert.That(errors.Errors, Is.EqualTo(new[] { FieldMessageKinds.MaxLength }));
            Assert.That(errors.Checks, Is.EqualTo(new[] { 1 }), "uniquePerForm is the submission's, and the values match");
        });
    }

    [Test]
    public void TheFieldsOwnChecks_DoNotRun_WithoutAValue_OrForAnUnreadableAnswer()
    {
        var number = TestFieldTypes.Registry.FindType("number")!;
        var field = Field("number") with { Validation = [new MatchesFieldCheck { Field = OtherField }] };

        Assert.Multiple(() =>
        {
            Assert.That(Checked(number, field, []).Checks, Is.Empty, "no value");
            Assert.That(Checked(number, field, ["1,5"]).Errors, Is.EqualTo(new[] { FieldMessageKinds.Invalid }));
            Assert.That(Checked(number, field, ["1,5"]).Checks, Is.Empty, "an unreadable answer");
            Assert.That(Checked(number, field, ["15"]).Checks, Is.EqualTo(new[] { 0 }), "a value, and the other field has none");
        });
    }

    [TestCase("text", "Ada", "ADA", true)]
    [TestCase("text", "Ada", "Bob", false)]
    [TestCase("number", "15", "15.0", true)]
    [TestCase("number", "15", "16", false)]
    [TestCase("number", "15", "015", false)]
    [TestCase("date", "2026-06-15", "2026-06-15", true)]
    [TestCase("date", "2026-06-15", "2026-06-16", false)]
    [TestCase("time", "10:30", "10:30:00", true)]
    [TestCase("time", "10:30", "10:31", false)]
    public void AMatchesFieldCheck_ComparesAsTheFieldsKindCompares(string type, string answer, string other, bool holds)
    {
        var errors = new ValidationCase(type, new FieldAnswers([answer]))
        {
            Settings = type == "number" ? """{"decimals":0}""" : null,
            Checks = [new MatchesFieldCheck { Field = OtherField }],
            Others = new Dictionary<Guid, ImmutableArray<string>> { [OtherField] = [other] },
        }.Run();

        Assert.That(errors.Checks, Is.EqualTo(holds ? Array.Empty<int>() : [0]));
    }

    [Test]
    public void AMatchesFieldCheck_NeedsAsManyValues_AndFailsOnAFieldWithoutValues()
    {
        FieldErrors Against(params string[] others) => Text("Ada", [new MatchesFieldCheck { Field = OtherField }], others);

        Assert.Multiple(() =>
        {
            Assert.That(Against("Ada").Checks, Is.Empty);
            Assert.That(Against("Ada", "Ada").Checks, Is.EqualTo(new[] { 0 }), "two values for one");
            Assert.That(Text("Ada", [new MatchesFieldCheck { Field = OtherField }]).Checks, Is.EqualTo(new[] { 0 }), "a hidden field has none");
        });
    }

    [Test]
    public void AMatchesFieldCheck_OfATypeWithoutAKind_ComparesExactly()
    {
        var type = new BrokenFieldType("kindless", FieldStorageKind.Text, kind: null) { OnRead = reading => reading.Add(reading.Answers.Values[0]) };
        var field = Field("kindless") with { Validation = [new MatchesFieldCheck { Field = OtherField }] };

        Assert.Multiple(() =>
        {
            Assert.That(Checked(type, field, ["Ada"], "Ada").Checks, Is.Empty);
            Assert.That(Checked(type, field, ["Ada"], "ADA").Checks, Is.EqualTo(new[] { 0 }));
        });
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
            Assert.That(() => FieldValues.Check(null!, field, read, new FieldCheckInput(false, Today)), Throws.ArgumentNullException);
            Assert.That(() => FieldValues.Check(type, null!, read, new FieldCheckInput(false, Today)), Throws.ArgumentNullException);
            Assert.That(() => FieldValues.Check(type, field, null!, new FieldCheckInput(false, Today)), Throws.ArgumentNullException);
            Assert.That(() => FieldValues.Check(type, field, read, null!), Throws.ArgumentNullException);
            Assert.That(() => type.AnswerLimits(null!), Throws.ArgumentNullException);
            Assert.That(() => type.Read(null!), Throws.ArgumentNullException);
            Assert.That(() => type.Validate(null!), Throws.ArgumentNullException);
        });
    }

    private static readonly Guid OtherField = new("3e9d7a4b-af5a-4b1d-8c6e-8a4f5dab9e73");

    // A text field's answer checked with its own checks, and the other field's values when it has some.
    private static FieldErrors Text(string answer, FieldCheck[] checks, params string[] others) =>
        new ValidationCase("text", new FieldAnswers([answer]))
        {
            Checks = checks,
            Others = others.Length == 0 ? ImmutableDictionary<Guid, ImmutableArray<string>>.Empty : new Dictionary<Guid, ImmutableArray<string>> { [OtherField] = [.. others] },
        }.Run();

    // A field's answers read and checked, with the other field's values when it has some.
    private static FieldErrors Checked(IFormBuilderFieldType type, FormField field, string[] answers, params string[] others) =>
        FieldValues.Check(type, field, FieldValues.Read(type, field, new FieldAnswers(answers)), new FieldCheckInput(false, Today)
        {
            Values = others.Length == 0 ? ImmutableDictionary<Guid, ImmutableArray<string>>.Empty : new Dictionary<Guid, ImmutableArray<string>> { [OtherField] = [.. others] },
        });

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
    private sealed class BrokenFieldType(string alias, FieldStorageKind storage, FieldKind? kind = FieldKind.Text) : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = Declare(alias, storage, kind);

        public FieldSettingsSchema SettingsSchema { get; } = FieldSettingsReader.Read(typeof(NoFieldSettings));

        public bool HasLimits { get; init; } = true;

        public Action<FieldReading>? OnRead { get; init; }

        public Action<FieldValidation>? OnValidate { get; init; }

        public FieldAnswerLimits AnswerLimits(FormField field) => HasLimits ? new(100, 2) : null!;

        public void Read(FieldReading reading) => OnRead?.Invoke(reading);

        public void Validate(FieldValidation validation) => OnValidate?.Invoke(validation);
    }
}
