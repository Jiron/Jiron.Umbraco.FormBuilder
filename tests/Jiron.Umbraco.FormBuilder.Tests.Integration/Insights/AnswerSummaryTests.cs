using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Insights;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Insights;

/// <summary>
/// The answer summaries against entries a real site stored (ROADMAP 11.5b, ADR-0212): each kind of summary, from the
/// database and labelled as the field's type shows its values, several choices and a repeatable group's rows included;
/// never a test entry, an entry in Spam, a sensitive field or one kept only until the actions run, nor a child of a group
/// that is either; an older version's options under their field, marked; the span and the list's filter choosing the
/// entries; the free texts a page at a time, across a group's rows at one instant; the visitors' own texts one line where
/// a list gives the options or an older version offered Other; a grid counted once per entry with an older version's
/// rows and columns marked; a scale's groups only where every version asked from 0 to 10, with their edges at 6 and 8;
/// an answer of several values counted once; and only free text read on, a long answer cut.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class AnswerSummaryTests : ActionTestBase
{
    private static readonly FormField Topic = new()
    {
        Key = Guid.Parse("c5000000-0000-4000-8000-000000000001"),
        Type = "radio",
        Alias = "topic",
        Label = new LocalizedText("Topic"),
        Settings = SettingsObject.Parse("""{"other":true}"""),
        Options = new FieldOptions { Items = [Item("sales", "Sales"), Item("support", "Support"), Item("delivery", "Delivery")] },
    };

    private static readonly FormField Interests = new()
    {
        Key = Guid.Parse("c5000000-0000-4000-8000-000000000002"),
        Type = "checkboxes",
        Alias = "interests",
        Label = new LocalizedText("Interests"),
        Options = new FieldOptions { Items = [Item("a", "Apps"), Item("b", "Books"), Item("c", "Cars")] },
    };

    private static readonly FormField Country = new() { Key = Guid.Parse("c5000000-0000-4000-8000-000000000003"), Type = "country", Alias = "country", Label = new LocalizedText("Country") };

    private static readonly FormField Rank = new()
    {
        Key = Guid.Parse("c5000000-0000-4000-8000-000000000004"),
        Type = "ranking",
        Alias = "rank",
        Label = new LocalizedText("Rank"),
        Options = new FieldOptions { Items = [Item("x", "X-ray"), Item("y", "Yoga"), Item("z", "Zen")] },
    };

    private static readonly GridRow Speed = new() { Key = Guid.Parse("c5000000-0000-4000-8000-000000000051"), Label = new LocalizedText("Speed") };

    private static readonly GridRow Price = new() { Key = Guid.Parse("c5000000-0000-4000-8000-000000000052"), Label = new LocalizedText("Price") };

    private static readonly FormField Service = new()
    {
        Key = Guid.Parse("c5000000-0000-4000-8000-000000000005"),
        Type = "grid",
        Alias = "service",
        Label = new LocalizedText("Service"),
        Options = new FieldOptions { Items = [Item("good", "Good"), Item("bad", "Bad")] },
        Rows = [Speed, Price],
    };

    private static readonly FormField Amount = new() { Key = Guid.Parse("c5000000-0000-4000-8000-000000000006"), Type = "number", Alias = "amount", Label = new LocalizedText("Amount") };

    private static readonly FormField Recommend = new() { Key = Guid.Parse("c5000000-0000-4000-8000-000000000007"), Type = "scale", Alias = "recommend", Label = new LocalizedText("Recommend") };

    private static readonly FormField Message = new() { Key = Guid.Parse("c5000000-0000-4000-8000-000000000008"), Type = "textarea", Alias = "message", Label = new LocalizedText("Message") };

    private static readonly FormField Email = new() { Key = Guid.Parse("c5000000-0000-4000-8000-000000000009"), Type = "email", Alias = "email", Label = new LocalizedText("Email") };

    private static readonly FormField Agreed = new() { Key = Guid.Parse("c5000000-0000-4000-8000-00000000000a"), Type = "checkbox", Alias = "agreed", Label = new LocalizedText("Agreed") };

    private static readonly FormField Secret = new() { Key = Guid.Parse("c5000000-0000-4000-8000-00000000000b"), Type = "text", Alias = "secret", Label = new LocalizedText("Secret"), Sensitive = true };

    private static readonly FormField Meal = new()
    {
        Key = Guid.Parse("c5000000-0000-4000-8000-00000000000d"),
        Type = "radio",
        Alias = "meal",
        Label = new LocalizedText("Meal"),
        Options = new FieldOptions { Items = [Item("veg", "Veggie"), Item("fish", "Fish")] },
    };

    private static readonly FormField Fleeting = new() { Key = Guid.Parse("c5000000-0000-4000-8000-00000000000e"), Type = "text", Alias = "fleeting", Label = new LocalizedText("Fleeting"), Keep = FieldKeep.UntilActionsRun };

    private static readonly FormField Seat = new() { Key = Guid.Parse("c5000000-0000-4000-8000-000000000010"), Type = "text", Alias = "seat", Label = new LocalizedText("Seat") };

    private static readonly FormField Passes = new() { Key = Guid.Parse("c5000000-0000-4000-8000-000000000011"), Type = "repeater", Alias = "passes", Label = new LocalizedText("Passes"), Keep = FieldKeep.UntilActionsRun, Fields = [Seat] };

    private static readonly FormField Code = new() { Key = Guid.Parse("c5000000-0000-4000-8000-000000000012"), Type = "text", Alias = "code", Label = new LocalizedText("Code") };

    private static readonly FormField Codes = new() { Key = Guid.Parse("c5000000-0000-4000-8000-000000000013"), Type = "repeater", Alias = "codes", Label = new LocalizedText("Codes"), Sensitive = true, Fields = [Code] };

    private static readonly FormField Guests = new() { Key = Guid.Parse("c5000000-0000-4000-8000-00000000000c"), Type = "repeater", Alias = "guests", Label = new LocalizedText("Guests"), Fields = [Meal] };

    private AnswerSummaries Summaries => GetRequiredService<AnswerSummaries>();

    private static InsightSpan Today => new(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2));

    [Test]
    public async Task EachKindOfField_IsSummarisedInTheDatabase_AsItsTypeShowsItsValues()
    {
        var form = await PublishAsync(name: "Survey", fields: [NameField, Topic, Interests, Country, Rank, Service, Amount, Recommend, Message, Email, Agreed, Secret, Fleeting, Guests, Passes, Codes]);
        await SendAsync(form, """{"name":"Ada","topic":"sales","interests":["a","b"],"country":"DE","rank":["x","y","z"],"service.c5000000-0000-4000-8000-000000000051":"good","service.c5000000-0000-4000-8000-000000000052":"good","amount":"5","recommend":"10","message":"First note","email":"ada@visitor.jfb.test","agreed":true,"secret":"s1","fleeting":"Gone soon","guests":[{"meal":"veg"},{"meal":"fish"}],"passes":[{"seat":"12A"}],"codes":[{"code":"X1"}]}""");
        await SendAsync(form, """{"name":"Bo","topic":"support","interests":["a"],"country":"DE","rank":["y","x","z"],"service.c5000000-0000-4000-8000-000000000051":"bad","amount":"10","recommend":"9","message":"Second note","agreed":true,"guests":[{"meal":"veg"}]}""");
        await SendAsync(form, """{"name":"Cy","topic":"sales","interests":["a","b"],"country":"AT","rank":["x","z","y"],"service.c5000000-0000-4000-8000-000000000051":"good","amount":"30","recommend":"7","email":"cy@visitor.jfb.test"}""");
        await SendAsync(form, """{"name":"Di","topic.other":"Partnership","amount":"50","recommend":"3"}""");
        await SendAsync(form, """{"name":"Spammer","topic":"sales","amount":"999"}""", honeypot: true);
        await TestEntryAsync(form, Topic.Key, "sales");

        var report = (await Summaries.SummarizeAsync(form, new AnswerRequest(Today, new EntryListFilter())))!;
        AnswerSummary Of(FormField field) => report.Fields.Single(summary => summary.Key == field.Key);
        static string[] Lines(AnswerSummary summary) => [.. summary.Options.Select(line => $"{(line.Other ? "Other" : line.Label)} {line.Count}{(line.Position is { } at ? " @" + at.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty)}{(line.Older ? " (older)" : string.Empty)}")];

        Assert.Multiple(() =>
        {
            Assert.That(report.Entries, Is.EqualTo(4), "no test entry, no entry in Spam");
            Assert.That(report.Fields.Select(summary => summary.Key), Does.Not.Contain(Secret.Key), "no sensitive field");
            Assert.That(report.Fields.Select(summary => summary.Key), Does.Not.Contain(Fleeting.Key), "nor one kept only until the actions run");
            Assert.That(report.Fields.Select(summary => summary.Key), Does.Not.Contain(Seat.Key), "nor a child of a group kept only until the actions run");
            Assert.That(report.Fields.Select(summary => summary.Key), Does.Not.Contain(Code.Key), "nor a child of a sensitive group");
            Assert.That(report.Fields.Select(summary => summary.Key), Does.Not.Contain(Guests.Key), "a repeatable group by its children");
            Assert.That((Of(Topic).Kind, Of(Topic).Answered), Is.EqualTo((AnswerSummaryKind.Options, 4L)));
            Assert.That(Lines(Of(Topic)), Is.EqualTo(new[] { "Sales 2", "Support 1", "Delivery 0", "Other 1" }), "the live options in their order, then the visitors' own texts as one line");
            Assert.That((Of(Interests).Answered, Of(Interests).Several), Is.EqualTo((3L, true)));
            Assert.That(Lines(Of(Interests)), Is.EqualTo(new[] { "Apps 3", "Books 2", "Cars 0" }));
            Assert.That(Lines(Of(Country)), Is.EqualTo(new[] { "Germany 2", "Austria 1" }), "named as the type names countries, the most first");
            Assert.That(Of(Rank).Kind, Is.EqualTo(AnswerSummaryKind.Ranking));
            Assert.That(Lines(Of(Rank)), Is.EqualTo(new[] { "X-ray 3 @1.3", "Yoga 3 @2", "Zen 3 @2.7" }), "by the average position");
            Assert.That(Of(Service).Kind, Is.EqualTo(AnswerSummaryKind.Grid));
            Assert.That(Of(Service).Rows.Select(row => $"{row.Label} {row.Answered}: {string.Join(", ", row.Options.Select(option => $"{option.Label} {option.Count}"))}"), Is.EqualTo(new[] { "Speed 3: Good 2, Bad 1", "Price 1: Good 1, Bad 0" }));
            Assert.That(Of(Amount).Numbers, Is.EqualTo(new AnswerNumberSummary(23.75, 20, 5, 50, null)));
            Assert.That(Of(Recommend).Numbers!.Groups, Is.EqualTo(new AnswerScaleGroups(1, 1, 2, 25)), "a scale from 0 to 10: one at 3, one at 7, two at 9 or 10");
            Assert.That(Of(Message).Kind, Is.EqualTo(AnswerSummaryKind.Texts));
            Assert.That(Of(Message).Texts.Select(text => text.Text), Is.EqualTo(new[] { "Second note", "First note" }), "newest first, never counted");
            Assert.That(Of(Message).More, Is.Null);
            Assert.That((Of(Email).Kind, Of(Email).Answered), Is.EqualTo((AnswerSummaryKind.Answered, 2L)), "contact details say how often they were answered");
            Assert.That((Of(Agreed).Kind, Of(Agreed).Ticked), Is.EqualTo((AnswerSummaryKind.Checkbox, 2L)));
            Assert.That((Of(Meal).Answered, Of(Meal).Parent, Of(Meal).ParentLabel), Is.EqualTo((3L, (Guid?)Guests.Key, "Guests")), "a group's child by its rows");
            Assert.That(Lines(Of(Meal)), Is.EqualTo(new[] { "Veggie 2", "Fish 1" }));
        });
    }

    [Test]
    public async Task AnOlderVersionsOption_CountsUnderItsField_MarkedAndNamedAsThatVersionNamedIt()
    {
        var older = Topic with { Settings = null, Options = new FieldOptions { Items = [Item("sales", "Sales"), Item("legacy", "Legacy offer")] } };
        var form = await PublishAsync(name: "Versions", fields: [NameField, older]);
        await SendAsync(form, """{"name":"Ada","topic":"legacy"}""");
        await SendAsync(form, """{"name":"Bo","topic":"sales"}""");
        await SaveAndPublishAsync(form, definition => definition with
        {
            Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [NameField, older with { Options = new FieldOptions { Items = [Item("sales", "Sales"), Item("press", "Press")] } }] }] }],
        });
        await SendAsync(form, """{"name":"Cy","topic":"press"}""", revision: 2);

        var report = (await Summaries.SummarizeAsync(form, new AnswerRequest(Today, new EntryListFilter())))!;
        var topic = report.Fields.Single(summary => summary.Key == Topic.Key);

        Assert.That(topic.Options.Select(line => (line.Label, line.Count, line.Older)), Is.EqualTo(new[] { ("Sales", 1L, false), ("Press", 1L, false), ("Legacy offer", 1L, true) }));
    }

    [Test]
    public async Task TheSpanAndTheListsFilter_ChooseTheEntries()
    {
        var form = await PublishAsync(name: "Filtered", fields: [NameField, Topic, Amount]);
        await SendAsync(form, """{"name":"Ada","topic":"sales","amount":"5"}""");
        await SendAsync(form, """{"name":"Bo","topic":"support","amount":"10"}""");
        await SendAsync(form, """{"name":"Spammer","topic":"sales","amount":"999"}""", honeypot: true);
        var sales = new EntryListFilter { Fields = [new EntryFieldCondition { Field = Topic.Key, Test = EntryFieldTest.OneOf, Values = ["sales"] }] };

        var filtered = (await Summaries.SummarizeAsync(form, new AnswerRequest(Today, sales)))!;
        var later = (await Summaries.SummarizeAsync(form, new AnswerRequest(new InsightSpan(new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 9)), new EntryListFilter())))!;
        var spamOnly = (await Summaries.SummarizeAsync(form, new AnswerRequest(Today, new EntryListFilter { States = [EntryState.Spam] })))!;
        var everyState = (await Summaries.SummarizeAsync(form, new AnswerRequest(Today, new EntryListFilter { States = [EntryState.Spam, EntryState.Unverified, EntryState.Submitted, EntryState.Approved, EntryState.Rejected] })))!;
        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(() => Summaries.SummarizeAsync(form, new AnswerRequest(Today, new EntryListFilter { Fields = [new EntryFieldCondition { Field = Guid.NewGuid(), Test = EntryFieldTest.Answered }] })));

        Assert.Multiple(() =>
        {
            Assert.That(filtered.Entries, Is.EqualTo(1));
            Assert.That(filtered.Fields.Single(summary => summary.Key == Amount.Key).Numbers!.Average, Is.EqualTo(5));
            Assert.That(later.Entries, Is.Zero, "no entry was received in that span");
            Assert.That(later.Fields.Single(summary => summary.Key == Topic.Key).Options.Select(line => line.Count), Is.All.Zero);
            Assert.That(spamOnly.Entries, Is.Zero, "Spam is never summarised, whatever the filter names");
            Assert.That(everyState.Entries, Is.EqualTo(2), "nor beside the other states");
            Assert.That(everyState.Fields.Single(summary => summary.Key == Amount.Key).Numbers!.Highest, Is.EqualTo(10));
            Assert.That(refused!.Problems.Select(problem => problem.Path), Is.EqualTo(new[] { "filter.fields[0].field" }));
        });
    }

    [Test]
    public async Task FreeTextAnswers_ComeAPageAtATime_EachWithItsEntry()
    {
        var form = await PublishAsync(name: "Texts", fields: [NameField, Message]);
        for (var i = 1; i <= AnswerSummaries.TextsShown + 3; i++)
        {
            Clock.Advance(TimeSpan.FromMinutes(1));
            await SendAsync(form, $$"""{"name":"Person {{i}}","message":"Note {{i}}"}""");
        }

        var request = new AnswerRequest(Today, new EntryListFilter());
        var first = (await Summaries.SummarizeAsync(form, request))!.Fields.Single(summary => summary.Key == Message.Key);
        var rest = (await Summaries.TextsAsync(form, Message.Key, request, first.More))!;
        var ids = await Entries.ListIdsAsync((await FormRows.GetAsync(form))!.Id, 1000);
        var newest = (await Entries.GetByIdAsync(ids[^1]))!;

        Assert.Multiple(async () =>
        {
            Assert.That(first.Texts.Select(text => text.Text), Is.EqualTo(new[] { "Note 8", "Note 7", "Note 6", "Note 5", "Note 4" }));
            Assert.That(first.Texts[0].Entry, Is.EqualTo(newest.Key), "each links to its entry");
            Assert.That(first.More, Is.Not.Null);
            Assert.That(rest.Texts.Select(text => text.Text), Is.EqualTo(new[] { "Note 3", "Note 2", "Note 1" }));
            Assert.That(rest.More, Is.Null);
            Assert.That(await Summaries.TextsAsync(form, NameField.Key, request, first.More), Is.Not.Null, "a line of text is free text too");
            Assert.That(await Summaries.TextsAsync(form, Guid.NewGuid(), request, first.More), Is.Null, "a field the form has not");
            Assert.ThrowsAsync<FormBuilderValidationException>(() => Summaries.TextsAsync(form, Message.Key, request, "not a cursor"));
        });
    }

    [Test]
    public async Task FreeTextsOfARepeatableGroup_ComeAPageAtATime_EveryRowOnce_AtOneInstant()
    {
        var remark = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "remark", Label = new LocalizedText("Remark") };
        var remarks = new FormField { Key = Guid.NewGuid(), Type = "repeater", Alias = "remarks", Label = new LocalizedText("Remarks"), Fields = [remark] };
        var form = await PublishAsync(name: "Remarks", fields: [NameField, remarks]);
        for (var i = 1; i <= 4; i++)
        {
            // The clock stands still: the four entries share their instant, and only their rows tell them apart.
            await SendAsync(form, $$"""{"name":"Person {{i}}","remarks":[{"remark":"{{i}}a"},{"remark":"{{i}}b"},{"remark":"{{i}}c"}]}""");
        }

        var request = new AnswerRequest(Today, new EntryListFilter());
        var first = (await Summaries.SummarizeAsync(form, request))!.Fields.Single(summary => summary.Key == remark.Key);
        var texts = first.Texts.Select(text => text.Text).ToList();
        for (var more = first.More; more is not null;)
        {
            var page = (await Summaries.TextsAsync(form, remark.Key, request, more))!;
            texts.AddRange(page.Texts.Select(text => text.Text));
            more = page.More;
        }

        Assert.Multiple(() =>
        {
            Assert.That(first.Texts.Select(text => text.Text), Is.EqualTo(new[] { "4a", "4b", "4c", "3a", "3b" }), "a page that ends inside an entry's rows");
            Assert.That(texts, Is.EqualTo(new[] { "4a", "4b", "4c", "3a", "3b", "3c", "2a", "2b", "2c", "1a", "1b", "1c" }));
            Assert.That((first.Answered, first.ParentLabel), Is.EqualTo((12L, "Remarks")));
        });
    }

    [Test]
    public async Task TheVisitorsOwnTexts_AreOneLine_WhereAListGivesTheOptions_OrAnOlderVersionOfferedOther()
    {
        var list = Guid.NewGuid();
        await GetRequiredService<OptionListService>().CreateAsync(list, new OptionListInput($"channels{list:N}", "Channels", OptionListKind.Manual, Items: """[{"value":"web","label":{"":"Website"}},{"value":"fair"}]"""));
        var channel = new FormField
        {
            Key = Guid.NewGuid(),
            Type = "radio",
            Alias = "channel",
            Label = new LocalizedText("Channel"),
            Settings = SettingsObject.Parse("""{"other":true}"""),
            Options = new FieldOptions { Source = OptionSource.List, OptionList = list },
        };
        var form = await PublishAsync(name: "Channels", fields: [NameField, channel, Topic]);
        await SendAsync(form, """{"name":"Ada","channel":"web","topic":"sales"}""");
        await SendAsync(form, """{"name":"Bo","channel":"fair","topic.other":"A partner of ours"}""");
        await SendAsync(form, """{"name":"Cy","channel.other":"My neighbour"}""");
        await SendAsync(form, """{"name":"Di","channel.other":"A podcast"}""");
        await SaveAndPublishAsync(form, definition => definition with
        {
            Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [NameField, channel, Topic with { Settings = null }] }] }],
        });
        await SendAsync(form, """{"name":"Ed","topic":"support"}""", revision: 2);

        var report = (await Summaries.SummarizeAsync(form, new AnswerRequest(Today, new EntryListFilter())))!;
        string[] Lines(Guid key) => [.. report.Fields.Single(summary => summary.Key == key).Options.Select(line => $"{(line.Other ? "Other" : line.Label)} {line.Count}")];

        Assert.Multiple(() =>
        {
            Assert.That(Lines(channel.Key), Is.EquivalentTo(new[] { "Website 1", "fair 1", "Other 2" }), "the list's values by their labels or themselves, the visitors' words as one line");
            Assert.That(Lines(channel.Key)[^1], Is.EqualTo("Other 2"), "the visitors' own texts last");
            Assert.That(Lines(Topic.Key), Is.EqualTo(new[] { "Sales 1", "Support 1", "Delivery 0", "Other 1" }), "Other offered by an older version only");
        });
    }

    [Test]
    public async Task AGrid_CountsEachEntryOnce_AndKeepsTheRowsAndColumnsOfOlderVersions_Marked()
    {
        var older = Service with { Options = new FieldOptions { Items = [Item("good", "Good"), Item("bad", "Bad"), Item("okay", "Okay")] } };
        var form = await PublishAsync(name: "Grid", fields: [NameField, older]);
        await SendAsync(form, """{"name":"Ada","service.c5000000-0000-4000-8000-000000000051":"good"}""");
        await SendAsync(form, """{"name":"Bo","service.c5000000-0000-4000-8000-000000000052":"okay"}""");
        await SaveAndPublishAsync(form, definition => definition with
        {
            Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [NameField, Service with { Rows = [Speed] }] }] }],
        });
        await SendAsync(form, """{"name":"Cy","service.c5000000-0000-4000-8000-000000000051":"bad"}""", revision: 2);

        var grid = (await Summaries.SummarizeAsync(form, new AnswerRequest(Today, new EntryListFilter())))!.Fields.Single(summary => summary.Key == Service.Key);
        static string Row(AnswerGridRow row) =>
            $"{row.Label}{(row.Removed ? " (removed)" : string.Empty)} {row.Answered}: {string.Join(", ", row.Options.Select(option => $"{option.Label}{(option.Older ? " (older)" : string.Empty)} {option.Count}"))}";

        Assert.Multiple(() =>
        {
            Assert.That(grid.Answered, Is.EqualTo(3), "each entry once, whichever row it answered");
            Assert.That(grid.Rows.Select(Row), Is.EqualTo(new[] { "Speed 2: Good 1, Bad 1", "Price (removed) 1: Good 0, Bad 0, Okay (older) 1" }));
        });
    }

    [Test]
    public async Task AScale_HasItsGroups_OnlyWhereEveryVersionAskedFrom0To10()
    {
        var narrow = Recommend with { Settings = SettingsObject.Parse("""{"from":1,"to":5}""") };
        var form = await PublishAsync(name: "Scales", fields: [NameField, narrow]);
        await SendAsync(form, """{"name":"Ada","recommend":"5"}""");
        await SaveAndPublishAsync(form, definition => definition with
        {
            Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [NameField, Recommend] }] }],
        });
        await SendAsync(form, """{"name":"Bo","recommend":"9"}""", revision: 2);

        var scale = (await Summaries.SummarizeAsync(form, new AnswerRequest(Today, new EntryListFilter())))!.Fields.Single(summary => summary.Key == Recommend.Key);

        Assert.That((scale.Numbers!.Lowest, scale.Numbers.Highest, scale.Numbers.Groups), Is.EqualTo((5.0, 9.0, (AnswerScaleGroups?)null)), "a 5 of five is no low score of ten");
    }

    [Test]
    public async Task OnlyFreeText_IsReadOn_AndALongAnswerIsCutWhereItsEntryHoldsTheWhole()
    {
        var form = await PublishAsync(name: "Long", fields: [NameField, Message, Email, Amount]);
        var longText = new string('x', AnswerSummaries.MaxTextLength + 100);
        await SendAsync(form, $$"""{"name":"Ada","message":"{{longText}}","email":"ada@visitor.jfb.test","amount":"5"}""");
        var request = new AnswerRequest(Today, new EntryListFilter());

        var message = (await Summaries.SummarizeAsync(form, request))!.Fields.Single(summary => summary.Key == Message.Key);
        var email = await Summaries.TextsAsync(form, Email.Key, request, after: null);
        var amount = await Summaries.TextsAsync(form, Amount.Key, request, after: null);
        var name = await Summaries.TextsAsync(form, NameField.Key, request, after: null);

        Assert.Multiple(() =>
        {
            Assert.That(message.Texts.Single().Text, Has.Length.EqualTo(AnswerSummaries.MaxTextLength + 1), "500 characters and the ellipsis");
            Assert.That(message.Texts.Single().Text, Does.EndWith(((char)0x2026).ToString()));
            Assert.That(email, Is.Null, "an email is no text to read through");
            Assert.That(amount, Is.Null, "nor a number");
            Assert.That(name?.Texts.Select(text => text.Text), Is.EqualTo(new[] { "Ada" }), "a line of text is");
        });
    }

    [Test]
    public async Task AScalesGroups_EndAt6And8_AsTheirNamesSay()
    {
        var form = await PublishAsync(name: "Edges", fields: [NameField, Recommend]);
        foreach (var (name, score) in new[] { ("Ada", 6), ("Bo", 7), ("Cy", 8), ("Di", 9) })
        {
            await SendAsync(form, $$"""{"name":"{{name}}","recommend":"{{score}}"}""");
        }

        var scale = (await Summaries.SummarizeAsync(form, new AnswerRequest(Today, new EntryListFilter())))!.Fields.Single(summary => summary.Key == Recommend.Key);

        Assert.That(scale.Numbers!.Groups, Is.EqualTo(new AnswerScaleGroups(1, 2, 1, 0)), "6 is low, 7 and 8 the middle, 9 high");
    }

    [Test]
    public async Task AnAnswerOfSeveralValues_CountsOnce()
    {
        var form = await PublishAsync(name: "Several", fields: [NameField, Email]);
        await SendAsync(form, """{"name":"Ada","email":"ada@visitor.jfb.test"}""");

        // An answer of two values, as a field that takes several stores them: one answer.
        var row = (await FormRows.GetAsync(form))!;
        var id = (await FormRows.ListFieldsAsync(row.Id)).Single(each => each.Values.FieldKey == Email.Key).Id;
        _ = await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('b', 64),
            Culture = "en-US",
            Values = [new NewEntryValue(id, "bo@visitor.jfb.test"), new NewEntryValue(id, "office@visitor.jfb.test", ValueIndex: 1)],
        });

        var email = (await Summaries.SummarizeAsync(form, new AnswerRequest(Today, new EntryListFilter())))!.Fields.Single(summary => summary.Key == Email.Key);

        Assert.That(email.Answered, Is.EqualTo(2), "each entry's answer once, whatever it holds");
    }

    private static FieldOption Item(string value, string label) => new() { Value = value, Label = new LocalizedText(label) };

    // Sends the values to the form, as a visitor's page does; with the honeypot filled, the entry goes to Spam.
    private async Task SendAsync(Guid form, string values, int revision = 1, bool honeypot = false)
    {
        var body = $$"""{"revision":{{revision}},"values":{{values}}{{(honeypot ? ",\"honeypot\":\"Buy now\"" : string.Empty)}}}""";
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
    }

    // A test entry with one text answer, as the preview stores one.
    private async Task TestEntryAsync(Guid form, Guid field, string text)
    {
        var row = (await FormRows.GetAsync(form))!;
        var id = (await FormRows.ListFieldsAsync(row.Id)).Single(each => each.Values.FieldKey == field).Id;
        _ = await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            IsTest = true,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            Values = [new NewEntryValue(id, text)],
        });
    }
}
