using System.Net;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

// The copy of the answers without JavaScript (ROADMAP 6.5b, ADR-0123): below a message outcome that offers it, the page
// after the post shows the entry's answers once, page by page as the rules showed them, as the summary page writes them
// without "Change", with the print button for the runtime and a link to the form again; any later view shows a line in
// their place, a HEAD takes nothing, two views at once show them once, an expired token shows the form, the page is
// never stored, and an outcome without the copy shows none.
public sealed partial class FormPostTests
{
    private const string CopyPath = "/post-copy/";

    [Test]
    public async Task TheCopyOfTheAnswers_ShowsOnceBelowTheOutcome_PageByPageAsTheRulesShowedThem_ThenALineInItsPlace()
    {
        var key = await PublishFormAsync("postCopy", CopyForm);
        _ = await PublishFormAsync("postNoCopy", NoCopyForm);
        _ = await PagesAsync("post-copy");
        var location = await SentCopyAsync();
        var (cache, first) = await PageAndHtmlAsync(location);
        var (laterCache, later) = await PageAndHtmlAsync(location);
        var sections = first.QuerySelectorAll("#jfb-1 > .jfb-summary > section").ToList();
        var print = first.QuerySelector("#jfb-1 > [data-jfb-copy] > button[data-jfb-print]");
        var again = first.QuerySelector("#jfb-1 > [data-jfb-copy] > a");
        var entryId = (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10)).Single();
        var submitted = (await GetRequiredService<IEntryEventRepository>().ListForEntryAsync(entryId)).Single(item => item.Kind == SubmissionEntries.SubmittedEvent);
        using var details = JsonDocument.Parse(submitted.Details);
        var hidden = details.RootElement.GetProperty("hidden").EnumerateArray().Select(item => item.GetString()).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(first.QuerySelector("#jfb-1 > [data-jfb-outcome]")?.TextContent, Does.Contain("Thank you, Ada."), "the outcome first, which keeps the focus");
            Assert.That(first.QuerySelector("#jfb-1 > [data-jfb-outcome] + .jfb-summary"), Is.Not.Null, "the copy right after it");
            Assert.That(sections.Select(section => section.QuerySelector("h3")!.TextContent), Is.EqualTo(new[] { "About you", "Page 2", "Last look, Ada" }),
                "a section for each page the rules showed, an untitled one numbered, a title's merge tag rendered from the entry's answers");
            Assert.That(sections.Select(section => section.GetAttribute("aria-labelledby")), Is.EqualTo(new[] { "jfb-1-copy-" + K(0xb3).ToString("N"), "jfb-1-copy-" + K(0xb5).ToString("N"), "jfb-1-copy-" + K(0xb9).ToString("N") }));
            Assert.That(DefinedTerms(sections[0]), Is.EqualTo(new[] { "Applicant", "Mail", "Secret", "Mobile" }), "the fields that take an answer, without the heading or the hidden field");
            Assert.That(Answer(sections[0], "Applicant"), Is.EqualTo("Ada Lovelace"));
            Assert.That(Answer(sections[0], "Mail"), Is.EqualTo("ada@example.com"));
            Assert.That(Row(sections[0], "Secret").QuerySelector("dd[data-jfb-masked] [aria-hidden='true']")?.TextContent, Is.EqualTo(Bullets), "a sensitive answer masked");
            Assert.That(Row(sections[0], "Secret").QuerySelector("dd[data-jfb-masked] .jfb-visually-hidden")?.TextContent, Is.EqualTo("Hidden for your privacy"));
            Assert.That(first.Body!.TextContent, Does.Not.Contain("Blue door"), "nowhere on the page");
            Assert.That(Row(sections[0], "Mobile").QuerySelector("dd[data-jfb-empty]")?.TextContent.Trim(), Is.EqualTo("Not answered"), "a question the rules showed and the visitor left");
            Assert.That(DefinedTerms(sections[1]), Is.EqualTo(new[] { "Seats", "Total", "Treat", "Extra", "Party", "Plan", "Notes" }), "a field the rules hid is left out");
            Assert.That(Answer(sections[1], "Total"), Is.EqualTo("36.00"), "a calculation visitors see, as stored");
            Assert.That(Answer(sections[1], "Treat"), Is.EqualTo("Tea, Cake"), "options by their labels");
            Assert.That(Answer(sections[1], "Extra"), Is.EqualTo("No"));
            Assert.That(Row(sections[1], "Party").QuerySelectorAll("dd > ol > li").Select(row => row.TextContent.Trim()), Is.EqualTo(new[] { "Person: Bob; Role: Cook" }), "a repeatable group's rows as a list");
            Assert.That(Answer(sections[1], "Plan"), Does.StartWith("plan.pdf"), "a file by its name");
            Assert.That(Row(sections[1], "Notes").QuerySelector("dd")!.InnerHtml.Trim(), Does.Match("^Bring\\s*<br>\\s*boots$"), "line breaks kept");
            Assert.That(DefinedTerms(sections[2]), Is.EqualTo(new[] { "Agree", "Remarks" }), "the last page's own fields too");
            Assert.That(Row(sections[2], "Remarks").QuerySelector("dd[data-jfb-empty]"), Is.Not.Null);
            Assert.That(first.QuerySelector("#jfb-1 > .jfb-summary")!.TextContent, Does.Not.Contain("Late arrivals"), "no section for a page the rules hid");
            Assert.That(first.QuerySelectorAll("#jfb-1 .jfb-summary button, #jfb-1 .jfb-summary [data-jfb-error]"), Is.Empty, "no Change and no error: the entry is sent");
            Assert.That(print?.GetAttribute("type"), Is.EqualTo("button"));
            Assert.That(print?.HasAttribute("hidden"), Is.True, "hidden until the runtime reveals it");
            Assert.That(print?.TextContent, Is.EqualTo("Print your answers"));
            Assert.That(print?.QuerySelector(".jfb-icon-print")?.GetAttribute("aria-hidden"), Is.EqualTo("true"));
            Assert.That(again?.GetAttribute("href"), Is.EqualTo(CopyPath + "#jfb-1"), "the page without the token, at the form");
            Assert.That(again?.TextContent, Is.EqualTo("Fill in the form again"));
            Assert.That(again?.ClassName, Is.EqualTo("jfb-button jfb-button-link"));
            Assert.That(cache, Is.EqualTo("no-store"), "never stored (ADR-0104)");
            Assert.That(hidden, Does.Contain(Dashed(K(0xaa))).And.Contains(Dashed(K(0xb7))), "the submitted event records what the rules hid: the field and the page");
            Assert.That(details.RootElement.GetProperty("outcome").GetString(), Is.EqualTo(Dashed(K(0xc0))));
            Assert.That((await GetRequiredService<IKeyValueRepository>().ListAsync(SentCopies.Prefix)).Select(pair => pair.Key), Is.EqualTo(new[] { SentCopies.KeyOf(key, SubmissionOf(location, key)) }), "the entry's mark");
            Assert.That(later.QuerySelector("#jfb-1 > [data-jfb-outcome]")?.TextContent, Does.Contain("Thank you, Ada."), "a later view keeps the outcome (ADR-0100)");
            Assert.That(later.QuerySelector("#jfb-1 .jfb-summary"), Is.Null, "without the answers");
            Assert.That(later.QuerySelector("#jfb-1 > p.jfb-help[data-jfb-copy-shown]")?.TextContent, Is.EqualTo("Your answers show only once, right after sending."), "a line in their place");
            Assert.That(later.QuerySelector("#jfb-1 [data-jfb-print]"), Is.Null, "nothing to print");
            Assert.That(later.QuerySelector("#jfb-1 > [data-jfb-copy] > a")?.GetAttribute("href"), Is.EqualTo(CopyPath + "#jfb-1"), "the link stays");
            Assert.That(later.Body!.TextContent, Does.Not.Contain("ada@example.com"));
            Assert.That(laterCache, Is.EqualTo("no-store"));
        });
    }

    [Test]
    public async Task TheCopy_IsNotTakenByAHead_ShowsOnceToTwoViewsAtOnce_AndNotAtAllForAnExpiredTokenOrAnOutcomeWithoutIt()
    {
        var key = await PublishFormAsync("postCopy", CopyForm);
        _ = await PublishFormAsync("postNoCopy", NoCopyForm);
        _ = await PagesAsync("post-copy");
        var location = await SentCopyAsync();
        using var head = await SendAsync(HttpMethod.Head, location);
        var views = await Task.WhenAll(PageAndHtmlAsync(location), PageAndHtmlAsync(location));
        var expiring = await SentCopyAsync("Grace");
        _clock.Advance(SentTokens.Lifetime + TimeSpan.FromSeconds(1));
        var (expiredCache, expired) = await PageAndHtmlAsync(expiring);
        var fresh = await SentCopyAsync("Mary");
        var (_, freshPage) = await PageAndHtmlAsync(fresh);
        var plain = FormOf(await DocumentAsync(CopyPath), "jfb-2");
        Fill(plain, "word", "hello");
        using var sentPlain = await SubmitAsync(plain, FormPostFields.Submit, CopyPath);
        var (_, plainPage) = await PageAndHtmlAsync(sentPlain.Headers.Location!.OriginalString);

        Assert.Multiple(async () =>
        {
            Assert.That(head.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(views.Count(view => view.Document.QuerySelector("#jfb-1 > .jfb-summary") is not null), Is.EqualTo(1), "a HEAD took nothing, and of two views at once one shows the answers");
            Assert.That(views.Count(view => view.Document.QuerySelector("#jfb-1 > [data-jfb-copy-shown]") is not null), Is.EqualTo(1), "and the other the line");
            Assert.That(expired.QuerySelector("form#jfb-1"), Is.Not.Null, "an expired token shows the form as for any visitor");
            Assert.That(expired.QuerySelector("[data-jfb-outcome], .jfb-summary, [data-jfb-copy]"), Is.Null, "and nothing of the entry");
            Assert.That(expired.Body!.TextContent, Does.Not.Contain("Grace"));
            Assert.That(expiredCache, Is.EqualTo("no-store"));
            Assert.That(Answer(freshPage.QuerySelectorAll("#jfb-1 > .jfb-summary > section")[0], "Applicant"), Is.EqualTo("Mary Lovelace"), "a new entry's copy after the old one expired");
            Assert.That((await GetRequiredService<IKeyValueRepository>().ListAsync(SentCopies.Prefix)).Select(pair => pair.Key), Is.EqualTo(new[] { SentCopies.KeyOf(key, SubmissionOf(fresh, key)) }),
                "its mark, the marks older than a token's lifetime gone with it");
            Assert.That(plainPage.QuerySelector("#jfb-2 > [data-jfb-outcome]")?.TextContent, Does.Contain("Noted."), "an outcome without the copy");
            Assert.That(plainPage.QuerySelector("#jfb-2 .jfb-summary, #jfb-2 [data-jfb-copy], #jfb-2 [data-jfb-copy-shown]"), Is.Null, "shows none");
        });
    }

    // The copy form: the summary form's four pages, the last one titled with a merge tag, and a message that offers the
    // copy of the answers.
    private static FormDefinition CopyForm(FormDefinition definition)
    {
        var form = SummaryForm(definition);
        return form with
        {
            Pages = [.. form.Pages.Take(3), form.Pages[3] with { Title = new LocalizedText("Last look, {{ field.applicant.first }}") }],
            Outcomes = [new FormOutcome { Key = K(0xc0), Message = new LocalizedHtml("<p>Thank you, {{ field.applicant.first }}.</p>"), ShowAnswers = true }],
        };
    }

    // A form of one field whose message offers no copy.
    private static FormDefinition NoCopyForm(FormDefinition definition) => WithFields(definition, Field(K(0xc1), "word", "text")) with
    {
        Outcomes = [new FormOutcome { Key = K(0xc2), Message = new LocalizedHtml("<p>Noted.</p>") }],
    };

    // The copy form answered through its pages and sent; the page the post redirects to.
    private async Task<string> SentCopyAsync(string firstName = "Ada")
    {
        var first = FormOf(await DocumentAsync(CopyPath), "jfb-1");
        Fill(first, "applicant.first", firstName);
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
        var last = await FilesAsync(second, FormPostFields.Next, ("plan", "plan.pdf", Bytes(Pdf, 1536)));
        Fill(last, "agree", "true");
        using var sent = await SubmitFilesAsync(last, FormPostFields.Submit);
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), await sent.Content.ReadAsStringAsync());
        return sent.Headers.Location!.OriginalString;
    }

    // The submission id a sent page's token names.
    private Guid SubmissionOf(string location, Guid form)
    {
        var value = Uri.UnescapeDataString(location[(location.IndexOf(SentTokens.QueryName + "=", StringComparison.Ordinal) + SentTokens.QueryName.Length + 1)..location.IndexOf('#', StringComparison.Ordinal)]);
        return GetRequiredService<SentTokens>().Read(value, form, "jfb-1") ?? throw new AssertionException("the token names no submission");
    }

    private static string Dashed(Guid key) => key.ToString("D");
}
