using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Web.Website.Caching;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// A page with a form behind Umbraco's website output cache (ROADMAP 6.2l, ADR-0004, ADR-0104): stored once and the
/// same bytes for every visitor while its form is for anyone, never stored with a form for members, for a visitor or a
/// member, nor after a post without JavaScript (<c>?jfb-sent</c>, with any value) or for a post; the surface action's
/// auto-route refused. The site's request filter stays its own.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class OutputCacheTests : OutputCacheTestBase
{
    [Test]
    public async Task ACachedPage_IsByteIdenticalForTwoVisitors_AndHoldsNothingPerVisitor()
    {
        _ = await PublishFormAsync(CacheForm, null, RequiredName);
        _ = await PagesAsync(CachePage);

        var first = await ServedAsync(Client, "/cache/");
        var second = await ServedAsync(Visitor(), "/cache/", request =>
        {
            request.Headers.TryAddWithoutValidation("Cookie", "theme=dark; returning=1");
            request.Headers.TryAddWithoutValidation("Accept-Language", "de-DE,de;q=0.9");
        });
        using var head = await Visitor().SendAsync(new HttpRequestMessage(HttpMethod.Head, "/cache/?utm_source=mail"));

        Assert.Multiple(() =>
        {
            Assert.That((first.Status, second.Status), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)));
            Assert.That((first.FromCache, second.FromCache), Is.EqualTo((false, true)), "rendered once, then answered from the cache");
            Assert.That(second.Bytes, Is.EqualTo(first.Bytes), "byte for byte");
            Assert.That(first.CacheControl ?? string.Empty, Does.Not.Contain("no-store"));
            Assert.That(head.Headers.CacheControl?.ToString() ?? string.Empty, Does.Not.Contain("no-store"), "a HEAD is no visitor's own either");
            Assert.That((first.SetsCookie, second.SetsCookie), Is.EqualTo((false, false)));
            Assert.That(first.Document.QuerySelector("form#jfb-1"), Is.Not.Null);
            Assert.That(first.Html, Does.Not.Contain("nonce").And.Not.Contain("__RequestVerificationToken").And.Not.Contain(SentTokens.QueryName).And.Not.Contain("challenge"), "nothing per visitor");
        });
    }

    [Test]
    public async Task TheSitesRequestFilter_StaysItsOwn_AndCachesASignedInMembersPage_WithAFormForAnyone()
    {
        _ = await PublishFormAsync(CacheForm, null, RequiredName);
        _ = await PagesAsync(CachePage);
        await SignInMemberAsync("cache-member@example.com");

        var first = await ServedAsync(Client, "/cache/");
        var second = await ServedAsync(Client, "/cache/");

        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredService<IWebsiteOutputCacheRequestFilter>(), Is.TypeOf<CachesSignedInMembers>(), "the package registers no filter of its own");
            Assert.That(Services.GetServices<IWebsiteOutputCacheVaryByProvider>().OfType<SentVaryBy>().Count(), Is.EqualTo(1));
            Assert.That((first.FromCache, second.FromCache), Is.EqualTo((false, true)), "the filter lets a member's page into the cache, so the rules below are the package's");
            Assert.That(second.Stamp, Is.EqualTo(first.Stamp));
        });
    }

    [Test]
    public async Task APageWithAFormForMembers_IsNeverStored_ForAVisitor_OrForAMember()
    {
        _ = await PublishFormAsync(CacheForm, definition => For(definition, MemberAccess.Members), RequiredName);
        _ = await PagesAsync(CachePage);

        var visitor = await ServedAsync(Client, "/cache/");
        var again = await ServedAsync(Visitor(), "/cache/");
        await SignInMemberAsync("cache-member@example.com");
        var member = await ServedAsync(Client, "/cache/");
        var memberAgain = await ServedAsync(Client, "/cache/");
        var after = await ServedAsync(Visitor(), "/cache/");

        Assert.Multiple(() =>
        {
            Served[] all = [visitor, again, member, memberAgain, after];
            Assert.That(all.Select(served => served.CacheControl), Is.All.EqualTo("no-store"));
            Assert.That(all.Select(served => served.FromCache), Is.All.False);
            Assert.That(all.Select(served => served.Stamp).Distinct().Count(), Is.EqualTo(all.Length), "each one rendered");
            Assert.That(visitor.Document.QuerySelector(".jfb-notice"), Is.Not.Null, "a visitor is asked to sign in");
            Assert.That(member.Document.QuerySelector("form#jfb-1"), Is.Not.Null, "a member gets the form");
            Assert.That(after.Document.QuerySelector("form#jfb-1"), Is.Null, "the member's page never answers a visitor");
        });
    }

    [Test]
    public async Task ThePageAfterAPost_ShowsTheOutcome_AndNoRequestThatCarriesJfbSent_IsStored_WhateverItsValue()
    {
        _ = await PublishFormAsync(CacheForm, null, RequiredName);
        _ = await PagesAsync(CachePage);
        var cached = await ServedAsync(Client, "/cache/");

        var sent = await PostAsync(Client, "/cache/", Sent(cached.Document.QuerySelector("form#jfb-1")!, ("name", "Ada")));
        var outcome = await ServedAsync(Client, sent.Location!);
        var outcomeAgain = await ServedAsync(Visitor(), sent.Location!);
        var forged = new[] { "?jfb-sent=x123", "?jfb-sent=x123", "?jfb-sent=", "?jfb-sent", "?JFB-SENT=jfb-1.x", "?utm_source=mail&jfb-sent=jfb-9.y" };
        var others = new List<Served>();
        foreach (var query in forged)
        {
            others.Add(await ServedAsync(Visitor(), "/cache/" + query));
        }

        var plain = await ServedAsync(Visitor(), "/cache/");

        Assert.Multiple(() =>
        {
            Assert.That(sent.Status, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(sent.Location, Does.StartWith("/cache/?" + SentTokens.QueryName + "=jfb-1."));
            Assert.That(outcome.Document.QuerySelector("#jfb-1 [data-jfb-outcome]"), Is.Not.Null, "the outcome, not the cached form");
            Assert.That(outcomeAgain.Document.QuerySelector("#jfb-1 [data-jfb-outcome]"), Is.Not.Null);
            Served[] marked = [outcome, outcomeAgain, .. others];
            Assert.That(marked.Select(served => served.CacheControl), Is.All.EqualTo("no-store"));
            Assert.That(marked.Select(served => served.FromCache), Is.All.False);
            Assert.That(marked.Select(served => served.Stamp).Append(cached.Stamp).Distinct().Count(), Is.EqualTo(marked.Length + 1), "each one rendered, none from another's entry");
            Assert.That(others.Select(served => served.Document.QuerySelector("form#jfb-1") is not null), Is.All.True, "a token that names nothing shows the form");
            Assert.That((plain.FromCache, plain.Stamp), Is.EqualTo((true, cached.Stamp)), "the page's own entry is untouched");
        });
    }

    [Test]
    public async Task NoResponseToAPost_IsStored_AndTheSurfaceAutoRoute_Answers400()
    {
        _ = await PublishFormAsync(CacheForm, null, RequiredName);
        _ = await PagesAsync(CachePage);
        var cached = await ServedAsync(Client, "/cache/");
        var form = cached.Document.QuerySelector("form#jfb-1")!;

        var invalid = await PostAsync(Client, "/cache/", Sent(form, ("name", string.Empty)));
        var invalidAgain = await PostAsync(Visitor(), "/cache/", Sent(form, ("name", string.Empty)));
        var bot = await PostAsync(Visitor(), "/cache/", [new("name", "spam")]);
        var botAgain = await PostAsync(Visitor(), "/cache/", [new("name", "spam")]);
        var auto = await PostAsync(Visitor(), "/umbraco/surface/" + FormBuilderSurfaceController.ControllerName + "/" + FormBuilderSurfaceController.ActionName, [new(FormPostFields.Form, Guid.NewGuid().ToString("D"))]);
        var plain = await ServedAsync(Visitor(), "/cache/");

        Assert.Multiple(() =>
        {
            Assert.That((invalid.Status, invalidAgain.Status, bot.Status, botAgain.Status), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK)));
            Assert.That(invalid.Document.QuerySelector("#jfb-1 [data-jfb-summary], #jfb-1 .jfb-summary"), Is.Not.Null, "the errors, shown again");
            Served[] posts = [invalid, invalidAgain, bot, botAgain];
            Assert.That(posts.Select(served => served.CacheControl), Is.All.EqualTo("no-store"), "a post the surface action took, and one that only rendered the page");
            Assert.That(posts.Select(served => served.FromCache), Is.All.False);
            Assert.That(posts.Select(served => served.Stamp).Append(cached.Stamp).Distinct().Count(), Is.EqualTo(posts.Length + 1), "none answered from another's entry");
            Assert.That((auto.Status, auto.CacheControl), Is.EqualTo((HttpStatusCode.BadRequest, "no-store")), "the auto-route has no page");
            Assert.That((plain.FromCache, plain.Stamp), Is.EqualTo((true, cached.Stamp)), "the page's entry is the visitor's GET");
            Assert.That(plain.Document.QuerySelector("#jfb-1 [data-jfb-summary], #jfb-1 .jfb-summary"), Is.Null);
        });
    }

    private static FormDefinition For(FormDefinition definition, MemberAccess access) =>
        definition with { Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = access } } };
}
