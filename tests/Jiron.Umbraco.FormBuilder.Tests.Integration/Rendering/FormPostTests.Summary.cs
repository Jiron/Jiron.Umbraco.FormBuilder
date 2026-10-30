using System.Net;
using AngleSharp.Dom;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

// The summary page without JavaScript (ROADMAP 6.5a, ADR-0122): the last page the rules show lists the answers of every
// page they show before it, page by page, as the outcome's copy writes them, each with a "Change" that opens its page
// at the field without a check and every answer kept; the send goes from there, and an earlier page's error after it
// shows in its row, linked from the error summary to the row's "Change".
public sealed partial class FormPostTests
{
    private const string SummaryPath = "/post-summary/";

    private static readonly string Bullets = new((char)0x2022, 3);

    private static readonly FormField Applicant = Field(K(0xa0), "applicant", "name") with { Required = true };
    private static readonly FormField Mail = Field(K(0xa1), "mail", "email") with { Required = true };
    private static readonly FormField Secret = Field(K(0xa2), "secret", "text") with { Sensitive = true };
    private static readonly FormField Mobile = Field(K(0xa3), "mobile", "text");
    private static readonly FormField Intro = new() { Key = K(0xa4), Type = "heading", Settings = SettingsObject.Parse("""{"text":{"":"Before you go on"}}""") };
    private static readonly FormField HiddenSource = Field(K(0xa5), "source", "hidden");
    private static readonly FormField Seats = Field(K(0xa6), "seats", "number");
    private static readonly FormField Total = Field(K(0xa7), "total", "calculation", """{"formula":"seats * 12"}""");
    private static readonly FormField Treat = Field(K(0xa8), "treat", "checkboxes") with { Options = Choices(("tea", "Tea"), ("cake", "Cake")) };
    private static readonly FormField Extra = Field(K(0xa9), "extra", "yesno");
    private static readonly FormField Reason = Field(K(0xaa), "why", "text") with { Rules = [WhenExtra(K(0xab))] };
    private static readonly FormField Person = Field(K(0xac), "person", "text") with { Required = true };
    private static readonly FormField Role = Field(K(0xad), "role", "dropdown") with { Options = Choices(("cook", "Cook"), ("host", "Host")) };
    private static readonly FormField Party = Field(K(0xae), "party", "repeater", """{"minRows":1,"maxRows":3}""") with { Fields = [Person, Role] };
    private static readonly FormField Plan = Field(K(0xaf), "plan", "file", """{"types":["pdf"],"maxBytes":2048}""");
    private static readonly FormField Notes = Field(K(0xbc), "notes", "textarea");
    private static readonly FormField Late = Field(K(0xb0), "late", "text");
    private static readonly FormField Agree = Field(K(0xb1), "agree", "consent", """{"text":{"":"<p>I agree to the terms.</p>"}}""") with { Required = true };
    private static readonly FormField Remarks = Field(K(0xb2), "remarks", "textarea");

    [Test]
    public async Task TheSummaryPage_ListsTheAnswersTheRulesShow_PageByPage_EachWithAChangeThatKeepsEveryAnswer_AndSendsFromThere()
    {
        var key = await PublishFormAsync("postSummary", SummaryForm);
        _ = await PublishFormAsync("postSingle", SingleForm);
        _ = await PagesAsync("post-summary");
        var document = await DocumentAsync(SummaryPath);
        var first = FormOf(document, "jfb-1");
        var single = FormOf(document, "jfb-2");
        Fill(first, "applicant.first", "Ada");
        Fill(first, "applicant.last", "Lovelace");
        Fill(first, "mail", "ada@example.com");
        Fill(first, "secret", "Blue door");
        var second = await FilesAsync(first, FormPostFields.Next);
        Fill(second, "seats", "3");
        Fill(second, "treat", "tea", "cake");
        Fill(second, "extra", "no");
        Fill(second, "party[0].person", "Bob");
        Fill(second, "party[0].role", "cook");
        Fill(second, "notes", "Bring" + Lf + "boots");
        var summary = await FilesAsync(second, FormPostFields.Next, ("plan", "plan.pdf", Bytes(Pdf, 1536)));
        var changing = await FilesAsync(summary, "goto:mail");
        Fill(changing, "mail", "ada@lovelace.example");
        var again = await FilesAsync(await FilesAsync(changing, FormPostFields.Next), FormPostFields.Next);
        Fill(again, "agree", "true");
        using var sent = await SubmitFilesAsync(again, FormPostFields.Submit);
        var catalogue = await CatalogueAsync(key);
        var stored = (await StoredAsync(key)).Single();
        var files = await Entries.ListFilesAsync((await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10)).Single());
        var sections = summary.QuerySelectorAll(".jfb-page:not([hidden]) > .jfb-summary > section").ToList();
        var mail = summary.QuerySelector("button[value='goto:mail']");

        Assert.Multiple(async () =>
        {
            Assert.That(first.QuerySelector(".jfb-summary"), Is.Null, "the first page shows no summary");
            Assert.That(single.QuerySelector(".jfb-summary"), Is.Null, "nor does a form whose rules show one page");
            Assert.That(Shown(summary), Is.EqualTo(K(0xb9)), "the last page the rules show");
            Assert.That(sections.Select(section => section.QuerySelector("h3")!.TextContent), Is.EqualTo(new[] { "About you", "Page 2" }), "a section for each page before it that the rules show, an untitled one numbered as the progress numbers it");
            Assert.That(sections.Select(section => section.GetAttribute("aria-labelledby")), Is.EqualTo(new[] { "jfb-1-answers-" + K(0xb3).ToString("N"), "jfb-1-answers-" + K(0xb5).ToString("N") }));
            Assert.That(DefinedTerms(sections[0]), Is.EqualTo(new[] { "Applicant", "Mail", "Secret", "Mobile" }), "the fields that take an answer, without the heading or the hidden field");
            Assert.That(Answer(sections[0], "Applicant"), Is.EqualTo("Ada Lovelace"));
            Assert.That(Answer(sections[0], "Mail"), Is.EqualTo("ada@example.com"));
            Assert.That(Row(sections[0], "Secret").QuerySelector("dd[data-jfb-masked] [aria-hidden='true']")?.TextContent, Is.EqualTo(Bullets), "a sensitive answer masked");
            Assert.That(Row(sections[0], "Secret").QuerySelector("dd[data-jfb-masked] .jfb-visually-hidden")?.TextContent, Is.EqualTo("Hidden for your privacy"));
            Assert.That(summary.QuerySelector(".jfb-summary")!.TextContent, Does.Not.Contain("Blue door"));
            Assert.That(Row(sections[0], "Mobile").QuerySelector("dd[data-jfb-empty]")?.TextContent.Trim(), Is.EqualTo("Not answered"));
            Assert.That(DefinedTerms(sections[1]), Is.EqualTo(new[] { "Seats", "Total", "Treat", "Extra", "Party", "Plan", "Notes" }), "a field the rules hide is left out");
            Assert.That(Answer(sections[1], "Total"), Is.EqualTo("36.00"), "a calculation visitors see, with its value as the page shows it, to its two places");
            Assert.That(Row(sections[1], "Total").QuerySelector("button"), Is.Null, "and no Change, as nobody types it");
            Assert.That(Answer(sections[1], "Treat"), Is.EqualTo("Tea, Cake"), "options by their labels");
            Assert.That(Answer(sections[1], "Extra"), Is.EqualTo("No"));
            Assert.That(Row(sections[1], "Party").QuerySelectorAll("dd > ol > li").Select(row => row.TextContent.Trim()), Is.EqualTo(new[] { "Person: Bob; Role: Cook" }), "a repeatable group's rows as a list");
            Assert.That(Answer(sections[1], "Plan"), Is.EqualTo("plan.pdf"), "a file by its name");
            Assert.That(Row(sections[1], "Notes").QuerySelector("dd")!.InnerHtml.Trim(), Does.Match("^Bring\\s*<br>\\s*boots$"), "line breaks kept");
            Assert.That(summary.QuerySelector(".jfb-summary")!.TextContent, Does.Not.Contain("Late arrivals"), "no section for a page the rules hide");
            Assert.That(mail?.GetAttribute("formaction"), Is.EqualTo($"{SummaryPath}?jfb-post={key:D}.jfb-1#jfb-1-mail"), "the form's own action, at the field");
            Assert.That(mail?.Id, Is.EqualTo("jfb-1-change-mail"));
            Assert.That(mail?.TextContent, Is.EqualTo("Change Mail"), "the label after the word, for screen readers");
            Assert.That(mail?.QuerySelector(".jfb-visually-hidden")?.TextContent, Is.EqualTo(" Mail"));
            Assert.That(summary.QuerySelectorAll("button[value^='goto:']").Select(button => button.GetAttribute("value")), Is.EqualTo(new[]
            {
                "goto:applicant", "goto:mail", "goto:secret", "goto:mobile", "goto:seats", "goto:treat", "goto:extra", "goto:party", "goto:plan", "goto:notes",
            }));
            Assert.That(summary.QuerySelector("button[type='submit']")?.GetAttribute("tabindex"), Is.EqualTo("-1"), "the hidden Send comes first, so Enter sends");
            Assert.That(summary.QuerySelector("button[type='submit']")?.GetAttribute("value"), Is.EqualTo(FormPostFields.Submit));
            Assert.That(summary.QuerySelectorAll(".jfb-navigation button").Select(button => button.GetAttribute("value")), Is.EqualTo(new[] { FormPostFields.Submit, FormPostFields.Back }), "the send from the summary page");
            Assert.That(summary.QuerySelector("[name='agree']"), Is.Not.Null, "the page's own fields after the answers");
            Assert.That(Shown(changing), Is.EqualTo(K(0xb3)), "Change opens the field's page");
            Assert.That(changing.QuerySelector("[data-jfb-summary]"), Is.Null, "without a check");
            Assert.That(changing.QuerySelector(".jfb-summary"), Is.Null);
            Assert.That(Kept(changing, "plan"), Is.EqualTo(new[] { "Keep plan.pdf (1.5 KB)" }), "every answer kept, the file too");
            Assert.That(Value(changing, "seats"), Is.EqualTo("3"));
            Assert.That(Shown(again), Is.EqualTo(K(0xb9)), "on with Next, each page checked again");
            Assert.That(Answer(again.QuerySelectorAll(".jfb-summary > section")[0], "Mail"), Is.EqualTo("ada@lovelace.example"), "the changed answer");
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), await sent.Content.ReadAsStringAsync());
            Assert.That(stored.Where(value => value.Field == catalogue[Mail.Key] || value.Field == catalogue[Secret.Key] || value.Field == catalogue[Total.Key]).Select(value => value.Text),
                Is.EquivalentTo(new[] { "ada@lovelace.example", "Blue door", "36" }), "sent from the summary page with every page's answers");
            Assert.That(files.Select(file => file.OriginalName), Is.EqualTo(new[] { "plan.pdf" }), "the file kept through the change");
        });
    }

    [Test]
    public async Task AChange_OpensItsPageWithoutACheck_AndANameOfNoFieldShowsTheSummaryPageAgain()
    {
        _ = await PublishFormAsync("postSummary", SummaryForm);
        _ = await PublishFormAsync("postSingle", SingleForm);
        _ = await PagesAsync("post-summary");
        var summary = await SummaryPageAsync();
        var group = await FilesAsync(summary, "goto:party");
        var forged = (IElement)summary.Clone();
        forged.QuerySelector("button[value='goto:mail']")!.SetAttribute("value", "goto:nobody");
        var unknown = await FilesAsync(forged, "goto:nobody");

        Assert.Multiple(() =>
        {
            Assert.That(Shown(group), Is.EqualTo(K(0xb5)), "a repeatable group's page");
            Assert.That(group.QuerySelector("[data-jfb-summary]"), Is.Null, "the summary page's own required consent is not checked");
            Assert.That(Value(group, "party[0].person"), Is.EqualTo("Bob"));
            Assert.That(Shown(unknown), Is.EqualTo(K(0xb9)), "a name of no field");
            Assert.That(unknown.QuerySelector("[data-jfb-summary]"), Is.Null);
            Assert.That(unknown.QuerySelector(".jfb-summary"), Is.Not.Null);
        });
    }

    [Test]
    public async Task ASendFromTheSummaryPage_ThatFailsAtEarlierPagesFields_ShowsTheErrorsInTheirRows_LinkedToTheirChange()
    {
        var key = await PublishFormAsync("postSummary", SummaryForm);
        _ = await PublishFormAsync("postSingle", SingleForm);
        _ = await PagesAsync("post-summary");
        var summary = await SummaryPageAsync();

        // The earlier pages' controls are in the form, hidden: a visitor's tools, or an answer another entry took meanwhile.
        Fill(summary, "mail", "not an address");
        Fill(summary, "party[0].person", string.Empty);
        var refused = await FilesAsync(summary, FormPostFields.Submit);
        var sections = refused.QuerySelectorAll(".jfb-summary > section").ToList();
        var mailError = Row(sections[0], "Mail").QuerySelector("dd > p.jfb-error");
        var partyError = Row(sections[1], "Party").QuerySelector("dd > p.jfb-error");

        Assert.Multiple(async () =>
        {
            Assert.That(Shown(refused), Is.EqualTo(K(0xb9)), "the summary page stays");
            Assert.That(refused.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-1-change-mail", "#jfb-1-change-party", "#jfb-1-agree" }),
                "the earlier pages' errors linked to their Change, the page's own to its field, in the form's order");
            Assert.That(mailError?.Id, Is.EqualTo("jfb-1-change-mail-error"));
            Assert.That(mailError?.TextContent, Is.EqualTo(refused.QuerySelector("#jfb-1-mail-error")?.TextContent).And.Not.Empty, "the field's own message in its row");
            Assert.That(refused.QuerySelector("#jfb-1-change-mail")?.GetAttribute("aria-describedby"), Is.EqualTo("jfb-1-change-mail-error"));
            Assert.That(Answer(sections[0], "Mail"), Does.StartWith("not an address"), "the answer as posted");
            Assert.That(partyError?.TextContent, Is.EqualTo(refused.QuerySelectorAll(".jfb-error-summary a")[1].TextContent).And.Not.Empty, "a row's error, as the summary names it");
            Assert.That(refused.QuerySelector("#jfb-1-agree-error")?.TextContent, Is.Not.Empty, "the page's own field shows its error at the field");
            Assert.That(refused.QuerySelector("#jfb-1-change-secret")?.HasAttribute("aria-describedby"), Is.False);
            Assert.That(await StoredAsync(key), Is.Empty);
        });
    }

    // The summary form: four pages, the third shown by a rule only, the last one the summary page with a consent and a
    // note of its own.
    private static FormDefinition SummaryForm(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with { Submission = definition.Settings.Submission with { SummaryPage = true } },
        Pages =
        [
            new FormPage { Key = K(0xb3), Title = new LocalizedText("About you"), Groups = [new FormGroup { Key = K(0xb4), Fields = [Intro, Applicant, Mail, Secret, Mobile, HiddenSource] }] },
            new FormPage { Key = K(0xb5), Groups = [new FormGroup { Key = K(0xb6), Fields = [Seats, Total, Treat, Extra, Reason, Party, Plan, Notes] }] },
            new FormPage { Key = K(0xb7), Title = new LocalizedText("Late arrivals"), Rules = [WhenExtra(K(0xb8))], Groups = [new FormGroup { Key = K(0xbd), Fields = [Late] }] },
            new FormPage { Key = K(0xb9), Title = new LocalizedText("Check your answers"), Groups = [new FormGroup { Key = K(0xba), Fields = [Agree, Remarks] }] },
        ],
    };

    // A form of one page with the summary page on.
    private static FormDefinition SingleForm(FormDefinition definition) => WithFields(definition, Field(K(0xbb), "word", "text")) with
    {
        Settings = definition.Settings with { Submission = definition.Settings.Submission with { SummaryPage = true } },
    };

    private static FormRule WhenExtra(Guid key) => new()
    {
        Key = key,
        Effect = RuleEffect.Show,
        Conditions = [new RuleCondition { Field = Extra.Key, Operator = "equals", Value = new ConditionOperand.Text("yes") }],
    };

    private static FieldOptions Choices(params (string Value, string Label)[] options) =>
        new() { Items = [.. options.Select(option => new FieldOption { Value = option.Value, Label = new LocalizedText(option.Label) })] };

    // The summary page of the summary form, its first two pages answered.
    private async Task<IElement> SummaryPageAsync()
    {
        var first = FormOf(await DocumentAsync(SummaryPath), "jfb-1");
        Fill(first, "applicant.first", "Ada");
        Fill(first, "applicant.last", "Lovelace");
        Fill(first, "mail", "ada@example.com");
        var second = await FilesAsync(first, FormPostFields.Next);
        Fill(second, "party[0].person", "Bob");
        var summary = await FilesAsync(second, FormPostFields.Next);
        Assert.That(Shown(summary), Is.EqualTo(K(0xb9)), "the summary page");
        return summary;
    }

    // A section's terms, in order.
    private static string[] DefinedTerms(IElement section) => [.. section.QuerySelectorAll("dl > div > dt").Select(term => term.TextContent)];

    // The row of a term.
    private static IElement Row(IElement section, string term) =>
        section.QuerySelectorAll("dl > div").SingleOrDefault(row => row.QuerySelector("dt")!.TextContent == term) ?? throw new AssertionException($"no row {term}");

    // The answer of a term's row, its whitespace as the page reads it.
    private static string Answer(IElement section, string term) =>
        string.Join(' ', Row(section, term).QuerySelector("dd")!.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
