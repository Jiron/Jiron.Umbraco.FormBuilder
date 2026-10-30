using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

// Prefill (ROADMAP 6.5c, ADR-0124): the server writes the signed-in member's value and the placement's context value
// into the controls' starts and marks a query prefill for the page to fill, the same for every visitor; a field hidden
// behind the member's value is left out of the member's page, which is never stored, and the entry takes the member's
// value whatever either transport posts for it, while any other visitor sees and answers the field.
public sealed partial class FormPostTests
{
    private const string PrefillPage = "/post-prefill/";
    private const string MemberEmail = "mira@example.test";
    private const string Forged = "forged@example.test";

    [Test]
    public async Task APrefilledForm_StartsWithTheMembersAndThePagesValues_AndHidesTheMembersFieldFromThemAlone()
    {
        await PublishFormAsync("postPrefill", PrefillForm);
        _ = await PagesAsync("post-prefill");
        var (anonymousCache, anonymous) = await PageAndHtmlAsync(PrefillPage + "?source=mail");
        await SignInMemberAsync(MemberEmail);
        var (memberCache, member) = await PageAndHtmlAsync(PrefillPage + "?source=mail");
        var visitorForm = FormOf(anonymous, "jfb-1");
        var memberForm = FormOf(member, "jfb-1");

        Assert.Multiple(() =>
        {
            Assert.That(Value(visitorForm, "email") ?? string.Empty, Is.Empty, "a visitor who is not signed in answers the email address");
            Assert.That(Value(visitorForm, "name") ?? string.Empty, Is.Empty, "and has no member's name");
            Assert.That(Value(visitorForm, "topic"), Is.EqualTo("billing"), "the context value the placement gives");
            Assert.That(Value(visitorForm, "source") ?? string.Empty, Is.Empty, "the server writes no query value");
            Assert.That(Marked(visitorForm), Is.EqualTo(new[] { "source" }), "it marks the query prefill for the page");
            Assert.That(anonymousCache, Is.Not.EqualTo("no-store"), "a visitor's page holds nobody's values");
            Assert.That(memberForm.QuerySelector("[data-jfb-alias='email'], [name='email']"), Is.Null, "the member's own address stands in, so their page leaves the field out");
            Assert.That(member.Body!.InnerHtml, Does.Not.Contain(MemberEmail), "and never writes it");
            Assert.That(Value(memberForm, "name"), Is.EqualTo("Rendering member"), "the member's name, theirs to change");
            Assert.That(Value(memberForm, "topic"), Is.EqualTo("billing"));
            Assert.That(Marked(memberForm), Is.EqualTo(new[] { "source" }));
            Assert.That(memberCache, Is.EqualTo("no-store"), "a member's page is theirs alone");
        });
    }

    [Test]
    public async Task APostedValueForAFieldHiddenBehindTheMembersValue_IsIgnored_OnBothTransports()
    {
        var key = await PublishFormAsync("postPrefill", PrefillForm);
        _ = await PagesAsync("post-prefill");
        var visitor = FormOf(await DocumentAsync(PrefillPage), "jfb-1");
        Fill(visitor, "email", "visitor@example.test");
        using var asVisitor = await SubmitAsync(visitor, FormPostFields.Submit, PrefillPage);
        await SignInMemberAsync(MemberEmail);
        var form = FormOf(await DocumentAsync(PrefillPage), "jfb-1");
        var forged = Posted(form, FormPostFields.Submit);
        forged.Add(new("email", Forged));
        using var page = await PostPairsAsync(form.GetAttribute("action")!.Split('#')[0], forged, OwnOrigin, PrefillPage);
        using var api = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent(new JsonObject
        {
            ["revision"] = 1,
            ["values"] = new JsonObject { ["email"] = Forged, ["name"] = "Mira", ["topic"] = "sales" },
        }.ToJsonString(), Encoding.UTF8, "application/json"));
        var entries = (await StoredAsync(key)).Select(entry => entry.Select(value => value.Text).ToList()).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(asVisitor.Headers.Location?.OriginalString, Does.Contain("jfb-sent="), "a visitor sends the address they typed");
            Assert.That(page.Headers.Location?.OriginalString, Does.Contain("jfb-sent="), "the member's post without JavaScript is sent");
            Assert.That(api.IsSuccessStatusCode, Is.True, await api.Content.ReadAsStringAsync());
            Assert.That(entries, Has.Count.EqualTo(3));
            Assert.That(entries[0], Does.Contain("visitor@example.test"));
            Assert.That(entries[1], Does.Contain(MemberEmail).And.Not.Contain(Forged), "the page's post takes the member's address, never the forged one");
            Assert.That(entries[2], Does.Contain(MemberEmail).And.Not.Contain(Forged), "and so does the JSON endpoint");
            Assert.That(entries[2], Does.Contain("Mira").And.Contain("sales"), "the fields the member sees take what was posted");
        });
    }

    // A contact form: the email address hidden behind the member's, the name shown with the member's, a topic from the
    // placement's context value (which the form lists as Topic and the page's attribute names in lower case), and a
    // source from the allowed query value.
    private static FormDefinition PrefillForm(FormDefinition definition) => WithFields(
        definition,
        Field(K(0xE0), "email", "email") with { Required = true, Prefill = new FieldPrefill { Member = "email", MemberMode = PrefillMemberMode.Hide } },
        Field(K(0xE1), "name", "text") with { Prefill = new FieldPrefill { Member = "name" } },
        Field(K(0xE2), "topic", "text") with { Prefill = new FieldPrefill { Context = "Topic" } },
        Field(K(0xE3), "source", "text") with { Prefill = new FieldPrefill { Query = "source" } }) with
    {
        Settings = definition.Settings with { Developer = definition.Settings.Developer with { QueryValues = ["source"], ContextValues = ["Topic"] } },
    };
}
