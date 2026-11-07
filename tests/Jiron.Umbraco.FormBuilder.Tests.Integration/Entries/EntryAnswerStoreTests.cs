using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Signatures;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Entries;

/// <summary>
/// An entry stored with its answers and read back typed (ADR-0070), on a form the form service published with a field
/// of every built-in type that holds a value: its uploads taken, its consent wording stored once, every value back as
/// its type read it; and an entry refused whole, its wording and uploads included.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class EntryAnswerStoreTests : RepositoryTestBase
{
    private const string Culture = "en-GB";

    private static readonly FormField FullName = Field(1, "text", "fullName");
    private static readonly FormField Message = Field(2, "textarea", "message");
    private static readonly FormField Email = Field(3, "email", "email");
    private static readonly FormField Phone = Field(4, "phone", "phone");
    private static readonly FormField Website = Field(5, "url", "website");
    private static readonly FormField Age = Field(6, "number", "age");
    private static readonly FormField Stars = Field(7, "rating", "stars");
    private static readonly FormField Score = Field(8, "scale", "score");
    private static readonly FormField Volume = Field(9, "slider", "volume");
    private static readonly FormField Total = Field(10, "calculation", "total", """{"formula":"age * 2"}""");
    private static readonly FormField Birthday = Field(11, "date", "birthday");
    private static readonly FormField Arrival = Field(12, "time", "arrival");
    private static readonly FormField Visit = Field(13, "datetime", "visit");
    private static readonly FormField Toppings = Field(14, "dropdown", "toppings", """{"multiple":true}""") with { Options = Options("ham", "olives", "pineapple") };
    private static readonly FormField Size = Field(15, "radio", "size", """{"other":true}""") with { Options = Options("s", "m", "l") };
    private static readonly FormField Fruit = Field(16, "checkboxes", "fruit", """{"other":true}""") with { Options = Options("a", "b", "c") };
    private static readonly GridRow Speed = new() { Key = Key(18), Label = new LocalizedText("Speed") };
    private static readonly GridRow Price = new() { Key = Key(19), Label = new LocalizedText("Price") };
    private static readonly FormField Satisfaction = Field(17, "grid", "satisfaction") with { Options = Options("good", "bad"), Rows = [Speed, Price] };
    private static readonly GridRow Mail = new() { Key = Key(21), Label = new LocalizedText("Mail") };
    private static readonly GridRow Call = new() { Key = Key(22), Label = new LocalizedText("Call") };
    private static readonly FormField Channels = Field(20, "grid", "channels", """{"multiple":true}""") with { Options = Options("weekly", "monthly", "never"), Rows = [Mail, Call] };
    private static readonly FormField Priorities = Field(23, "ranking", "priorities") with { Options = Options("a", "b", "c") };
    private static readonly FormField Newsletter = Field(24, "checkbox", "newsletter");
    private static readonly FormField Returning = Field(25, "yesno", "returning");
    private static readonly FormField Terms = Field(26, "consent", "terms", """{"text":{"":"I agree to the terms of {{ form.name }}."}}""");
    private static readonly FormField Country = Field(27, "country", "country");
    private static readonly FormField Name = Field(28, "name", "name");
    private static readonly FormField Address = Field(29, "address", "address");
    private static readonly FormField Attachments = Field(30, "file", "attachments", """{"maxFiles":5}""");
    private static readonly FormField Signature = Field(31, "signature", "signature");
    private static readonly FormField Campaign = Field(32, "hidden", "campaign");
    private static readonly FormField GuestName = Field(34, "text", "guestName");
    private static readonly FormField Ticket = Field(35, "file", "ticket");
    private static readonly FormField Guests = Field(33, "repeater", "guests") with { Fields = [GuestName, Ticket] };

    private IFormBuilderFormService Service => GetRequiredService<IFormBuilderFormService>();

    private EntryAnswerStore Store => GetRequiredService<EntryAnswerStore>();

    private ConsentWordings Wordings => GetRequiredService<ConsentWordings>();

    private FieldTypeRegistry Types => GetRequiredService<FieldTypeRegistry>();

    private EntryPanels Panels => GetRequiredService<EntryPanels>();

    private MergeTagAnswerTexts AnswerTexts => GetRequiredService<MergeTagAnswerTexts>();

    [Test]
    public async Task EveryType_IsStored_AndReadBackTyped()
    {
        var form = await PublishAsync();
        var first = await UploadAsync(form, Attachments, "plan.pdf");
        var second = await UploadAsync(form, Attachments, "budget.pdf");
        var drawn = await UploadAsync(form, Signature, "signature.png");
        var ticket = await UploadAsync(form, Ticket, "ticket.pdf");
        var wording = (await Wordings.ShownAsync(Terms, Scope(form.Definition)))!;
        EntryFieldAnswer[] answers =
        [
            Answer(FullName, "Ada Lovelace"),
            Answer(Message, "Line one" + (char)10 + "line two"),
            Answer(Email, "ada@example.org"),
            Answer(Phone, "+41 44 668 18 00"),
            Answer(Website, "https://example.org/"),
            Answer(Age, "123456789012345"),
            Answer(Stars, "4"),
            Answer(Score, "7"),
            Answer(Volume, "35"),

            // The server works out a calculation's result (5.4c), here one past the digits a stored number keeps.
            new EntryFieldAnswer(Total.Key, [new FieldValue("123456789012345678.25")]),
            Answer(Birthday, "1752-12-31"),
            Answer(Arrival, "09:30:15"),
            Answer(Visit, "2026-10-26T18:45"),
            Answer(Toppings, "ham", "olives"),
            Answer(Size, new FieldAnswers([], [Part("other", "Huge")])),
            Answer(Fruit, new FieldAnswers(["b", "a"], [Part("other", "Quince")])),
            Answer(Satisfaction, new FieldAnswers([], [Part(Price.Key.ToString("D"), "bad"), Part(Speed.Key.ToString("D"), "good")])),
            Answer(Channels, new FieldAnswers([], [Part(Call.Key.ToString("D"), "never"), Part(Mail.Key.ToString("D"), "weekly", "monthly")])),
            Answer(Priorities, new FieldAnswers([], [Part("a", "10"), Part("b", "2"), Part("c", "7")])),
            Answer(Newsletter, "true"),
            Answer(Returning, "no"),
            Answer(Terms, new FieldAnswers(["true"], [Part("wording", wording.Hash)])),
            Answer(Country, "CH"),
            Answer(Name, new FieldAnswers([], [Part("last", "Lovelace"), Part("first", "Ada")])),
            Answer(Address, new FieldAnswers([], [Part("street", "Main Street 1"), Part("postcode", "3000"), Part("city", "Bern")])),
            Answer(Attachments, Token(first), Token(second)) with { Files = [first, second] },
            Answer(Signature, Token(drawn)) with { Files = [drawn] },
            Answer(Campaign, "spring"),
            Answer(GuestName, new FieldAnswers(["Ada"])),
            Answer(Ticket, new FieldAnswers([Token(ticket)])) with { Files = [ticket] },
            Answer(GuestName, new FieldAnswers(["Grace"]), row: 1),
        ];

        var entry = (await Store.CreateAsync(NewEntry(form), form.Definition, answers, new Dictionary<Guid, ConsentWording> { [Terms.Key] = wording }))!;
        var read = (await Store.ReadAsync(entry.Id))!;
        var columns = (await Entries.ListValuesAsync([entry.Id])).ToDictionary(value => (value.FieldId, value.RowIndex, value.ValueIndex));
        var catalogue = (await Forms.ListFieldsAsync(form.Id)).ToDictionary(field => field.Values.FieldKey, field => field.Id);
        var taken = await Entries.ListFilesAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(Text(read, FullName), Is.EqualTo("Ada Lovelace"));
            Assert.That(Text(read, Message), Is.EqualTo("Line one" + (char)10 + "line two"));
            Assert.That(new[] { Text(read, Email), Text(read, Phone), Text(read, Website), Text(read, Returning), Text(read, Country), Text(read, Campaign) }, Is.EqualTo(new[] { "ada@example.org", "+41 44 668 18 00", "https://example.org/", "no", "CH", "spring" }));
            Assert.That(One(read, Age).Number, Is.EqualTo(123456789012345m));
            Assert.That(new[] { One(read, Stars).Number, One(read, Score).Number, One(read, Volume).Number }, Is.EqualTo(new decimal?[] { 4, 7, 35 }));
            Assert.That((One(read, Total).Number, One(read, Total).Text), Is.EqualTo(((decimal?)null, "123456789012345678.25")), "never rounded");
            Assert.That(columns[(catalogue[Total.Key], 0, 0)].Number, Is.Null, "no rounded column either");
            Assert.That(One(read, Birthday).Date, Is.EqualTo(new DateOnly(1752, 12, 31)));
            Assert.That(columns[(catalogue[Birthday.Key], 0, 0)].Date, Is.Null, "before the dates both databases keep");
            Assert.That(One(read, Arrival).Time, Is.EqualTo(new TimeOnly(9, 30, 15)));
            Assert.That(One(read, Visit).DateTime, Is.EqualTo(new DateTime(2026, 10, 26, 18, 45, 0)));
            Assert.That(columns[(catalogue[Visit.Key], 0, 0)].Date, Is.EqualTo(new DateTime(2026, 10, 26, 18, 45, 0)));
            Assert.That(Texts(read, Toppings), Is.EqualTo(new[] { "ham", "olives" }));
            Assert.That(Texts(read, Size), Is.EqualTo(new[] { "Huge" }), "an Other text alone");
            Assert.That(Texts(read, Fruit), Is.EqualTo(new[] { "b", "a", "Quince" }), "an Other text after the options");
            Assert.That(read.Find(Satisfaction.Key)!.Values.Select(value => value.Value), Is.EqualTo(new[] { new FieldValue("good", Speed.Key), new FieldValue("bad", Price.Key) }), "a grid's rows");
            Assert.That(read.Find(Channels.Key)!.Values.Select(value => value.Value), Is.EqualTo(new[] { new FieldValue("weekly", Mail.Key), new FieldValue("monthly", Mail.Key), new FieldValue("never", Call.Key) }));
            Assert.That(columns.Keys.Select(key => key.FieldId), Has.None.EqualTo(catalogue[Satisfaction.Key]).And.None.EqualTo(catalogue[Guests.Key]), "a grid and a repeatable group store nothing of their own");
            Assert.That(Texts(read, Priorities), Is.EqualTo(new[] { "b", "c", "a" }), "a ranking's positions as its order");
            Assert.That(One(read, Newsletter).Bool, Is.True);
            Assert.That(One(read, Name).Parts, Is.EqualTo(new[] { KeyValuePair.Create("first", "Ada"), KeyValuePair.Create("last", "Lovelace") }));
            Assert.That(One(read, Address).Parts!.Select(part => part.Value), Is.EqualTo(new[] { "Main Street 1", "3000", "Bern" }));
            Assert.That(read.Find(Attachments.Key)!.Values.Select(value => value.File!.OriginalName), Is.EqualTo(new[] { "plan.pdf", "budget.pdf" }));
            Assert.That(One(read, Signature).File!.EntryId, Is.EqualTo(entry.Id));
            Assert.That(taken.Select(file => file.Id), Is.EquivalentTo(new[] { first.Id, second.Id, drawn.Id, ticket.Id }), "the entry took its uploads");
            Assert.That(One(read, Terms).Bool, Is.True);
            Assert.That((One(read, Terms).Consent!.Culture, One(read, Terms).Consent!.Text), Is.EqualTo((Culture, "I agree to the terms of Every type.")), "the wording as shown");
            Assert.That(await ConsentTexts.GetAsync(wording.Hash), Is.Not.Null);
            Assert.That(Texts(read, GuestName, 0), Is.EqualTo(new[] { "Ada" }));
            Assert.That(Texts(read, GuestName, 1), Is.EqualTo(new[] { "Grace" }));
            Assert.That(read.Find(Ticket.Key, 0)!.Values.Single().File!.OriginalName, Is.EqualTo("ticket.pdf"));
            Assert.That(read.Rows(Guests.Key), Is.EqualTo(new[] { 0, 1 }), "a repeatable group's rows");

            var tags = read.ForMergeTags();
            foreach (var answer in answers)
            {
                Assert.That(tags.Find(answer.Field, answer.Row)!.Values, Is.EqualTo(answer.Values), $"{answer.Field} reads back as its type read it");
            }
        });
    }

    [Test]
    public async Task ThePanel_ShowsEveryType_AsTheStoreReadsIt_AndATypedSignatureWithItsName()
    {
        var form = await PublishAsync();
        var plan = await UploadAsync(form, Attachments, "plan.pdf");
        var typed = await UploadAsync(form, Signature, "signature.png");
        await using (var png = new MemoryStream(TypedSignature.Draw("Ada Lovelace", 400, 150)))
        {
            await GetRequiredService<IFormBuilderFileStore>().SaveAsync(typed.StoredName, png);
        }

        var wording = (await Wordings.ShownAsync(Terms, Scope(form.Definition)))!;
        EntryFieldAnswer[] answers =
        [
            Answer(FullName, "Ada Lovelace"),
            Answer(Message, "Line one" + (char)10 + "line two"),
            Answer(Email, "ada@example.org"),
            Answer(Age, "42"),
            Answer(Birthday, "1990-03-14"),
            Answer(Toppings, "ham", "olives"),
            Answer(Fruit, new FieldAnswers(["b", "a"], [Part("other", "Quince")])),
            Answer(Satisfaction, new FieldAnswers([], [Part(Price.Key.ToString("D"), "bad"), Part(Speed.Key.ToString("D"), "good")])),
            Answer(Priorities, new FieldAnswers([], [Part("a", "10"), Part("b", "2"), Part("c", "7")])),
            Answer(Newsletter, "true"),
            Answer(Terms, new FieldAnswers(["true"], [Part("wording", wording.Hash)])),
            Answer(Name, new FieldAnswers([], [Part("last", "Lovelace"), Part("first", "Ada")])),
            Answer(Attachments, Token(plan)) with { Files = [plan] },
            Answer(Signature, Token(typed)) with { Files = [typed] },
            Answer(GuestName, new FieldAnswers(["Ada"])),
            Answer(GuestName, new FieldAnswers(["Grace"]), row: 1),
        ];
        var entry = (await Store.CreateAsync(NewEntry(form), form.Definition, answers, new Dictionary<Guid, ConsentWording> { [Terms.Key] = wording }))!;

        var panel = (await Panels.OpenAsync(form.Key, entry.Key, Editor, mayEdit: true))!;
        var shown = panel.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Answers).ToDictionary(answer => answer.Field);
        var read = (await Store.ReadAsync(entry.Id))!.ForMergeTags();
        var scope = Scope(form.Definition) with { Answers = read };
        var texts = await AnswerTexts.TextsAsync(scope, form.Definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Select(field => new AnswerField(field, null)));

        Assert.Multiple(() =>
        {
            foreach (var answer in answers.Where(answer => answer.Row == 0 && shown.ContainsKey(answer.Field) && answer.Field != Attachments.Key && answer.Field != Signature.Key))
            {
                var field = form.Definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Single(item => item.Key == answer.Field);
                var stored = AnswerTexts.Texts(field, read.Find(answer.Field)!, Culture, listing: true, texts).Select(text => text.Text);
                Assert.That(shown[answer.Field].Texts, Is.EqualTo(new[] { string.Join(", ", stored) }), $"{field.Alias} shows each value as its type read it");
            }

            Assert.That(shown[FullName.Key].Texts, Is.EqualTo(new[] { "Ada Lovelace" }));
            Assert.That(shown[Attachments.Key].Files.Select(found => (found.Key, found.Name, found.Size)), Is.EqualTo(new[] { (plan.Key, "plan.pdf", 1234L) }));
            Assert.That(shown[Signature.Key].SignedName, Is.EqualTo("Ada Lovelace"), "the name a typed signature was signed with");
            Assert.That(shown[Terms.Key].Consent, Is.EqualTo("I agree to the terms of Every type."), "the exact wording the visitor saw");
            Assert.That(shown[Guests.Key].Texts, Has.Count.EqualTo(2), "a repeatable group's rows");
            Assert.That(shown[Website.Key].Texts, Is.Empty, "an unanswered field shows none");
            Assert.That(panel.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Answers).Select(answer => answer.Field), Does.Not.Contain(GuestName.Key), "a repeatable group's child shows in its rows");
            Assert.That(shown[FullName.Key].Edit, Is.EqualTo(new EntryPanelEdit("text", ["Ada Lovelace"], [], false)).Using<EntryPanelEdit>(SameEdit));
            Assert.That(shown[Message.Key].Edit!.Kind, Is.EqualTo("longText"));
            Assert.That(shown[Toppings.Key].Edit!.Options.Select(option => option.Value), Is.EqualTo(new[] { "ham", "olives", "pineapple" }));
            Assert.That(new[] { Terms, Signature, Satisfaction, Priorities, Total, Attachments, Name, Guests }.Select(field => shown[field.Key].Edit), Is.All.Null, "consents, files, grids, rankings, calculations, parts and rows stay as they are");
        });
    }

    private static bool SameEdit(EntryPanelEdit left, EntryPanelEdit right) =>
        left.Kind == right.Kind && left.Values.SequenceEqual(right.Values) && left.Options.SequenceEqual(right.Options) && left.Required == right.Required;

    [Test]
    public async Task AWording_IsStoredOnce_ForEveryEntryThatAgreedToIt()
    {
        var form = await PublishAsync();
        var wording = (await Wordings.ShownAsync(Terms, Scope(form.Definition)))!;
        var wordings = new Dictionary<Guid, ConsentWording> { [Terms.Key] = wording };
        EntryFieldAnswer[] ticked = [Answer(Terms, new FieldAnswers(["true"], [Part("wording", wording.Hash)]))];

        await Store.CreateAsync(NewEntry(form), form.Definition, ticked, wordings);
        var second = (await Store.CreateAsync(NewEntry(form), form.Definition, ticked, wordings))!;

        Assert.Multiple(async () =>
        {
            Assert.That(Count(FormBuilderTables.ConsentText), Is.EqualTo(1));
            Assert.That((await Store.ReadAsync(second.Id))!.Find(Terms.Key)!.Values.Single().Consent!.Hash, Is.EqualTo(wording.Hash));
        });
    }

    [Test]
    public async Task ARefusedEntry_StoresNothing_ItsWordingAndUploadsIncluded()
    {
        var form = await PublishAsync();
        var upload = await UploadAsync(form, Attachments, "plan.pdf");
        var stored = await Store.CreateAsync(NewEntry(form), form.Definition, [Answer(Attachments, Token(upload)) with { Files = [upload] }], new Dictionary<Guid, ConsentWording>());
        var wording = (await Wordings.ShownAsync(Terms, Scope(form.Definition) with { Culture = "de-CH" }))!;
        var other = await UploadAsync(form, Attachments, "other.pdf");

        // The upload the first entry took, as a pipeline that read it before that entry would hand it over.
        AssertRefused(
            () => Store.CreateAsync(
                NewEntry(form),
                form.Definition,
                [Answer(Terms, new FieldAnswers(["true"], [Part("wording", wording.Hash)])), Answer(Attachments, Token(other), Token(upload)) with { Files = [other, upload] }],
                new Dictionary<Guid, ConsentWording> { [Terms.Key] = wording }),
            FormBuilderConflict.MissingFile);

        Assert.Multiple(async () =>
        {
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(1));
            Assert.That(Count(FormBuilderTables.ConsentText), Is.Zero, "the wording went with the entry");
            Assert.That((await Entries.GetFileAsync(other.Key))!.EntryId, Is.Null, "the other upload still waits");
            Assert.That((await Entries.GetFileAsync(upload.Key))!.EntryId, Is.EqualTo(stored!.Id));
        });
    }

    [Test]
    public async Task AnEntry_ComesWithoutValuesOrUploads_ForItsAnswersGiveThem()
    {
        var form = await PublishAsync();
        var catalogue = await Forms.ListFieldsAsync(form.Id);

        Assert.Multiple(() =>
        {
            Assert.That(async () => await Store.CreateAsync(NewEntry(form) with { Values = [new NewEntryValue(catalogue[0].Id, "Ada")] }, form.Definition, [], new Dictionary<Guid, ConsentWording>()), Throws.ArgumentException);
            Assert.That(async () => await Store.CreateAsync(NewEntry(form) with { FileIds = [1] }, form.Definition, [], new Dictionary<Guid, ConsentWording>()), Throws.ArgumentException);
            Assert.That(Count(FormBuilderTables.Entry), Is.Zero);
        });
    }

    [Test]
    public async Task AFormOrEntryThatIsGone_GivesNull()
    {
        var form = await PublishAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await Store.CreateAsync(NewEntry(form) with { FormId = form.Id + 100 }, form.Definition, [], new Dictionary<Guid, ConsentWording>()), Is.Null);
            Assert.That(await Store.ReadAsync(12345), Is.Null);
            Assert.That(await Store.CreateAsync(NewEntry(form), form.Definition, [], new Dictionary<Guid, ConsentWording>()), Is.Not.Null, "a form whose answers are all empty");
        });
    }

    private static Guid Key(int number) => Guid.Parse($"70000000-0000-4000-8000-{number:D12}");

    private static FormField Field(int key, string type, string alias, string? settings = null) => new()
    {
        Key = Key(key),
        Type = type,
        Alias = alias,
        Label = new LocalizedText(alias),
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    private static FieldOptions Options(params string[] values) => new() { Items = [.. values.Select(value => new FieldOption { Value = value })] };

    private static KeyValuePair<string, IEnumerable<string>> Part(string name, params string[] texts) => new(name, texts);

    // An upload answers with its key (ADR-0006).
    private static string Token(StoredEntryFile file) => file.Key.ToString("D");

    private static NewEntry NewEntry(PublishedForm form) => NewEntry(form.Id, form.VersionId) with { Culture = Culture };

    private static MergeTagScope Scope(FormDefinition definition) => new() { Form = definition, FormName = "Every type", FormAlias = "everyType", Culture = Culture };

    private static EntryAnswerValue One(EntryAnswers answers, FormField field) => answers.Find(field.Key)!.Values.Single();

    private static string Text(EntryAnswers answers, FormField field) => One(answers, field).Text;

    private static string[] Texts(EntryAnswers answers, FormField field, int row = 0) => [.. answers.Find(field.Key, row)!.Values.Select(value => value.Text)];

    private EntryFieldAnswer Answer(FormField field, params string[] texts) => Answer(field, new FieldAnswers(texts));

    private EntryFieldAnswer Answer(FormField field, FieldAnswers posted, int row = 0) =>
        new(field.Key, FieldValues.Read(Types.FindType(field.Type)!, field, posted).Values, row);

    private async Task<StoredEntryFile> UploadAsync(PublishedForm form, FormField field, string name) =>
        (await Entries.AddFileAsync(new NewEntryFile(Guid.NewGuid(), form.Id, field.Key, name, Hex(16), "application/pdf", 1234, Hex(32), "pending")))!;

    // The form named "Every type", published with a field of every type that holds a value.
    private async Task<PublishedForm> PublishAsync()
    {
        var created = await Service.CreateAsync(new NewFormRequest("Every type", UserKey: Editor));
        var draft = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var definition = WithFields(
            draft,
            FullName, Message, Email, Phone, Website, Age, Stars, Score, Volume, Total, Birthday, Arrival, Visit, Toppings, Size, Fruit,
            Satisfaction, Channels, Priorities, Newsletter, Returning, Terms, Country, Name, Address, Attachments, Signature, Campaign, Guests);
        var saved = (await Service.SaveDraftAsync(created.Key, created.ConcurrencyVersion, definition, null, Editor))!;
        await Service.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, Editor);
        var row = (await Forms.GetAsync(created.Key))!;
        return new PublishedForm(row.Id, row.PublishedVersionId!.Value, (await Service.GetDraftAsync(created.Key))!.Definition!, created.Key);
    }

    private sealed record PublishedForm(int Id, int VersionId, FormDefinition Definition, Guid Key);
}
