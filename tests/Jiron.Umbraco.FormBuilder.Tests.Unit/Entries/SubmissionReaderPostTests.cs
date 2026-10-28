using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// Posts that a client can send but the browser's runtime never does (ADR-0080), as <see cref="SubmissionReader"/> reads
/// them: one upload named twice, by one token or by two, an upload an entry holds or another field's, an answer that
/// fails after one of its tokens stood, a ranking's part names that are not clean or too long, a query value a field
/// that keeps its value on the server cannot take or read, and a checkbox's <c>false</c>.
/// </summary>
public sealed class SubmissionReaderPostTests
{
    private static readonly Guid Attachment = Key(1);
    private static readonly Guid Rooms = Key(2);
    private static readonly Guid Plan = Key(3);
    private static readonly Guid Colours = Key(4);
    private static readonly Guid Campaign = Key(5);
    private static readonly Guid Newsletter = Key(6);
    private static readonly Guid Wing = Key(7);
    private static readonly Guid Count = Key(8);

    // A file field, a repeatable group of rooms with up to two plans and a hidden wing prefilled from the query each, a
    // ranking whose list the site lacks (so its answers are taken without being held to options), a hidden field and a
    // site's number that keeps its value on the server, both prefilled from the query, and a checkbox.
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
                            Field(Attachment, "file", "attachment", """{"maxFiles":2}"""),
                            Field(Rooms, "repeater", "rooms", """{"maxRows":3}""") with
                            {
                                Fields =
                                [
                                    Field(Plan, "file", "plan", """{"maxFiles":2}"""),
                                    Field(Wing, "hidden", "wing") with { DefaultValue = FieldDefaultValue.Of("east"), Prefill = new FieldPrefill { Query = "wing" } },
                                ],
                            },
                            Field(Colours, "ranking", "colours") with { Options = new FieldOptions { Source = OptionSource.List, OptionList = Key(200) } },
                            Field(Campaign, "hidden", "campaign") with { DefaultValue = FieldDefaultValue.Of("web"), Prefill = new FieldPrefill { Query = "utm" } },
                            Field(Newsletter, "checkbox", "newsletter"),
                            Field(Count, "acme.serverNumber", "count") with { DefaultValue = FieldDefaultValue.Of("3"), Prefill = new FieldPrefill { Query = "count" } },
                        ],
                    },
                ],
            },
        ],
        Settings = new FormSettings { Developer = new FormDeveloperSettings { QueryValues = ["utm", "wing", "count"] } },
    };

    [Test]
    public async Task OneTokenInTwoRows_StandsForTheFirstRowsValue_AndTheLaterOneIsInvalid()
    {
        var plan = Upload(1, Plan);
        var read = await ReadAsync(Post() with
        {
            Rows = new Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> { [Rooms] = [Row(Plan, "token-1"), Row(Plan, "token-1")] },
            Uploads = new Dictionary<string, StoredEntryFile> { ["token-1"] = plan },
        });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "plan@1: invalid" }), "the later row, where the token was named again");
            Assert.That(read.Answers.Where(answer => answer.Field == Plan).Select(answer => (answer.Row, answer.Files.Single())), Is.EqualTo(new[] { (0, plan) }), "stored once, at the first row");
        });
    }

    [Test]
    public async Task OneTokenTwiceInOneAnswer_IsInvalid_AndStoresNothing()
    {
        var read = await ReadAsync(Post() with
        {
            Fields = new Dictionary<Guid, PostedAnswer> { [Attachment] = new(["token-2", "token-2"]) },
            Uploads = new Dictionary<string, StoredEntryFile> { ["token-2"] = Upload(2, Attachment) },
        });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "attachment: invalid" }));
            Assert.That(read.Answers.Where(answer => answer.Field == Attachment), Is.Empty);
        });
    }

    [Test]
    public async Task TwoTokensForOneUploadInTwoRows_StandForTheFirstRowsValue_AndTheLaterOneIsInvalid()
    {
        var plan = Upload(3, Plan);
        var read = await ReadAsync(Post() with
        {
            Rows = new Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> { [Rooms] = [Row(Plan, "token-a"), Row(Plan, "token-b")] },
            Uploads = new Dictionary<string, StoredEntryFile> { ["token-a"] = plan, ["token-b"] = plan },
        });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "plan@1: invalid" }), "the later row, whose token names the same upload");
            Assert.That(read.Answers.Where(answer => answer.Field == Plan).Select(answer => (answer.Row, answer.Files.Single())), Is.EqualTo(new[] { (0, plan) }), "stored once, at the first row");
        });
    }

    [Test]
    public async Task TwoTokensForOneUploadInOneAnswer_AreInvalid_AndStoreNothing()
    {
        var attachment = Upload(4, Attachment);
        var read = await ReadAsync(Post() with
        {
            Fields = new Dictionary<Guid, PostedAnswer> { [Attachment] = new(["token-a", "token-b"]) },
            Uploads = new Dictionary<string, StoredEntryFile> { ["token-a"] = attachment, ["token-b"] = attachment },
        });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "attachment: invalid" }));
            Assert.That(read.Answers.Where(answer => answer.Field == Attachment), Is.Empty);
        });
    }

    [Test]
    public async Task AnUploadAnEntryHolds_IsInvalid_AndStoresNothing()
    {
        var read = await ReadAsync(Post() with
        {
            Fields = new Dictionary<Guid, PostedAnswer> { [Attachment] = new(["token-5"]) },
            Uploads = new Dictionary<string, StoredEntryFile> { ["token-5"] = Upload(5, Attachment) with { EntryId = 41 } },
        });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "attachment: invalid" }), "an upload belongs to one entry");
            Assert.That(read.Answers.Where(answer => answer.Field == Attachment), Is.Empty);
        });
    }

    [Test]
    public async Task ATokenTwoFieldsName_IsInvalidAtTheFieldItsUploadIsNot_AndTheOtherKeepsIt()
    {
        var plan = Upload(6, Plan);
        var read = await ReadAsync(Post() with
        {
            Fields = new Dictionary<Guid, PostedAnswer> { [Attachment] = new(["token-6"]) },
            Rows = new Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> { [Rooms] = [Row(Plan, "token-6")] },
            Uploads = new Dictionary<string, StoredEntryFile> { ["token-6"] = plan },
        });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "attachment: invalid" }), "the earlier field names an upload of another, which it never takes");
            Assert.That(read.Answers.Single(answer => answer.Field == Plan).Files, Is.EqualTo(new[] { plan }), "so the plan's own value still has it");
        });
    }

    [Test]
    public async Task AnAnswerThatFailsAfterOneOfItsTokensStood_TakesNothing_SoALaterValueGetsTheUpload()
    {
        var plan = Upload(7, Plan);
        var read = await ReadAsync(Post() with
        {
            Rows = new Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>>
            {
                [Rooms] = [new Dictionary<Guid, PostedAnswer> { [Plan] = new(["token-7", "token-other"]) }, Row(Plan, "token-7")],
            },
            Uploads = new Dictionary<string, StoredEntryFile> { ["token-7"] = plan, ["token-other"] = Upload(8, Attachment) },
        });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "plan@0: invalid" }), "the first row's second token names another field's upload");
            Assert.That(read.Answers.Where(answer => answer.Field == Plan).Select(answer => (answer.Row, answer.Files.Single())), Is.EqualTo(new[] { (1, plan) }), "so its first token took nothing, and the second row has it");
        });
    }

    [Test]
    public async Task ARankingsPartNames_AreCleaned_EvenWhereItsOptionsAreUnknown()
    {
        var read = await ReadAsync(Ranking(("  red ", "1"), ("blue" + (char)0x2003, "3"), ("green", "2")));

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty, "the options are unknown, so the answers are taken as given");
            Assert.That(read.Answers.Single(answer => answer.Field == Colours).Values.Select(value => value.Text), Is.EqualTo(new[] { "red", "green", "blue" }), "by position, each name cleaned");
        });
    }

    [Test]
    public async Task ARankingsPartName_LongerThanAnOptionValue_IsRefusedBeforeItIsRead()
    {
        var read = await ReadAsync(Ranking((new string('r', DefinitionSaveChecks.OptionValueLength + 1), "1"), ("green", "2")));
        var longest = await ReadAsync(Ranking((new string('r', DefinitionSaveChecks.OptionValueLength), "1"), ("green", "2")));

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "colours: invalid" }));
            Assert.That(read.Answers.Where(answer => answer.Field == Colours), Is.Empty, "nothing of it stored");
            Assert.That(longest.Errors, Is.Empty, "an option value's length is taken");
        });
    }

    [Test]
    public async Task TwoRankingPartNames_ThatCleanToOne_AreOneOptionWithTwoPositions()
    {
        var read = await ReadAsync(Ranking(("red", "1"), (" red", "2"), ("green", "3")));

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "colours: invalid" }), "one option at two positions");
            Assert.That(read.Answers.Where(answer => answer.Field == Colours), Is.Empty);
        });
    }

    [Test]
    public async Task AQueryValueTooLongForItsHiddenField_CountsAsNone_AndItsDefaultStandsIn()
    {
        var logger = new RecordingLogger<SubmissionReader>();
        var tooLong = new string('u', FieldAnswerLimits.DefaultTextLength + 1);

        var read = await ReadAsync(Post() with { Fields = new Dictionary<Guid, PostedAnswer> { [Campaign] = new([tooLong]) } }, logger);

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty, "the visitor can neither see nor change the field");
            Assert.That(read.Answers.Single(answer => answer.Field == Campaign).Values.Single().Text, Is.EqualTo("web"));
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.One.Contains("'campaign'"), "the field by its alias");
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.None.Contains("uuu"), "never the value");
        });
    }

    [Test]
    public async Task TwoQueryValuesForAHiddenField_CountAsNone_AndItsDefaultStandsIn_LoggedAsAValueItsTypeCannotTake()
    {
        var logger = new RecordingLogger<SubmissionReader>();

        var read = await ReadAsync(Post() with { Fields = new Dictionary<Guid, PostedAnswer> { [Campaign] = new(["mail-secret", "print-secret"]) } }, logger);

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty, "the visitor can neither see nor change the field");
            Assert.That(read.Answers.Single(answer => answer.Field == Campaign).Values.Single().Text, Is.EqualTo("web"));
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.One.Contains("'campaign' a value its type cannot take"), "a limit other than the length");
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.None.Contains("secret"), "never the values");
        });
    }

    [Test]
    public async Task AQueryValueTooLongForAHiddenFieldInARow_CountsAsNone_AndItsDefaultStandsInThere()
    {
        var logger = new RecordingLogger<SubmissionReader>();
        var tooLong = new string('w', FieldAnswerLimits.DefaultTextLength + 1);
        static Dictionary<Guid, PostedAnswer> Room(string wing) => new() { [Wing] = new([wing]) };

        var read = await ReadAsync(Post() with { Rows = new Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> { [Rooms] = [Room("north"), Room(tooLong)] } }, logger);

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty, "the visitor can neither see nor change the field");
            Assert.That(read.Answers.Where(answer => answer.Field == Wing).Select(answer => (answer.Row, answer.Values.Single().Text)), Is.EqualTo(new[] { (0, "north"), (1, "east") }), "the query's value in the first row, the default in the second");
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.One.Contains("'wing'"), "the field by its alias, once");
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.None.Contains("www"), "never the value");
        });
    }

    [Test]
    public async Task AQueryValueASitesServerSideTypeCannotRead_CountsAsNone_AndItsDefaultStandsIn()
    {
        var logger = new RecordingLogger<SubmissionReader>();

        var read = await ReadAsync(Post() with { Fields = new Dictionary<Guid, PostedAnswer> { [Count] = new(["many-secret"]) } }, logger);

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty, "the visitor can neither see nor change the field, so no error could tell them anything");
            Assert.That(read.Answers.Single(answer => answer.Field == Count).Values.Single().Text, Is.EqualTo("3"));
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.One.Contains("'count' a value its type cannot take"));
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.None.Contains("secret"), "never the value");
        });
    }

    [TestCase("false", false)]
    [TestCase("no", false)]
    [TestCase("true", true)]
    public async Task ACheckboxsAnswer_OtherThanTrue_IsUnticked(string posted, bool ticked)
    {
        var read = await ReadAsync(Post() with { Fields = new Dictionary<Guid, PostedAnswer> { [Newsletter] = new([posted]) } });

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty);
            Assert.That(read.Answers.Any(answer => answer.Field == Newsletter), Is.EqualTo(ticked));
        });
    }

    private static PostedSubmission Post() => new()
    {
        Scope = MergeTagTestSite.Scope(form: Form),
        Now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
        TimeZone = "Europe/Zurich",
    };

    // A ranking posted without JavaScript: each option's value as a part's name, its position as the part's text.
    private static PostedSubmission Ranking(params (string Name, string Position)[] parts) => Post() with
    {
        Fields = new Dictionary<Guid, PostedAnswer> { [Colours] = new([], parts.ToDictionary(part => part.Name, part => (IReadOnlyList<string?>)[part.Position], StringComparer.Ordinal)) },
    };

    private static async Task<SubmissionAnswers> ReadAsync(PostedSubmission post, RecordingLogger<SubmissionReader>? logger = null)
    {
        var options = new OptionListTestSite().Options();
        var site = MergeTagTestSite.Create(types: Types, options: options);
        return await new SubmissionReader(Types, site.Service, options, logger ?? new RecordingLogger<SubmissionReader>()).ReadAsync(post);
    }

    private static FieldTypeRegistry Types { get; } = TestFieldTypes.Build(builder => builder.Append<ServerNumberFieldType>());

    // Each error as "alias: kind", a child's with its row.
    private static string[] Errors(SubmissionAnswers read) =>
        [.. read.Errors.Select(error => $"{AliasOf(error.Field)}{(error.Row is { } row ? $"@{row}" : "")}: {FieldMessageProperties.All.Single(property => property.Kind == error.Kind).Name}")];

    private static string AliasOf(Guid key) =>
        Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)).Single(field => field.Key == key).Alias!;

    private static Dictionary<Guid, PostedAnswer> Row(Guid field, string text) => new() { [field] = new PostedAnswer([text]) };

    private static StoredEntryFile Upload(int id, Guid field) =>
        new(id, Key(500 + id), null, 7, field, $"file{id}.pdf", $"{id:D32}", "application/pdf", 100L * id, new string('a', 64), "clean", DateTimeOffset.UnixEpoch);

    private static FormField Field(Guid key, string type, string alias, string? settings = null) => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    private static Guid Key(int number) => Guid.Parse($"80000000-0000-4000-8000-{number:D12}");

    /// <summary>A site's number that keeps its value on the server and may be prefilled from the query, which reads only numbers.</summary>
    public sealed class ServerNumberFieldType() : FormBuilderFieldTypeBase<NoFieldSettings>(new FieldTypeDeclaration
    {
        Alias = "acme.serverNumber",
        Name = "Server number",
        Icon = "icon-calculator",
        PaletteGroup = FieldPaletteGroup.Structure,
        HoldsValue = true,
        Storage = FieldStorageKind.Number,
        RuleKind = FieldKind.Number,
        ServerOnlyDefault = true,
        Features = FieldFeatures.Label | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.VisibilityRules,
    });
}
