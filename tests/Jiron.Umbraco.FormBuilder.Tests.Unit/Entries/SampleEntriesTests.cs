using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// The sample entry of previews and test runs (ROADMAP 10.1c, ADR-0187): number 42, submitted now, in the state an entry
/// of the form arrives in, for the form with the site's setting locks applied; for every field that holds a value the
/// first candidate of its own kind that the field takes, its default value first and a hidden field's only answer, read
/// by its type and passed by its checks; a calculation worked out from the others; a file, a signature, a grid without
/// columns and a field of choices without options not answered. As a page posts them (ADR-0204), the sample answers hold
/// each grid row's first column and a signature's typed name too.
/// </summary>
[TestFixture]
public sealed class SampleEntriesTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 6, 15);

    [Test]
    public async Task EveryBuiltInTypeThatHoldsAValue_IsAnsweredWithWhatItTakes()
    {
        var answers = await Samples().AnswersAsync(Scope(EntryTestForm.Definition, "en-GB"), Today);

        Assert.Multiple(() =>
        {
            Assert.That(Texts(answers, EntryTestForm.FullName), Is.EqualTo(new[] { "fullName" }), "a text without a label: its alias");
            Assert.That(Texts(answers, EntryTestForm.Email), Is.EqualTo(new[] { SampleEntries.Email }));
            Assert.That(Texts(answers, EntryTestForm.Phone), Is.EqualTo(new[] { SampleEntries.Phone }));
            Assert.That(Texts(answers, EntryTestForm.Website), Is.EqualTo(new[] { SampleEntries.Url }));
            Assert.That(Texts(answers, EntryTestForm.Age), Is.EqualTo(new[] { "3" }));
            Assert.That(Texts(answers, EntryTestForm.Stars), Is.EqualTo(new[] { "3" }));
            Assert.That(Texts(answers, EntryTestForm.Total), Is.EqualTo(new[] { "1" }), "a calculation, worked out");
            Assert.That(Texts(answers, EntryTestForm.Birthday), Is.EqualTo(new[] { "2026-06-15" }));
            Assert.That(Texts(answers, EntryTestForm.Arrival), Is.EqualTo(new[] { "09:30" }));
            Assert.That(Texts(answers, EntryTestForm.Visit), Is.EqualTo(new[] { "2026-06-15T09:30" }));
            Assert.That(Texts(answers, EntryTestForm.Newsletter), Is.EqualTo(new[] { "true" }));
            Assert.That(Texts(answers, EntryTestForm.Returning), Is.EqualTo(new[] { "yes" }));
            Assert.That(Texts(answers, EntryTestForm.Terms), Is.EqualTo(new[] { "true" }), "a consent ticked, with its wording's hash");
            Assert.That(answers.Find(EntryTestForm.Terms)!.Values[0].StoredText, Has.Length.EqualTo(64));
            Assert.That(Texts(answers, EntryTestForm.Country), Is.EqualTo(new[] { "GB" }), "the culture's country");
            Assert.That(Texts(answers, EntryTestForm.Name), Is.EqualTo(new[] { $$"""{"first":"{{SampleEntries.GivenName}}","last":"{{SampleEntries.FamilyName}}"}""" }), "the parts the field asks for");
            Assert.That(Texts(answers, EntryTestForm.Address)[0], Does.Contain("\"city\":\"Exampleton\""));
            Assert.That(Texts(answers, EntryTestForm.GuestName), Is.EqualTo(new[] { "guestName" }), "a repeatable group's field, in its first row");
            Assert.That(
                new[] { EntryTestForm.Toppings, EntryTestForm.Size, EntryTestForm.Satisfaction, EntryTestForm.Priorities, EntryTestForm.Attachments, EntryTestForm.Signature, EntryTestForm.Ticket, EntryTestForm.Guests, EntryTestForm.Heading, EntryTestForm.Campaign }
                    .Where(field => answers.Find(field) is not null),
                Is.Empty,
                "choices without options, a grid, files, a signature, the group itself, a heading, and a hidden field without a default value");
        });
    }

    [Test]
    public async Task AFieldsOwnKindAndChecks_ChooseTheCandidate_ANumberFromTen_AndChoicesAsManyAsItAsksFor()
    {
        var number = Field("number", "guests", settings: """{"min":"10"}""");
        var several = Field("checkboxes", "topics", settings: """{"minSelections":2}""") with { Options = Items("delivery", "press", "billing") };
        var dropdown = Field("dropdown", "toppings", settings: """{"multiple":true,"minSelections":2}""") with { Options = Items("cheese", "olives", "basil") };
        var one = Field("radio", "size") with { Options = Items("small", "large") };
        var answers = await Samples().AnswersAsync(Scope(Definition(number, several, dropdown, one), "en-GB"), Today);

        Assert.Multiple(() =>
        {
            Assert.That(Texts(answers, number.Key), Is.EqualTo(new[] { "10" }));
            Assert.That(Texts(answers, several.Key), Is.EqualTo(new[] { "delivery", "press" }));
            Assert.That(Texts(answers, dropdown.Key), Is.EqualTo(new[] { "cheese", "olives" }), "a dropdown that takes several, by its own kind");
            Assert.That(Texts(answers, one.Key), Is.EqualTo(new[] { "small" }));
        });
    }

    [Test]
    public async Task AFieldsDefaultValue_ComesFirst_AndIsAHiddenFieldsOnlyAnswer_RenderedForTheSample()
    {
        var source = Field("text", "source") with { DefaultValue = FieldDefaultValue.Of("web") };
        var campaign = Field("hidden", "campaign") with { DefaultValue = FieldDefaultValue.Of("spring {{ form.alias }}") };
        var bare = Field("hidden", "bare");
        var mergeTags = Substitute.For<IFormBuilderMergeTagService>();
        mergeTags.RenderAsync(Arg.Any<string>(), MergeTagPlace.HiddenValue, MergeTagContext.Text, Arg.Any<MergeTagScope>(), Arg.Any<CancellationToken>())
            .Returns(call => new MergeTagRendering(call.ArgAt<string>(0).Replace("{{ form.alias }}", call.ArgAt<MergeTagScope>(3).FormAlias, StringComparison.Ordinal), []));

        var answers = await Samples(mergeTags: mergeTags).AnswersAsync(Scope(Definition(source, campaign, bare), "en-GB"), Today);

        Assert.Multiple(() =>
        {
            Assert.That(Texts(answers, source.Key), Is.EqualTo(new[] { "web" }));
            Assert.That(Texts(answers, campaign.Key), Is.EqualTo(new[] { "spring contact" }), "its tags rendered for the sample");
            Assert.That(answers.Find(bare.Key), Is.Null, "a hidden field without a default: visitors send nothing else");
        });
    }

    [Test]
    public async Task ACalculation_IsWorkedOutFromTheSamplesAnswers_RoundedToItsPlaces()
    {
        var total = Field("calculation", "total", settings: """{"formula":"age * 2 / 7","format":{"decimals":2}}""");
        var age = Field("number", "age");
        var answers = await Samples().AnswersAsync(Scope(Definition(total, age), "en-GB"), Today);

        Assert.That(Texts(answers, total.Key), Is.EqualTo(new[] { "0.86" }), "3 * 2 / 7 to two places, a field after it read too");
    }

    [Test]
    public async Task ALabel_IsTheTextsAnswer_InTheCulture_AndOneNamingADictionaryItem_TheAlias()
    {
        var named = Field("text", "name") with { Label = new LocalizedText([new("", "Your name"), new("de-DE", "Ihr Name")]) };
        var item = Field("text", "city") with { Label = new LocalizedText("#jfbCity") };
        var definition = Definition(named, item);

        var german = await Samples().AnswersAsync(Scope(definition, "de-DE"), Today);
        var english = await Samples().AnswersAsync(Scope(definition, "en-GB"), Today);

        Assert.Multiple(() =>
        {
            Assert.That(Texts(german, named.Key), Is.EqualTo(new[] { "Ihr Name" }));
            Assert.That(Texts(english, named.Key), Is.EqualTo(new[] { "Your name" }));
            Assert.That(Texts(english, item.Key), Is.EqualTo(new[] { "city" }));
        });
    }

    [Test]
    public async Task AListsOptions_AreItsSources_AndACascadingField_TakesOneOfItsParentsSample()
    {
        var region = Field("dropdown", "region") with { Options = new FieldOptions { Source = OptionSource.List, OptionList = Guid.NewGuid() } };
        var city = Field("dropdown", "city") with
        {
            Options = new FieldOptions
            {
                Parent = region.Key,
                Items = [new FieldOption { Value = "hamburg", ParentValues = ["north"] }, new FieldOption { Value = "munich", ParentValues = ["south"] }],
            },
        };
        var options = Substitute.For<IFormBuilderOptionService>();
        options.GetAsync(Arg.Is<FormField>(field => field.Key == region.Key), Arg.Any<FormBuilderOptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new FormBuilderResolvedOptions([new FormBuilderOption("south"), new FormBuilderOption("north")], IsAvailable: true));

        var answers = await Samples(options).AnswersAsync(Scope(Definition(region, city), "en-GB"), Today);

        Assert.Multiple(() =>
        {
            Assert.That(Texts(answers, region.Key), Is.EqualTo(new[] { "south" }), "the source's first option");
            Assert.That(Texts(answers, city.Key), Is.EqualTo(new[] { "munich" }), "the option its parent's sample leaves");
            options.Received().GetAsync(Arg.Any<FormField>(), Arg.Is<FormBuilderOptionContext>(context => context.Culture == "en-GB"), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task TheScope_IsEntry42_SubmittedNow_InTheStateItsFormGivesAnEntry_InTheCulture()
    {
        var definition = Definition(Field("email", "email"));
        var manual = definition with { Settings = new FormSettings { Submission = new FormSubmissionSettings { Approval = ApprovalMode.Manual } } };
        var verified = definition with
        {
            Settings = new FormSettings { Submission = new FormSubmissionSettings { Verification = new EmailVerification { EmailField = definition.Pages[0].Groups[0].Fields[0].Key } } },
        };

        var scope = await Samples().ScopeAsync(definition, "Contact", "contact", "de-DE");

        Assert.Multiple(async () =>
        {
            Assert.That((scope.FormName, scope.FormAlias, scope.Culture), Is.EqualTo(("Contact", "contact", "de-DE")));
            Assert.That((scope.Entry!.Number, scope.Entry.State, scope.Entry.Submitted), Is.EqualTo((SampleEntries.Number, "approved", Now)), "approved at once by default");
            Assert.That((await Samples().ScopeAsync(manual, "Contact", "contact", "de-DE")).Entry!.State, Is.EqualTo("submitted"));
            Assert.That((await Samples().ScopeAsync(verified, "Contact", "contact", "de-DE")).Entry!.State, Is.EqualTo("unverified"));
            Assert.That(scope.Form, Is.EqualTo(definition));
            Assert.That(scope.Answers.Find(definition.Pages[0].Groups[0].Fields[0].Key), Is.Not.Null);
        });
    }

    [Test]
    public async Task TheSitesSettingLocks_Apply_ACountryOnlyFromWhereTheSiteAllows()
    {
        var country = Field("country", "country");
        var options = new FormBuilderOptions();
        options.Designer.SettingLocks.Add(new DesignerSettingLock { Kind = DesignerSettingLock.FieldKind, Type = "country", Setting = "allowed", Value = """["CH","AT"]""", Hidden = true });
        var locks = new FieldSettingLocks(Options.Create(options), TestFieldTypes.Registry);

        var scope = await Samples(locks: locks).ScopeAsync(Definition(country), "Contact", "contact", "en-GB");

        Assert.Multiple(() =>
        {
            Assert.That(Texts(scope.Answers, country.Key), Is.EqualTo(new[] { "CH" }), "not GB, which the site's lock leaves out");
            Assert.That(scope.Form.Pages[0].Groups[0].Fields[0].Settings?.ToString(), Does.Contain("CH"), "the form as the site runs it");
        });
    }

    [Test]
    public async Task ThePostedAnswers_HoldEachGridRowsFirstColumn_AndASignaturesTypedName_ForThePageToDraw()
    {
        var email = Field("email", "email");
        var grid = Field("grid", "satisfaction") with { Rows = [new GridRow { Key = Guid.NewGuid() }, new GridRow { Key = Guid.NewGuid() }], Options = Items("good", "poor") };
        var signature = Field("signature", "signed");
        var guests = Field("repeater", "guests") with { Fields = [Field("text", "guestName")] };
        var definition = Definition(email, grid, signature, guests);

        var posted = await Samples().PostedAsync(definition, "Contact", "contact", "en-GB");
        var answers = await Samples().AnswersAsync(Scope(definition, "en-GB"), Today);

        Assert.Multiple(() =>
        {
            Assert.That(posted[email.Key].Values, Is.EqualTo(new[] { SampleEntries.Email }));
            Assert.That(
                posted[grid.Key].Parts.Select(part => (part.Key, part.Value.Single())),
                Is.EquivalentTo(grid.Rows.Select(row => (GridFieldType.RowPart(row.Key), "good"))),
                "each row's first column");
            Assert.That(posted[signature.Key].Parts[SignatureFieldType.TypedPart], Is.EqualTo(new[] { SampleEntries.GivenName + " " + SampleEntries.FamilyName }));
            Assert.That(posted[guests.Fields[0].Key].Values, Is.EqualTo(new[] { "guestName" }), "a repeatable group's field, as its one row posts it");
            Assert.That(posted.ContainsKey(guests.Key), Is.False, "the group itself posts nothing");
            Assert.That(answers.Find(grid.Key), Is.Not.Null, "the merge tags read the grid too");
            Assert.That(answers.Find(signature.Key), Is.Null, "a signature's tag reads a file, which no sample has");
            Assert.That(() => Samples().PostedAsync(null!, "Contact", "contact", "en-GB"), Throws.ArgumentNullException);
            Assert.That(() => Samples().PostedAsync(definition, "Contact", "contact", null!), Throws.ArgumentNullException);
        });
    }

    private static SampleEntries Samples(IFormBuilderOptionService? options = null, IFormBuilderMergeTagService? mergeTags = null, FieldSettingLocks? locks = null)
    {
        var settings = Substitute.For<IFormBuilderSettingsService>();
        settings.GetAsync<GeneralSettings>(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(
            new SettingsSnapshot<GeneralSettings>(new GeneralSettings(), 1, new HashSet<string>(), new Dictionary<string, SettingsLock>()));
        var clock = new TestTimeProvider(Now);
        return new SampleEntries(
            TestFieldTypes.Registry,
            options ?? Substitute.For<IFormBuilderOptionService>(),
            mergeTags ?? Substitute.For<IFormBuilderMergeTagService>(),
            locks ?? new FieldSettingLocks(Options.Create(new FormBuilderOptions()), TestFieldTypes.Registry),
            new SiteTimeZones(settings, clock, new SystemTimeZoneLinks(), NullLogger<SiteTimeZones>.Instance),
            clock);
    }

    private static MergeTagScope Scope(FormDefinition definition, string culture) =>
        new() { Form = definition, FormName = "Contact", FormAlias = "contact", Culture = culture };

    private static string[] Texts(MergeTagAnswers answers, Guid field) => answers.Find(field) is { } answer ? [.. answer.Values.Select(value => value.Text)] : [];

    private static FormDefinition Definition(params FormField[] fields) => new()
    {
        Key = Guid.NewGuid(),
        Pages = [new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid(), Fields = fields }] }],
    };

    private static FormField Field(string type, string alias, string? settings = null) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    private static FieldOptions Items(params string[] values) => new() { Items = [.. values.Select(value => new FieldOption { Value = value })] };
}
