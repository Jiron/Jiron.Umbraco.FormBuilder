using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The post without JavaScript on real pages (ROADMAP 6.2h, ADR-0004, ADR-0100), posted as a browser posts a form: a
/// valid post stored as the runtime's post to <c>entries</c> stores the same answers (a name's parts, a long text's line
/// breaks, a repeater's rows, a switch left off) and redirected to its outcome, an invalid one shown again with its
/// answers and errors, a three-page form moved through with Next and Back beside another form, a consent whose wording
/// changed unticked with a notice, each outcome, a sent token that names nothing, a form for members posted from another
/// origin, and the surface action's auto-route, every answer of the action <c>no-store</c>.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed partial class FormPostTests : RenderingTestBase
{
    private static readonly string Crlf = new([(char)13, (char)10]);
    private static readonly string Lf = ((char)10).ToString();

    private static readonly FormField FullName = Field(K(0x10), "fullName", "name");
    private static readonly FormField Story = Field(K(0x11), "story", "textarea");
    private static readonly FormField Updates = Field(K(0x12), "updates", "yesno", """{"style":"switch"}""");
    private static readonly FormField Terms = Field(K(0x13), "terms", "consent", """{"text":{"":"<p>I agree to the terms.</p>"}}""") with { Required = true };
    private static readonly FormField Guest = Field(K(0x15), "guest", "text");
    private static readonly FormField Vegan = Field(K(0x16), "vegan", "yesno", """{"style":"switch"}""");
    private static readonly FormField Guests = Field(K(0x14), "guests", "repeater", """{"minRows":2,"maxRows":3}""") with { Fields = [Guest, Vegan] };
    private static readonly FormField Email = Field(K(0x20), "email", "email") with { Required = true };
    private static readonly FormField Topic = Field(K(0x21), "topic", "text") with { Required = true };
    private static readonly FormField Note = Field(K(0x22), "note", "textarea");
    private static readonly FormField Way = Field(K(0x30), "way", "text");
    private static readonly Guid FirstRow = K(0x61);
    private static readonly Guid RoomRow = K(0x63);
    private static readonly FieldOptions Grades = new() { Items = [new FieldOption { Value = "poor", Label = new LocalizedText("Poor") }, new FieldOption { Value = "good", Label = new LocalizedText("Good") }] };
    private static readonly FormField Overall = Field(K(0x60), "overall", "grid") with
    {
        Options = Grades,
        Rows = [new GridRow { Key = FirstRow, Label = new LocalizedText("The welcome") }, new GridRow { Key = K(0x62), Label = new LocalizedText("The room"), Required = true }],
    };

    private static readonly FormField Visits = Field(K(0x64), "visits", "repeater", """{"minRows":2,"rowLabel":{"":"Visit"}}""") with
    {
        Fields =
        [
            Field(K(0x65), "who", "text") with { Required = true },
            Field(K(0x66), "rating", "grid") with { Options = Grades, Rows = [new GridRow { Key = RoomRow, Label = new LocalizedText("The room"), Required = true }] },
        ],
    };

    private TestClock _clock = null!;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [TearDown]
    public void ResetTheClock() => _clock?.Reset();

    [Test]
    public async Task AValidPost_IsStoredAsTheRuntimesPostStoresTheSameAnswers_AndLeadsToItsOutcome()
    {
        var key = await PublishFormAsync("postMain", MainForm);
        _ = await PublishFormAsync("postPaged", Paged);
        _ = await PagesAsync("post");
        var document = await DocumentAsync("/post/");
        var form = FormOf(document, "jfb-1");
        Fill(form, "fullName.first", "Ada");
        Fill(form, "fullName.last", "Lovelace");
        Fill(form, "story", "Line one" + Lf + "Line two" + Lf + Lf + "Line four");
        Fill(form, "terms", "true");
        Fill(form, "guests[0].guest", "Bob");
        Fill(form, "guests[0].vegan", "yes");
        Fill(form, "guests[1].guest", "Lin");
        var hash = form.QuerySelector("input[name='jfb-consent-terms']")!.GetAttribute("value")!;
        var posted = Posted(form, FormPostFields.Submit);

        using var sent = await SubmitAsync(form, FormPostFields.Submit);
        using var runtime = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent(new JsonObject
        {
            ["revision"] = 1,
            ["values"] = new JsonObject
            {
                ["fullName.first"] = "Ada",
                ["fullName.last"] = "Lovelace",
                ["story"] = "Line one" + Lf + "Line two" + Lf + Lf + "Line four",
                ["updates"] = "no",
                ["terms"] = true,
                ["guests"] = new JsonArray(new JsonObject { ["guest"] = "Bob", ["vegan"] = "yes" }, new JsonObject { ["guest"] = "Lin", ["vegan"] = "no" }),
            },
            ["consents"] = new JsonObject { ["terms"] = hash },
        }.ToJsonString(), Encoding.UTF8, "application/json"));
        var entries = await StoredAsync(key);
        var catalogue = await CatalogueAsync(key);
        var location = sent.Headers.Location?.OriginalString ?? string.Empty;
        var (outcome, outcomeHtml) = await PageAndHtmlAsync(location);

        Assert.Multiple(async () =>
        {
            Assert.That(posted.Single(pair => pair.Key == "story").Value, Does.Contain(Crlf), "the browser posts CRLF");
            Assert.That(posted.Any(pair => pair.Key == "updates"), Is.False, "a switch left off posts nothing");
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), await sent.Content.ReadAsStringAsync());
            Assert.That(Header(sent, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(location, Does.Match("^/post/[?]jfb-sent=jfb-1[.][A-Za-z0-9_-]+#jfb-1$"), "back to the page, at the form, with the token for its instance");
            Assert.That(runtime.StatusCode, Is.EqualTo(HttpStatusCode.OK), await runtime.Content.ReadAsStringAsync());
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0], Is.EquivalentTo(entries[1]), "the post without JavaScript stores what the runtime's post stores");
            Assert.That(entries[0].Where(value => value.Field == catalogue[Story.Key]).Select(value => value.Text), Is.EqualTo(new[] { "Line one" + Lf + "Line two" + Lf + Lf + "Line four" }), "LF only");
            Assert.That(entries[0].Count(value => value.Field == catalogue[Updates.Key]), Is.EqualTo(1), "the switch left off is stored, as no");
            Assert.That(entries[0].Where(value => value.Field == catalogue[Guest.Key]).OrderBy(value => value.Row).Select(value => value.Text), Is.EqualTo(new[] { "Bob", "Lin" }), "both rows");
            Assert.That(entries[0].Count(value => value.Field == catalogue[Vegan.Key]), Is.EqualTo(2), "each row's switch, the second one left off");
            Assert.That(outcome, Is.EqualTo("no-store"), "a page that shows an outcome");
            Assert.That(outcomeHtml.QuerySelector("#jfb-1[data-jfb-form] [data-jfb-outcome] p")!.TextContent, Is.EqualTo("Thank you, Ada."), "the outcome the receipt renders, recalling an answer that the address never holds");
            Assert.That(outcomeHtml.QuerySelector("#jfb-1 .jfb-reference")!.TextContent, Does.StartWith("Your reference is "));
            Assert.That(outcomeHtml.QuerySelector("#jfb-1 [data-jfb-continue]"), Is.Null, "a message alone");
            Assert.That(outcomeHtml.QuerySelector("form#jfb-2"), Is.Not.Null, "the page's other form, as for any visitor");
            Assert.That(location, Does.Not.Contain("Ada"));
        });
    }

    [Test]
    public async Task AnInvalidPost_ShowsThePageAgainWithItsAnswersAndErrors_AndStoresNothing()
    {
        var key = await PublishFormAsync("postMain", MainForm);
        _ = await PublishFormAsync("postPaged", Paged);
        _ = await PagesAsync("post");
        var form = FormOf(await DocumentAsync("/post/"), "jfb-1");
        Fill(form, "fullName.first", "Ada");
        Fill(form, "story", "First" + Lf + "Second");
        Fill(form, "guests[1].guest", "Lin");
        Fill(form, "guests[1].vegan", "yes");

        using var response = await SubmitAsync(form, FormPostFields.Submit);
        var again = await ParseAsync(response);
        var shown = FormOf(again, "jfb-1");
        var buttonless = await MoveAsync(FormOf(await DocumentAsync("/post/"), "jfb-1"), null, "jfb-1");

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Header(response, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(shown.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-1-terms" }), "the consent left unticked");
            Assert.That(shown.QuerySelector("[name='fullName.first']")!.GetAttribute("value"), Is.EqualTo("Ada"));
            Assert.That(shown.QuerySelector("textarea[name='story']")!.TextContent, Is.EqualTo("First" + Lf + "Second"));
            Assert.That(shown.QuerySelectorAll(".jfb-row"), Has.Length.EqualTo(2), "the rows the page showed");
            Assert.That(shown.QuerySelector("[name='guests[0].guest']")!.GetAttribute("value"), Is.Null, "the first row empty");
            Assert.That(shown.QuerySelector("[name='guests[1].guest']")!.GetAttribute("value"), Is.EqualTo("Lin"));
            Assert.That(shown.QuerySelector("[name='guests[1].vegan']")!.HasAttribute("checked"), Is.True, "a switch turned on stays on");
            Assert.That(shown.QuerySelector("[name='updates']")!.HasAttribute("checked"), Is.False, "a switch left off stays off");
            Assert.That(shown.GetAttribute("action"), Is.EqualTo("/post/#jfb-1"), "the page opens at the form");
            Assert.That(FormOf(again, "jfb-2").QuerySelector("[data-jfb-summary]"), Is.Null, "the other form as a first render");
            Assert.That(buttonless.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-1-terms" }), "a post without a button is sent as Send is");
            Assert.That(await StoredAsync(key), Is.Empty);
        });
    }

    [Test]
    public async Task AThreePageForm_BesideAnother_MovesWithNextAndBack_KeepingEveryAnswer_AndSendsFromItsLastPage()
    {
        _ = await PublishFormAsync("postMain", MainForm);
        var key = await PublishFormAsync("postPaged", Paged);
        _ = await PagesAsync("post");
        var first = FormOf(await DocumentAsync("/post/"), "jfb-2");

        var refused = await MoveAsync(first, FormPostFields.Next, "jfb-2");
        Fill(refused, "email", "ada@example.com");
        var second = await MoveAsync(refused, FormPostFields.Next, "jfb-2");
        var stays = await MoveAsync(second, FormPostFields.Next, "jfb-2");
        Fill(stays, "topic", "Gardens");
        var third = await MoveAsync(stays, FormPostFields.Next, "jfb-2");
        Fill(third, "note", "Bring" + Lf + "boots");
        var back = await MoveAsync(third, FormPostFields.Back, "jfb-2");
        var last = await MoveAsync(back, FormPostFields.Next, "jfb-2");
        Fill(last, "email", "not an address");
        var earlier = await MoveAsync(last, FormPostFields.Submit, "jfb-2");
        Fill(earlier, "email", "ada@example.com");
        var again = await MoveAsync(await MoveAsync(earlier, FormPostFields.Next, "jfb-2"), FormPostFields.Next, "jfb-2");
        using var sent = await SubmitAsync(again, FormPostFields.Submit);
        var location = sent.Headers.Location?.OriginalString ?? string.Empty;
        var (_, outcome) = await PageAndHtmlAsync(location);
        var catalogue = await CatalogueAsync(key);
        var stored = (await StoredAsync(key)).Single();

        Assert.Multiple(async () =>
        {
            Assert.That(Shown(first), Is.EqualTo(K(0x40)), "the first page");
            Assert.That(Shown(refused), Is.EqualTo(K(0x40)), "Next with an error stays");
            Assert.That(refused.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-2-email" }), "the page's own error alone");
            Assert.That(Shown(second), Is.EqualTo(K(0x41)), "the next page once it passes");
            Assert.That(second.QuerySelector("[data-jfb-summary]"), Is.Null);
            Assert.That(second.QuerySelector("[name='email']")!.GetAttribute("value"), Is.EqualTo("ada@example.com"), "the earlier page's answers are posted again");
            Assert.That(Shown(stays), Is.EqualTo(K(0x41)));
            Assert.That(stays.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-2-topic" }));
            Assert.That(Shown(third), Is.EqualTo(K(0x42)));
            Assert.That(third.QuerySelectorAll("button[name='jfb-nav']:not([tabindex])").Select(button => button.GetAttribute("value")), Is.EqualTo(new[] { "submit", "back" }), "Send on the last page, then Back");
            Assert.That(Shown(back), Is.EqualTo(K(0x41)), "Back without a check");
            Assert.That(back.QuerySelector("textarea[name='note']")!.TextContent, Is.EqualTo("Bring" + Lf + "boots"), "a later page's answers are kept too");
            Assert.That(Shown(last), Is.EqualTo(K(0x42)));
            Assert.That(Shown(earlier), Is.EqualTo(K(0x40)), "Send from the last page shows the first page that holds an error");
            Assert.That(earlier.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-2-email" }));
            Assert.That(earlier.QuerySelector("textarea[name='note']")!.TextContent, Is.EqualTo("Bring" + Lf + "boots"), "with the later pages' answers kept");
            Assert.That(Shown(again), Is.EqualTo(K(0x42)));
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), await sent.Content.ReadAsStringAsync());
            Assert.That(location, Does.Match("^/post/[?]jfb-sent=jfb-2[.][A-Za-z0-9_-]+#jfb-2$"));
            Assert.That(outcome.QuerySelector("#jfb-2 [data-jfb-outcome] p")!.TextContent, Is.EqualTo("Thank you. We have received your message."), "a form without an outcome of its own");
            Assert.That(outcome.QuerySelector("form#jfb-1"), Is.Not.Null, "the first form as for any visitor");
            Assert.That(stored.Where(value => value.Field == catalogue[Email.Key] || value.Field == catalogue[Topic.Key] || value.Field == catalogue[Note.Key]).Select(value => value.Text),
                Is.EquivalentTo(new[] { "ada@example.com", "Gardens", "Bring" + Lf + "boots" }), "every page's answers in one entry");
        });
    }

    [Test]
    public async Task APostWhoseConsentWordingChanged_ShowsTheFormAgainWithTheConsentUnticked_AndANotice()
    {
        var key = await PublishFormAsync("postMain", MainForm);
        _ = await PublishFormAsync("postPaged", Paged);
        _ = await PagesAsync("post");
        var form = FormOf(await DocumentAsync("/post/"), "jfb-1");
        await SaveAndPublishAsync(key, definition => definition with
        {
            Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [.. definition.Pages[0].Groups[0].Fields.Select(field => field.Key == Terms.Key ? field with { Settings = SettingsObject.Parse("""{"text":{"":"<p>I agree to the new terms.</p>"}}""") } : field)] }] }],
        });
        Fill(form, "fullName.first", "Ada");
        Fill(form, "terms", "true");

        using var response = await SubmitAsync(form, FormPostFields.Submit);
        var shown = FormOf(await ParseAsync(response), "jfb-1");
        var notice = shown.QuerySelector("[data-jfb-notice]");
        Fill(shown, "terms", "true");
        using var again = await SubmitAsync(shown, FormPostFields.Submit);

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Header(response, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(notice?.GetAttribute("data-jfb-notice"), Is.EqualTo("form_changed"));
            Assert.That(notice?.GetAttribute("role"), Is.EqualTo("alert"));
            Assert.That(notice?.GetAttribute("class"), Is.EqualTo("jfb-notice"), "the form changed: no error");
            Assert.That(notice?.GetAttribute("tabindex"), Is.EqualTo("-1"));
            Assert.That(notice?.GetAttribute("aria-labelledby"), Is.EqualTo("jfb-1-notice"));
            Assert.That(notice?.QuerySelector("h2#jfb-1-notice")?.TextContent, Is.EqualTo("This form was updated while you filled it in"));
            Assert.That(notice?.QuerySelector("p")?.TextContent, Is.EqualTo("Your answers are kept. An agreement has new wording, so it is no longer ticked: read it again before you send."));
            Assert.That(shown.QuerySelector("[name='terms']")!.HasAttribute("checked"), Is.False, "the consent whose wording changed");
            Assert.That(shown.QuerySelector("[name='fullName.first']")!.GetAttribute("value"), Is.EqualTo("Ada"), "every other answer kept");
            Assert.That(shown.QuerySelector("[name='jfb-revision']")!.GetAttribute("value"), Is.EqualTo("2"), "the form as it is now");
            Assert.That(shown.QuerySelector(".jfb-consent, [data-jfb-alias='terms']")!.TextContent, Does.Contain("I agree to the new terms."));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "ticked again under the new wording, it is sent");
            Assert.That(await StoredAsync(key), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task EachOutcome_IsFollowed_AMessageThenARedirectWithALinkToGoOn()
    {
        _ = await PublishFormAsync("postWays", Ways, Way);
        _ = await PagesAsync("post-ways");

        async Task<HttpResponseMessage> SendWayAsync(string way)
        {
            var form = FormOf(await DocumentAsync("/post-ways/"), "jfb-1");
            Fill(form, "way", way);
            return await SubmitAsync(form, FormPostFields.Submit, "/post-ways/");
        }

        using var redirect = await SendWayAsync("redirect");
        using var message = await SendWayAsync("message");
        using var both = await SendWayAsync("both");
        var (_, messagePage) = await PageAndHtmlAsync(message.Headers.Location!.OriginalString);
        var (_, bothPage) = await PageAndHtmlAsync(both.Headers.Location!.OriginalString);

        Assert.Multiple(() =>
        {
            Assert.That((redirect.StatusCode, redirect.Headers.Location?.OriginalString), Is.EqualTo((HttpStatusCode.Redirect, "https://example.com/thanks")), "a redirect at once");
            Assert.That(Header(redirect, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(message.Headers.Location!.OriginalString, Does.StartWith("/post-ways/?jfb-sent=jfb-1."));
            Assert.That(messagePage.QuerySelector("[data-jfb-outcome] p")!.TextContent, Is.EqualTo("Thank you, message."));
            Assert.That(messagePage.QuerySelector("[data-jfb-continue]"), Is.Null);
            Assert.That(both.Headers.Location!.OriginalString, Does.StartWith("/post-ways/?jfb-sent=jfb-1."), "a message with a redirect shows its message first");
            Assert.That(bothPage.QuerySelector("[data-jfb-outcome] p")!.TextContent, Is.EqualTo("Thanks, both."));
            Assert.That(bothPage.QuerySelector("[data-jfb-outcome] .jfb-navigation a")!.OuterHtml, Is.EqualTo("""<a class="jfb-button" href="/after/" data-jfb-continue="">Continue</a>"""), "a link to go on, which the visitor follows when ready");
            Assert.That(bothPage.QuerySelector("meta[http-equiv]"), Is.Null, "no timed refresh");
        });
    }

    [Test]
    public async Task ASentValue_ForgedExpiredOrForAnotherInstance_ShowsTheFormAsForAnyVisitor()
    {
        _ = await PublishFormAsync("postWays", Ways, Way);
        _ = await PagesAsync("post-ways");
        var form = FormOf(await DocumentAsync("/post-ways/"), "jfb-1");
        Fill(form, "way", "message");
        using var sent = await SubmitAsync(form, FormPostFields.Submit, "/post-ways/");
        var location = sent.Headers.Location!.OriginalString;
        var token = location[(location.IndexOf("jfb-1.", StringComparison.Ordinal) + "jfb-1.".Length)..location.IndexOf('#', StringComparison.Ordinal)];

        var (fresh, freshPage) = await PageAndHtmlAsync(location);
        var forged = await DocumentAsync("/post-ways/?jfb-sent=jfb-1.AAAA" + token[4..]);
        var other = await DocumentAsync("/post-ways/?jfb-sent=jfb-2." + token);
        var twice = await DocumentAsync("/post-ways/?jfb-sent=jfb-1." + token + "&jfb-sent=jfb-1." + token);
        _clock.Advance(SentTokens.Lifetime + TimeSpan.FromSeconds(1));
        var (expired, expiredPage) = await PageAndHtmlAsync(location);

        Assert.Multiple(() =>
        {
            Assert.That(freshPage.QuerySelector("[data-jfb-outcome]"), Is.Not.Null, "the value as the redirect gave it");
            Assert.That(fresh, Is.EqualTo("no-store"));
            Assert.That(forged.QuerySelector("form#jfb-1"), Is.Not.Null, "a changed token");
            Assert.That(other.QuerySelector("form#jfb-1"), Is.Not.Null, "another instance's");
            Assert.That(twice.QuerySelector("form#jfb-1"), Is.Not.Null, "two values");
            Assert.That(expiredPage.QuerySelector("form#jfb-1"), Is.Not.Null, "after 30 minutes");
            Assert.That(expired, Is.Not.EqualTo("no-store"), "a page that shows the form only");
        });
    }

    [Test]
    public async Task APostToAFormForMembers_FromAnotherOrigin_Answers403AndRendersNothing_AndFromTheSitesOwnKeepsItsAnswers()
    {
        var key = await PublishFormAsync("postMain", definition => MainForm(definition) with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } },
        });
        _ = await PublishFormAsync("postPaged", Paged);
        _ = await PagesAsync("post");
        var carrier = FormOf(await DocumentAsync("/post/"), "jfb-2");
        List<KeyValuePair<string, string>> Post() =>
        [
            new(FormPostFields.RouteToken, carrier.QuerySelector("input[name='ufprt']")!.GetAttribute("value")!),
            new(FormPostFields.Form, key.ToString("D")),
            new(FormPostFields.Instance, "jfb-1"),
            new(FormPostFields.Revision, "1"),
            new("fullName.first", "Ada"),
            new(FormPostFields.Nav, FormPostFields.Submit),
        ];

        using var foreign = await PostPairsAsync("/post/", Post(), origin: "https://evil.test");
        using var opaque = await PostPairsAsync("/post/", Post(), origin: "null");
        using var own = await PostPairsAsync("/post/", Post(), origin: OwnOrigin);
        using var none = await PostPairsAsync("/post/", Post(), origin: null);
        await SignInMemberAsync("member@example.com");
        using var member = await PostPairsAsync("/post/", Post(), origin: OwnOrigin);
        var shown = FormOf(await ParseAsync(member), "jfb-1");
        var gated = await ParseAsync(own);

        Assert.Multiple(async () =>
        {
            Assert.That(foreign.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await foreign.Content.ReadAsStringAsync(), Does.Not.Contain("Ada"), "another site never sees the answers again");
            Assert.That(Header(foreign, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(opaque.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "an opaque origin");
            Assert.That(own.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(gated.QuerySelector("#jfb-1 .jfb-notice h2")!.TextContent, Is.EqualTo("This form is for members"), "a visitor the form does not admit gets its notice");
            Assert.That(none.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a post without an origin passes");
            Assert.That(shown.QuerySelector("[data-jfb-notice]")!.GetAttribute("data-jfb-notice"), Is.EqualTo("not_sent"), "until members bind to their posts, a member's post is not sent");
            Assert.That(shown.QuerySelector("[data-jfb-notice] h2")!.TextContent, Is.EqualTo("Your answers weren't sent"));
            Assert.That(shown.QuerySelector("[data-jfb-notice]")!.GetAttribute("class"), Is.EqualTo("jfb-notice jfb-notice-error"), "a send that did not go through");
            Assert.That(shown.QuerySelector("[data-jfb-notice] + [data-jfb-summary], [data-jfb-summary] + [data-jfb-notice]"), Is.Null, "no summary beside it");
            Assert.That(shown.QuerySelector("[name='fullName.first']")!.GetAttribute("value"), Is.EqualTo("Ada"), "with its answers kept");
            Assert.That(await StoredAsync(key), Is.Empty);
        });
    }

    [Test]
    public async Task TheQueryValuesTheFormLists_ComeFromTheSitesOwnReferrer_AreCarriedOnInThePage_AndReadByItsRules()
    {
        _ = await PublishFormAsync(
            "postWays",
            definition => Ways(definition) with { Settings = definition.Settings with { Developer = definition.Settings.Developer with { QueryValues = ["source", "tag"] } } },
            Way with { Required = true },
            Field(K(0x31), "promo", "text") with
            {
                Rules = [new FormRule { Key = K(0x32), Effect = RuleEffect.Show, Conditions = [new RuleCondition { Context = "query.source", Operator = "equals", Value = new ConditionOperand.Text("ad") }] }],
            });
        _ = await PagesAsync("post-ways");
        static string[] Carried(IElement form, string name) => [.. form.QuerySelectorAll($"input[type=hidden][name='jfb-query-{name}']").Select(input => input.GetAttribute("value")!)];
        static bool Promo(IElement form) => !form.QuerySelector("[data-jfb-alias='promo']")!.HasAttribute("hidden");
        var many = string.Join("&", Enumerable.Range(1, FormSubmissions.MaxQueryValues + 5).Select(n => "tag=t" + n));

        var first = await MoveAsync(FormOf(await DocumentAsync("/post-ways/"), "jfb-1"), FormPostFields.Submit, "jfb-1", "/post-ways/?source=ad&other=x&" + many + "&tag=" + new string('x', FormSubmissions.MaxValueLength + 1));
        var carried = await MoveAsync(first, FormPostFields.Submit, "jfb-1", "/post-ways/");
        var foreign = await MoveAsync(FormOf(await DocumentAsync("/post-ways/"), "jfb-1"), FormPostFields.Submit, "jfb-1", "https://elsewhere.test/post-ways/?source=ad");

        Assert.Multiple(() =>
        {
            Assert.That(Carried(first, "source"), Is.EqualTo(new[] { "ad" }), "from the page's address, which the referrer gives");
            Assert.That(Carried(first, "other"), Is.Empty, "a name the form does not list");
            Assert.That(Carried(first, "tag"), Is.EqualTo(Enumerable.Range(1, FormSubmissions.MaxQueryValues).Select(n => "t" + n)), "held to the pipeline's limits: the most values, none too long");
            Assert.That(Promo(first), Is.True, "a rule reads them, as the pipeline does");
            Assert.That(Carried(carried, "source"), Is.EqualTo(new[] { "ad" }), "carried on by the page, whose own address has none");
            Assert.That(Promo(carried), Is.True);
            Assert.That(Carried(foreign, "source"), Is.Empty, "another site's address");
            Assert.That(Promo(foreign), Is.False, "without the value, the rule hides the field");
        });
    }

    [Test]
    public async Task ErrorsInARepeatersRows_ShowInTheirRows_EveryAnswerKept()
    {
        _ = await PublishFormAsync("postMain", definition => WithFields(definition, Overall, Visits));
        _ = await PagesAsync("post");
        var form = FormOf(await DocumentAsync("/post/"), "jfb-1");
        Fill(form, "visits[0].who", "Ada");
        Fill(form, $"overall.{FirstRow:D}", "good");

        // A row's answer that is none of the options, as a page the browser keeps from an older version could post.
        var posted = Posted(form, FormPostFields.Submit).Append(new($"visits[0].rating.{RoomRow:D}", "excellent")).ToList();
        using var response = await PostPairsAsync("/post/", posted, OwnOrigin, "/post/");
        var shown = FormOf(await ParseAsync(response), "jfb-1");
        var rows = shown.QuerySelectorAll("[data-jfb-alias='visits'] .jfb-row");

        Assert.Multiple(() =>
        {
            Assert.That(shown.QuerySelectorAll(".jfb-error-summary a").Select(link => (link.GetAttribute("href"), link.TextContent)), Is.EqualTo(new[]
            {
                ("#jfb-1-overall", "Overall: Choose an answer in each row"),
                ("#jfb-1-visits-0-rating", "Rating (Visit 1): Choose one of the options given"),
                ("#jfb-1-visits-1-who", "Who (Visit 2): Enter an answer"),
                ("#jfb-1-visits-1-rating", "Rating (Visit 2): Choose an answer in each row"),
            }), "a grid answers for all its rows (ADR-0069); each row's errors after its legend, in the form's order");
            Assert.That(rows[0].QuerySelectorAll("[data-jfb-error]").Select(error => error.TextContent), Is.EqualTo(new[] { "Choose one of the options given" }));
            Assert.That(rows[1].QuerySelectorAll("[data-jfb-error]").Select(error => error.TextContent), Is.EqualTo(new[] { "Enter an answer", "Choose an answer in each row" }));
            Assert.That(shown.QuerySelector("[name='visits[0].who']")!.GetAttribute("value"), Is.EqualTo("Ada"), "every answer kept");
            Assert.That(shown.QuerySelector($"[name='overall.{FirstRow:D}'][value=good]")!.HasAttribute("checked"), Is.True);
        });
    }

    [Test]
    public async Task AFormThatFilledUpMeanwhile_ShowsItsNotice_OnNextAndOnSend()
    {
        _ = await PublishFormAsync("postMain", MainForm);
        var key = await PublishFormAsync("postPaged", definition => Paged(definition) with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { MaxEntries = 1 } },
        });
        _ = await PagesAsync("post");
        var onNext = FormOf(await DocumentAsync("/post/"), "jfb-2");
        var onSend = FormOf(await DocumentAsync("/post/"), "jfb-2");
        using var taken = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request =>
            request.Content = new StringContent("""{"revision":1,"values":{"email":"lin@example.com","topic":"Gardens"}}""", Encoding.UTF8, "application/json"));
        Fill(onNext, "email", "ada@example.com");
        Fill(onSend, "email", "ada@example.com");
        Fill(onSend, "topic", "Gardens");

        using var next = await SubmitAsync(onNext, FormPostFields.Next);
        var afterNext = await ParseAsync(next);
        using var send = await PostPairsAsync("/post/", [.. Posted(onSend, FormPostFields.Next).Where(pair => pair.Key != FormPostFields.Nav), new(FormPostFields.Nav, FormPostFields.Submit)], OwnOrigin, "/post/");
        var afterSend = await ParseAsync(send);

        Assert.Multiple(async () =>
        {
            Assert.That(taken.StatusCode, Is.EqualTo(HttpStatusCode.OK), await taken.Content.ReadAsStringAsync());
            Assert.That(afterNext.QuerySelector("#jfb-2 .jfb-notice h2")?.TextContent, Is.EqualTo("This form is full"), "Next meets the full form");
            Assert.That(afterNext.QuerySelector("form#jfb-2"), Is.Null, "the notice in the form's place, as for any visitor now");
            Assert.That(afterSend.QuerySelector("#jfb-2 .jfb-notice h2")?.TextContent, Is.EqualTo("This form is full"), "and so does Send");
            Assert.That(afterSend.QuerySelector("form#jfb-1"), Is.Not.Null, "the page's other form as it is");
            Assert.That(await StoredAsync(key), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AFormPublishedAgainMeanwhile_SaysTheAnswersAreKept_AndWhichAgreementsToReadAgain()
    {
        var first = Field(K(0x33), "first", "consent", """{"text":{"":"<p>I agree to the first.</p>"}}""");
        var second = Field(K(0x34), "second", "consent", """{"text":{"":"<p>I agree to the second.</p>"}}""");
        var key = await PublishFormAsync("postWays", Ways, Way, first, second);
        _ = await PagesAsync("post-ways");
        var form = FormOf(await DocumentAsync("/post-ways/"), "jfb-1");
        Fill(form, "way", "message");
        Fill(form, "first", "true");
        Fill(form, "second", "true");
        var newer = Posted(form, FormPostFields.Submit).Select(pair => pair.Key == FormPostFields.Revision ? new KeyValuePair<string, string>(pair.Key, "99") : pair).ToList();
        using var none = await PostPairsAsync("/post-ways/", newer, OwnOrigin, "/post-ways/");
        var unchanged = FormOf(await ParseAsync(none), "jfb-1");
        await SaveAndPublishAsync(key, definition => WithFields(definition, Way,
            first with { Settings = SettingsObject.Parse("""{"text":{"":"<p>I agree to the new first.</p>"}}""") },
            second with { Settings = SettingsObject.Parse("""{"text":{"":"<p>I agree to the new second.</p>"}}""") }));
        var both = await MoveAsync(form, FormPostFields.Submit, "jfb-1", "/post-ways/");

        Assert.Multiple(() =>
        {
            Assert.That(unchanged.QuerySelector("[data-jfb-notice] p")!.TextContent, Is.EqualTo("Your answers are kept."), "a revision no version had: no agreement changed");
            Assert.That(unchanged.QuerySelectorAll("input[type=checkbox]:checked, input[type=checkbox][checked]").Select(box => box.GetAttribute("name")), Is.EqualTo(new[] { "first", "second" }), "so both stay ticked");
            Assert.That(both.QuerySelector("[data-jfb-notice] p")!.TextContent, Is.EqualTo("Your answers are kept. Some agreements have new wording, so they are no longer ticked: read them again before you send."));
            Assert.That(both.QuerySelectorAll("input[type=checkbox][checked]"), Is.Empty);
            Assert.That(both.QuerySelector("[name='way']")!.GetAttribute("value"), Is.EqualTo("message"));
        });
    }

    [Test]
    public async Task TheSurfaceAutoRoute_AndAPostThatNamesNoForm_Answer400_EveryAnswerOfTheActionNoStore()
    {
        _ = await PublishFormAsync("postMain", MainForm);
        _ = await PublishFormAsync("postPaged", Paged);
        _ = await PagesAsync("post");
        var carrier = FormOf(await DocumentAsync("/post/"), "jfb-2");
        var route = carrier.QuerySelector("input[name='ufprt']")!.GetAttribute("value")!;

        using var auto = await PostPairsAsync("/umbraco/surface/" + FormBuilderSurfaceController.ControllerName + "/" + FormBuilderSurfaceController.ActionName, [new(FormPostFields.Form, K(0x01).ToString("D"))], OwnOrigin);
        using var nameless = await PostPairsAsync("/post/", [new(FormPostFields.RouteToken, route)], OwnOrigin);
        using var badInstance = await PostPairsAsync("/post/", [new(FormPostFields.RouteToken, route), new(FormPostFields.Form, K(0x01).ToString("D")), new(FormPostFields.Instance, "evil"), new(FormPostFields.Revision, "1")], OwnOrigin);
        using var wordy = await PostPairsAsync("/post/", [new(FormPostFields.RouteToken, route), new(FormPostFields.Form, K(0x01).ToString("D")), new(FormPostFields.Instance, "jfb-one"), new(FormPostFields.Revision, "1")], OwnOrigin);
        using var bare = await PostPairsAsync("/post/", [new(FormPostFields.RouteToken, route), new(FormPostFields.Form, K(0x01).ToString("D")), new(FormPostFields.Instance, "jfb-"), new(FormPostFields.Revision, "1")], OwnOrigin);
        using var gone = await PostPairsAsync("/post/", [new(FormPostFields.RouteToken, route), new(FormPostFields.Form, Guid.NewGuid().ToString("D")), new(FormPostFields.Instance, "jfb-1"), new(FormPostFields.Revision, "1")], OwnOrigin);

        Assert.Multiple(() =>
        {
            Assert.That(auto.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "a post that reached the action without a page");
            Assert.That(Header(auto, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(nameless.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "no form named");
            Assert.That(Header(nameless, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(badInstance.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "an instance id the render never gives");
            Assert.That((wordy.StatusCode, bare.StatusCode), Is.EqualTo((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest)), "the prefix without its number");
            Assert.That(gone.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a form gone meanwhile: the page without it");
            Assert.That(Header(gone, "Cache-Control"), Is.EqualTo("no-store"));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        _clock = new TestClock();
        builder.Services.AddSingleton(_clock);
        builder.Services.AddUnique<TimeProvider>(_clock);
        builder.Services.AddSingleton<MemoryFileStore>();
        builder.Services.AddUnique<IFormBuilderFileStore>(provider => provider.GetRequiredService<MemoryFileStore>());
        builder.Services.AddSingleton<BodyCap>();
        builder.Services.AddSingleton<IStartupFilter>(provider => provider.GetRequiredService<BodyCap>());
    }

    // The main form: a name, a long text, a switch, a required consent and two guests' rows with a switch each; its
    // message recalls the first name.
    private static FormDefinition MainForm(FormDefinition definition) => WithFields(definition, FullName, Story, Updates, Terms, Guests) with
    {
        Outcomes = [new FormOutcome { Key = K(0x50), Message = new LocalizedHtml("<p>Thank you, {{ field.fullName.first }}.</p>") }],
    };

    // Three pages: an email, then a topic, both required, then a note; no outcome of its own.
    private static FormDefinition Paged(FormDefinition definition) => definition with
    {
        Pages =
        [
            new FormPage { Key = K(0x40), Groups = [new FormGroup { Key = K(0x43), Fields = [Email] }] },
            new FormPage { Key = K(0x41), Groups = [new FormGroup { Key = K(0x44), Fields = [Topic] }] },
            new FormPage { Key = K(0x42), Groups = [new FormGroup { Key = K(0x45), Fields = [Note] }] },
        ],
    };

    // A redirect at once, a message then a redirect, and a message, chosen by the answer of way.
    private static FormDefinition Ways(FormDefinition definition) => definition with
    {
        Outcomes =
        [
            new FormOutcome { Key = K(0x51), Kind = OutcomeKind.Redirect, Redirect = new OutcomeRedirect { Url = "https://example.com/thanks" }, Rules = [Use(K(0x52), "redirect")] },
            new FormOutcome { Key = K(0x53), Message = new LocalizedHtml("<p>Thanks, {{ field.way }}.</p>"), Redirect = new OutcomeRedirect { Url = "/after/", DelaySeconds = 5 }, Rules = [Use(K(0x54), "both")] },
            new FormOutcome { Key = K(0x55), Message = new LocalizedHtml("<p>Thank you, {{ field.way }}.</p>") },
        ],
    };

    private static FormRule Use(Guid key, string way) => new()
    {
        Key = key,
        Effect = RuleEffect.Use,
        Conditions = [new RuleCondition { Field = Way.Key, Operator = "equals", Value = new ConditionOperand.Text(way) }],
    };

    private static FormField Field(Guid key, string alias, string type, string settings = "{}") => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Label = new LocalizedText(char.ToUpperInvariant(alias[0]) + alias[1..]),
        Settings = settings == "{}" ? null : SettingsObject.Parse(settings),
    };

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-0000006308{number:x2}");

    // The site's own origin, as a browser names it.
    private string OwnOrigin => Client.BaseAddress!.GetLeftPart(UriPartial.Authority);

    // The key of the page a form shows: the one page that is not hidden.
    private static Guid Shown(IElement form) => Guid.Parse(form.QuerySelectorAll("[data-jfb-page]").Single(page => !page.HasAttribute("hidden")).GetAttribute("data-jfb-page")!);

    private static IElement FormOf(IHtmlDocument document, string instance) =>
        document.QuerySelector($"form#{instance}") ?? throw new AssertionException($"no form {instance} in: {document.Body?.InnerHtml}");

    // Sets a control's value as a visitor would: a box or a radio ticked when its value is among the values, else the
    // first value typed in.
    private static void Fill(IElement form, string name, params string[] values)
    {
        var controls = form.QuerySelectorAll($"[name='{name}']");
        Assert.That(controls, Is.Not.Empty, $"no control {name}");
        foreach (var control in controls)
        {
            switch (control)
            {
                case IHtmlInputElement { Type: "checkbox" or "radio" } box:
                    box.IsChecked = values.Contains(box.Value);
                    break;
                case IHtmlInputElement input:
                    input.Value = values[0];
                    break;
                case IHtmlTextAreaElement area:
                    area.Value = values[0];
                    break;
                case IHtmlSelectElement select:
                    foreach (var option in select.Options)
                    {
                        option.IsSelected = values.Contains(option.Value);
                    }

                    break;
            }
        }
    }

    // The names and values a browser posts for the form (HTML's form data set): every named control that is not
    // disabled, a box only when ticked, a select's chosen options, the button pressed, every line break as CRLF.
    private static List<KeyValuePair<string, string>> Posted(IElement form, string? nav)
    {
        var pressed = nav is null ? null : form.QuerySelectorAll("button[name='jfb-nav']:not([tabindex])").Single(button => button.GetAttribute("value") == nav);
        var posted = new List<KeyValuePair<string, string>>();
        foreach (var control in form.QuerySelectorAll("input, select, textarea, button"))
        {
            if (control.GetAttribute("name") is not { Length: > 0 } name || control.HasAttribute("disabled") || control.Closest("fieldset[disabled]") is not null)
            {
                continue;
            }

            switch (control)
            {
                case IHtmlButtonElement button when button == pressed:
                    posted.Add(new(name, button.Value));
                    break;
                case IHtmlInputElement { Type: "checkbox" or "radio" } box when box.IsChecked:
                    posted.Add(new(name, box.Value));
                    break;
                case IHtmlInputElement { Type: "checkbox" or "radio" or "submit" or "button" or "reset" or "image" or "file" }:
                    break;
                case IHtmlInputElement input:
                    posted.Add(new(name, input.Value));
                    break;
                case IHtmlTextAreaElement area:
                    posted.Add(new(name, area.Value));
                    break;
                case IHtmlSelectElement select:
                    posted.AddRange(select.Options.Where(option => option.IsSelected).Select(option => new KeyValuePair<string, string>(name, option.Value)));
                    break;
            }
        }

        var cr = ((char)13).ToString();
        return [.. posted.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value.Replace(Crlf, Lf, StringComparison.Ordinal).Replace(cr, Lf, StringComparison.Ordinal).Replace(Lf, Crlf, StringComparison.Ordinal)))];
    }

    // Posts the form as a browser without JavaScript does: to its action, from the site's own origin, the page as the referrer.
    private Task<HttpResponseMessage> SubmitAsync(IElement form, string? nav, string page = "/post/") =>
        PostPairsAsync(form.GetAttribute("action")!.Split('#')[0], Posted(form, nav), OwnOrigin, page);

    private async Task<HttpResponseMessage> PostPairsAsync(string path, List<KeyValuePair<string, string>> pairs, string? origin, string? referer = null) =>
        await SendAsync(HttpMethod.Post, path, request =>
        {
            request.Content = new FormUrlEncodedContent(pairs);
            if (origin is not null)
            {
                request.Headers.TryAddWithoutValidation("Origin", origin);
            }

            if (referer is not null)
            {
                request.Headers.Referrer = new Uri(Client.BaseAddress!, referer);
            }
        });

    private static async Task<IHtmlDocument> ParseAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), html);
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    private async Task<IHtmlDocument> DocumentAsync(string path)
    {
        using var response = await GetAsync(path);
        return await ParseAsync(response);
    }

    // A page with its Cache-Control.
    private async Task<(string? CacheControl, IHtmlDocument Document)> PageAndHtmlAsync(string path)
    {
        using var response = await GetAsync(path);
        return (Header(response, "Cache-Control"), await ParseAsync(response));
    }

    // Posts the form, and the form as the page shows it again.
    private async Task<IElement> MoveAsync(IElement form, string? nav, string instance, string page = "/post/")
    {
        using var response = await SubmitAsync(form, nav, page);
        return FormOf(await ParseAsync(response), instance);
    }

    // The form's fields' catalogue rows by the field's key.
    private async Task<Dictionary<Guid, int>> CatalogueAsync(Guid key) =>
        (await FormRows.ListFieldsAsync((await FormRows.GetAsync(key))!.Id)).ToDictionary(field => field.Values.FieldKey, field => field.Id);

    // Each entry's stored values, oldest entry first, without the rows' and the entry's ids.
    private async Task<List<List<Stored>>> StoredAsync(Guid key)
    {
        var ids = await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10);
        var values = await Entries.ListValuesAsync(ids);
        return [.. ids.Order().Select(id => values.Where(value => value.EntryId == id).Select(value => new Stored(value.FieldId, value.RowIndex, value.ValueIndex, value.Text, value.TextKey, value.Number, value.Date, value.Bool)).ToList())];
    }

    private sealed record Stored(int Field, int Row, int Index, string? Text, string? TextKey, double? Number, DateTime? Date, bool? Bool);
}
