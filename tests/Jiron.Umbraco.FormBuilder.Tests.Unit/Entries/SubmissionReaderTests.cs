using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// A submission's answers over its whole form (ADR-0072), as <see cref="SubmissionReader"/> reads them for the pipeline:
/// a repeatable group's rows counted and held to its limits, a child field's error in its row, the calculations the
/// server works out whatever was posted, a hidden field's member value resolved on submit and absent from the public
/// projection, a query prefill, what the rules hide neither checked nor stored, files, form-level validation rules, and
/// the actions whose rules look at a resolved value.
/// </summary>
public sealed class SubmissionReaderTests
{
    private static readonly Guid Name = Key(1);
    private static readonly Guid Kind = Key(2);
    private static readonly Guid Company = Key(3);
    private static readonly Guid Price = Key(4);
    private static readonly Guid Quantity = Key(5);
    private static readonly Guid Total = Key(6);
    private static readonly Guid Attachment = Key(7);
    private static readonly Guid Guests = Key(8);
    private static readonly Guid GuestName = Key(9);
    private static readonly Guid Age = Key(10);
    private static readonly Guid Fare = Key(11);
    private static readonly Guid Pets = Key(12);
    private static readonly Guid PetName = Key(13);
    private static readonly Guid MemberEmail = Key(14);
    private static readonly Guid Campaign = Key(15);
    private static readonly Guid Contact = Key(16);
    private static readonly Guid NameAgain = Key(17);
    private static readonly Guid GuestNameAgain = Key(18);
    private static readonly Guid TooMany = Key(20);
    private static readonly Guid Notify = Key(30);
    private static readonly Guid Shipping = Key(31);
    private static readonly Guid MemberKey = Key(40);

    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    // A contact form with a field of each kind the step treats on its own: a name typed twice, a field the rules show only
    // for business, two numbers and their calculated total, a file, a repeatable group of guests (a name each, typed
    // twice, and a calculation per row), one of pets the rules show only for private customers, a hidden field whose value
    // is the member's email address, one prefilled from the query, and a name in parts; an action that runs only with a
    // member's address, one for business, and a validation rule on the quantity.
    private static FormDefinition Form { get; } = new()
    {
        Key = Key(100),
        Pages =
        [
            new FormPage
            {
                Key = Key(101),
                Groups =
                [
                    new FormGroup
                    {
                        Key = Key(102),
                        Fields =
                        [
                            Field(Name, "text", "name") with { Required = true },
                            Field(NameAgain, "text", "nameAgain") with { Validation = [new MatchesFieldCheck { Field = Name }] },
                            Field(Kind, "radio", "kind") with { Options = new FieldOptions { Items = [new FieldOption { Value = "private" }, new FieldOption { Value = "business" }] } },
                            Field(Company, "text", "company") with { Rules = [Rule(RuleEffect.Show, Kind, "equals", "business")] },
                            Field(Price, "number", "price"),
                            Field(Quantity, "number", "quantity"),
                            Field(Total, "calculation", "total", """{"formula":"price * quantity","format":{"decimals":2}}"""),
                            Field(Attachment, "file", "attachment", """{"maxFiles":2}"""),
                            Field(Guests, "repeater", "guests", """{"minRows":1,"maxRows":3}""") with
                            {
                                Fields =
                                [
                                    Field(GuestName, "text", "guestName") with { Required = true },
                                    Field(GuestNameAgain, "text", "guestNameAgain") with { Validation = [new MatchesFieldCheck { Field = GuestName }] },
                                    Field(Age, "number", "age", """{"min":"0","max":"120"}"""),
                                    Field(Fare, "calculation", "fare", """{"formula":"age * 2"}"""),
                                ],
                            },
                            Field(Pets, "repeater", "pets", """{"maxRows":3}""") with { Rules = [Rule(RuleEffect.Show, Kind, "equals", "private")], Fields = [Field(PetName, "text", "petName")] },
                            Field(MemberEmail, "hidden", "memberEmail") with { DefaultValue = FieldDefaultValue.Of("{{ member.email }}") },
                            Field(Campaign, "hidden", "campaign") with { DefaultValue = FieldDefaultValue.Of("web"), Prefill = new FieldPrefill { Query = "utm" } },
                            Field(Contact, "name", "contact"),
                        ],
                    },
                ],
            },
        ],
        ValidationRules = [new FormValidationRule { Key = TooMany, Field = Quantity, Conditions = [Condition(Quantity, "greaterThan", "10")] }],
        Actions =
        [
            new FormAction { Key = Notify, Type = "email", Rules = [Rule(RuleEffect.Run, MemberEmail, "isNotEmpty")] },
            new FormAction { Key = Shipping, Type = "email", Rules = [Rule(RuleEffect.Run, Kind, "equals", "business")] },
        ],
        Settings = new FormSettings { Developer = new FormDeveloperSettings { QueryValues = ["utm"] } },
    };

    [Test]
    public async Task AValidSubmission_StoresEveryShownAnswer_WithTheServersCalculations()
    {
        var read = await ReadAsync(Valid());

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty);
            Assert.That(read.IsValid, Is.True);
            Assert.That(Stored(read), Is.EqualTo(new[]
            {
                "name: Ada", "kind: private", "price: 12", "quantity: 2", "total: 24", "guestName@0: Bob", "age@0: 30", "fare@0: 60", "campaign: web",
            }));
        });
    }

    [Test]
    public async Task ACalculationsPostedValue_IsReplacedByTheServers()
    {
        var post = Valid() with { Fields = Valid().Fields.With(Total, "999").With(Quantity, "3") };
        post = post with { Rows = Rows(Guests, Row((GuestName, "Bob"), (Age, "30"), (Fare, "1"))) };

        var read = await ReadAsync(post);

        Assert.Multiple(() =>
        {
            Assert.That(Value(read, Total), Is.EqualTo("36"), "price times quantity, never the posted 999");
            Assert.That(Value(read, Fare), Is.EqualTo("60"), "a row's calculation, never the posted 1");
            Assert.That(read.Errors, Is.Empty);
        });
    }

    [TestCase(0, "rowCount")]
    [TestCase(1)]
    [TestCase(3)]
    [TestCase(4, "rowCount")]
    public async Task ARepeatableGroupsRows_AreCountedAndHeldToItsLimits(int rows, params string[] errors)
    {
        var read = await ReadAsync(Valid() with { Rows = Rows(Guests, [.. Enumerable.Repeat(Row((GuestName, "Bob")), rows)]) });

        Assert.That(Errors(read), Is.EqualTo(errors.Select(error => $"guests: {error}")));
    }

    [Test]
    public async Task TooManyRows_AreNeitherReadNorChecked()
    {
        var tooLong = new string('x', 5000);
        var read = await ReadAsync(Valid() with { Rows = Rows(Guests, [.. Enumerable.Repeat(Row((GuestName, tooLong), (Age, "200")), 1000)]) });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "guests: rowCount" }), "the count alone, no row's errors");
            Assert.That(read.Answers.Where(answer => answer.Field == GuestName), Is.Empty);
        });
    }

    [Test]
    public async Task AChildFieldsError_IsInItsRow()
    {
        var read = await ReadAsync(Valid() with { Rows = Rows(Guests, Row((GuestName, "Bob"), (Age, "30")), Row((GuestName, "Carol"), (Age, "130")), Row((Age, "abc"))) });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "age@1: max", "guestName@2: required", "age@2: invalid" }));
            Assert.That(read.Errors.Select(error => error.Row), Is.EqualTo(new int?[] { 1, 2, 2 }), "the row as the visitor saw it");
        });
    }

    [Test]
    public async Task AChildsMatchesFieldCheck_ComparesWithItsOwnRow()
    {
        var read = await ReadAsync(Valid() with
        {
            Fields = Valid().Fields.With(NameAgain, "Ada"),
            Rows = Rows(Guests, Row((GuestName, "Bob"), (GuestNameAgain, "Bob")), Row((GuestName, "Carol"), (GuestNameAgain, "Bob"))),
        });
        var mistyped = await ReadAsync(Valid() with { Fields = Valid().Fields.With(NameAgain, "Ida") });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "guestNameAgain@1: check 0" }), "Carol's row, never Bob's");
            Assert.That(Errors(mistyped), Is.EqualTo(new[] { "nameAgain: check 0" }));
        });
    }

    [Test]
    public async Task AGroupTheRulesHide_IsNeitherCountedNorStored()
    {
        var pets = Rows(Pets, Row((PetName, "Rex")), Row((PetName, "Tom")), Row((PetName, "Max")), Row((PetName, "Kit")));
        var business = await ReadAsync(Valid() with { Fields = Valid().Fields.With(Kind, "business"), Rows = pets.Concat(Valid().Rows).ToDictionary() });
        var tooMany = await ReadAsync(Valid() with { Rows = pets.Concat(Valid().Rows).ToDictionary() });

        Assert.Multiple(() =>
        {
            Assert.That(business.Errors, Is.Empty, "four rows over three, but the group is hidden");
            Assert.That(business.Answers.Where(answer => answer.Field == PetName), Is.Empty);
            Assert.That(Errors(tooMany), Is.EqualTo(new[] { "pets: rowCount" }));
        });
    }

    [Test]
    public async Task AnEmptyRow_CountsAsARow_AndTheStoredRowsCloseUp()
    {
        var read = await ReadAsync(Valid() with { Rows = Rows(Pets, Row((PetName, "Rex")), Row(), Row((PetName, "Tom"))).Concat(Rows(Guests, Row((GuestName, "Bob")))).ToDictionary() });

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty);
            Assert.That(Stored(read).Where(text => text.StartsWith("petName", StringComparison.Ordinal)), Is.EqualTo(new[] { "petName@0: Rex", "petName@1: Tom" }));
        });
    }

    [Test]
    public async Task AHiddenFieldsMemberValue_IsResolvedOnSubmit_WhateverWasPosted()
    {
        var umbraco = new TestUmbraco();
        umbraco.Member(MemberKey, "Ada Lovelace", "ada@example.com");
        var post = Valid() with { Fields = Valid().Fields.With(MemberEmail, "forged@example.com") };

        var signedIn = await ReadAsync(post with { Scope = post.Scope with { Member = MemberKey } }, umbraco);
        var anonymous = await ReadAsync(post, umbraco);

        Assert.Multiple(() =>
        {
            Assert.That(Value(signedIn, MemberEmail), Is.EqualTo("ada@example.com"));
            Assert.That(signedIn.State.Actions, Is.EqualTo(new[] { DefinitionLogic.Id(Notify) }), "the action's rule sees the resolved value");
            Assert.That(Value(anonymous, MemberEmail), Is.Null, "no member, no value, and never the posted one");
            Assert.That(anonymous.State.Actions, Is.Empty);
            Assert.That(signedIn.ForMergeTags.Find(MemberEmail)?.Values.Single().Text, Is.EqualTo("ada@example.com"), "the actions' merge tags show it");
        });
    }

    [Test]
    public void AHiddenFieldsMemberValue_NeverAppearsInThePublicProjection()
    {
        var projection = Encoding.UTF8.GetString(PublicProjection.Write(PublicProjection.Create(Form, TestFieldTypes.Registry)));

        Assert.Multiple(() =>
        {
            Assert.That(projection, Does.Contain("\"memberEmail\""), "the field is there");
            Assert.That(projection, Does.Not.Contain("member.email"), "its value's source is not");
            Assert.That(projection, Does.Not.Contain("\"web\""), "nor any hidden field's default");
        });
    }

    [Test]
    public async Task AHiddenFieldPrefilledFromTheQuery_TakesThePostedValue_OrItsDefault()
    {
        var posted = await ReadAsync(Valid() with { Fields = Valid().Fields.With(Campaign, "  newsletter ") });
        var none = await ReadAsync(Valid());

        Assert.Multiple(() =>
        {
            Assert.That(Value(posted, Campaign), Is.EqualTo("newsletter"), "cleaned as every answer is");
            Assert.That(Value(none, Campaign), Is.EqualTo("web"));
        });
    }

    [Test]
    public async Task WhatTheRulesHide_IsNeitherCheckedNorStored()
    {
        var hidden = await ReadAsync(Valid() with { Fields = Valid().Fields.With(Company, new string('x', 5000)) });
        var shown = await ReadAsync(Valid() with { Fields = Valid().Fields.With(Kind, "business").With(Company, new string('x', 5000)) });

        Assert.Multiple(() =>
        {
            Assert.That(hidden.Errors, Is.Empty);
            Assert.That(Value(hidden, Company), Is.Null);
            Assert.That(hidden.ForMergeTags.Hidden, Does.Contain(Company));
            Assert.That(Errors(shown), Is.EqualTo(new[] { "company: maxLength" }), "over its type's limit, once shown");
            Assert.That(shown.State.Actions, Is.EqualTo(new[] { DefinitionLogic.Id(Shipping) }));
        });
    }

    [Test]
    public async Task AShownFieldsErrors_AreItsTypes_ItsRequirement_AndTheValidationRules()
    {
        var read = await ReadAsync(Valid() with { Fields = Valid().Fields.Without(Name).With(Price, "abc").With(Quantity, "11") });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "name: required", "price: invalid", $"quantity: rule {TooMany}" }));
            Assert.That(read.IsValid, Is.False);
        });
    }

    [Test]
    public async Task AFileField_StoresTheUploadsItsTokensStandFor_AndRefusesAnyOther()
    {
        var mine = Upload(1, Attachment);
        var other = Upload(2, Name);
        var uploads = new Dictionary<string, StoredEntryFile> { ["token-1"] = mine, ["token-2"] = other };

        var taken = await ReadAsync(Valid() with { Fields = Valid().Fields.With(Attachment, "token-1"), Uploads = uploads });
        var unknown = await ReadAsync(Valid() with { Fields = Valid().Fields.With(Attachment, "token-1", "token-9"), Uploads = uploads });
        var another = await ReadAsync(Valid() with { Fields = Valid().Fields.With(Attachment, "token-2"), Uploads = uploads });

        Assert.Multiple(() =>
        {
            Assert.That(taken.Errors, Is.Empty);
            Assert.That(taken.Answers.Single(answer => answer.Field == Attachment).Files, Is.EqualTo(new[] { mine }));
            Assert.That(taken.ForMergeTags.Find(Attachment)?.Files.Single().Name, Is.EqualTo("file1.pdf"));
            Assert.That(Errors(unknown), Is.EqualTo(new[] { "attachment: invalid" }));
            Assert.That(unknown.Answers.Where(answer => answer.Field == Attachment), Is.Empty);
            Assert.That(Errors(another), Is.EqualTo(new[] { "attachment: invalid" }), "an upload of another field");
        });
    }

    [Test]
    public async Task WhatTheVersionLacks_IsIgnored()
    {
        var read = await ReadAsync(Valid() with { Fields = Valid().Fields.With(Key(99), "anything").With(GuestName, "outside its group") });

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty);
            Assert.That(read.Answers.Select(answer => answer.Field), Does.Not.Contain(Key(99)));
            Assert.That(read.Answers.Count(answer => answer.Field == GuestName), Is.EqualTo(1), "only the row's");
        });
    }

    [Test]
    public async Task AFieldWhoseTypeTheSiteLacks_IsIgnored()
    {
        var group = Form.Pages[0].Groups[0];
        var form = Form with { Pages = [Form.Pages[0] with { Groups = [group with { Fields = [.. group.Fields, Field(Key(19), "jfb-test-gone", "gone") with { Required = true }] }] }] };

        var read = await ReadAsync(Valid() with { Scope = Valid().Scope with { Form = form }, Fields = Valid().Fields.With(Key(19), "anything") });

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty, "not even its requirement");
            Assert.That(Stored(read), Has.Length.EqualTo(9), "the other answers, as without it");
        });
    }

    [Test]
    public async Task AnAnswersParts_AreCleaned_AndAPartWithoutAName_LeftOut()
    {
        var parts = new Dictionary<string, IReadOnlyList<string?>> { ["first"] = ["  Ada "], ["last"] = [null, "Lovelace "], [" "] = ["x"] };
        var read = await ReadAsync(Valid() with { Fields = new Dictionary<Guid, PostedAnswer>(Valid().Fields) { [Contact] = new PostedAnswer([], parts) } });

        Assert.That(FieldPartsJson.Read(Value(read, Contact)!), Is.EqualTo(new Dictionary<string, string> { ["first"] = "Ada", ["last"] = "Lovelace" }));
    }

    [Test]
    public void AnUnknownTimeZone_IsRefused() =>
        Assert.That(async () => await ReadAsync(Valid() with { TimeZone = "Mars/Olympus" }), Throws.ArgumentException.With.Message.Contains("'Mars/Olympus'"));

    [Test]
    public async Task AResolvedValueLongerThanItsTypeTakes_IsLeftOut_AndLogged()
    {
        var umbraco = new TestUmbraco();
        umbraco.Member(MemberKey, "Ada Lovelace", new string('a', 2001) + "@example.com");
        var logger = new Recorder();

        var read = await ReadAsync(Valid() with { Scope = Valid().Scope with { Member = MemberKey } }, umbraco, logger);

        Assert.Multiple(() =>
        {
            Assert.That(Value(read, MemberEmail), Is.Null);
            Assert.That(logger.Messages, Has.One.Contains("'memberEmail'"), "the field by its alias");
            Assert.That(logger.Messages, Has.None.Contains("example.com"), "never the value");
        });
    }

    // A submission that holds: a private customer, two items at 12, one guest.
    private static PostedSubmission Valid() => new()
    {
        Scope = MergeTagTestSite.Scope(form: Form),
        Now = Now,
        TimeZone = "Europe/Zurich",
        Fields = new Dictionary<Guid, PostedAnswer> { [Name] = Answer("Ada"), [Kind] = Answer("private"), [Price] = Answer("12"), [Quantity] = Answer("2") },
        Rows = Rows(Guests, Row((GuestName, "Bob"), (Age, "30"))),
    };

    private static async Task<SubmissionAnswers> ReadAsync(PostedSubmission post, TestUmbraco? umbraco = null, ILogger<SubmissionReader>? logger = null)
    {
        var options = new OptionListTestSite().Options();
        var site = MergeTagTestSite.Create(umbraco: umbraco, options: options);
        var reader = new SubmissionReader(TestFieldTypes.Registry, site.Service, options, logger ?? new Recorder());
        return await reader.ReadAsync(post);
    }

    // Each stored value as "alias: text", a child's with its row.
    private static string[] Stored(SubmissionAnswers read) =>
        [.. read.Answers.SelectMany(answer => answer.Values.Select(value => $"{AliasOf(answer.Field)}{(IsChild(answer.Field) ? $"@{answer.Row}" : "")}: {value.Text}"))];

    private static string? Value(SubmissionAnswers read, Guid field) => read.Answers.SingleOrDefault(answer => answer.Field == field)?.Values.Single().Text;

    // Each error as "alias: kind", a child's with its row, a field's check by its index and a validation rule by its key.
    private static string[] Errors(SubmissionAnswers read) =>
        [.. read.Errors.Select(error => $"{AliasOf(error.Field)}{(error.Row is { } row ? $"@{row}" : "")}: {(error.Rule is { } rule ? $"rule {rule}" : error.Check is { } check ? $"check {check}" : ValidationCaseName(error.Kind))}")];

    private static string ValidationCaseName(FieldMessageKinds kind) => FieldMessageProperties.All.Single(property => property.Kind == kind).Name;

    private static string AliasOf(Guid key) =>
        Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)).Single(field => field.Key == key).Alias!;

    private static bool IsChild(Guid key) => Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields).Any(field => field.Key == key);

    private static PostedAnswer Answer(params string[] texts) => new(texts);

    private static Dictionary<Guid, PostedAnswer> Row(params (Guid Field, string Text)[] answers) => answers.ToDictionary(answer => answer.Field, answer => Answer(answer.Text));

    private static Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> Rows(Guid group, params IReadOnlyDictionary<Guid, PostedAnswer>[] rows) => new() { [group] = rows };

    private static StoredEntryFile Upload(int id, Guid field) =>
        new(id, Key(500 + id), null, 7, field, $"file{id}.pdf", $"{id:D32}", "application/pdf", 100L * id, new string('a', 64), "clean", DateTimeOffset.UnixEpoch);

    private static FormRule Rule(RuleEffect effect, Guid field, string op, string? value = null) => new()
    {
        Key = Guid.NewGuid(),
        Effect = effect,
        Conditions = [Condition(field, op, value)],
    };

    private static RuleCondition Condition(Guid field, string op, string? value) =>
        new() { Field = field, Operator = op, Value = value is null ? null : new ConditionOperand.Text(value) };

    private static FormField Field(Guid key, string type, string alias, string? settings = null) => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    private static Guid Key(int number) => Guid.Parse($"72000000-0000-4000-8000-{number:D12}");

    // A logger that keeps what is logged.
    private sealed class Recorder : ILogger<SubmissionReader>
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _messages.Add(formatter(state, exception));
    }
}

/// <summary>Changes to a submission's posted answers, for the tests.</summary>
internal static class PostedAnswersExtensions
{
    /// <summary>The answers with a field's texts given or replaced.</summary>
    public static IReadOnlyDictionary<Guid, PostedAnswer> With(this IReadOnlyDictionary<Guid, PostedAnswer> answers, Guid field, params string[] texts) =>
        new Dictionary<Guid, PostedAnswer>(answers) { [field] = new PostedAnswer(texts) };

    /// <summary>The answers without a field's.</summary>
    public static IReadOnlyDictionary<Guid, PostedAnswer> Without(this IReadOnlyDictionary<Guid, PostedAnswer> answers, Guid field) =>
        answers.Where(answer => answer.Key != field).ToDictionary();
}
