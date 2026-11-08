using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The library's email layouts through the management API (ROADMAP 12.1, ADR-0215): the editor's languages and mail's
/// own colours; a layout created, read and changed with its logo from the media library, each problem at its path; the
/// email actions that name it, counted, and the delete that waits until none does and refuses the site's default; a
/// duplicate's free alias; the preview of a sample entry in each culture; and the test mail to the user who asks.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EmailLayoutLibraryApiTests : ManagementApiTestBase
{
    private const string Designer = "designer";

    private static readonly object Brand = new
    {
        layout = new
        {
            alias = "brand",
            name = "Brand mail",
            design = new
            {
                look = "letter",
                logoUrl = "https://cdn.jfb.test/logo.png",
                logoAlt = new Dictionary<string, string> { [""] = "Acme", ["de"] = "Acme GmbH" },
                light = new { accent = "#8a1538" },
                dark = new { accent = "#f0a5b8" },
                header = new Dictionary<string, string> { [""] = "<p>Hello from Acme</p>", ["de"] = "<p>Hallo von Acme</p>" },
                footer = new Dictionary<string, string> { [""] = "<p>Acme, Main Street 1</p>" },
            },
        },
    };

    private IEmailRepository Emails => GetRequiredService<IEmailRepository>();

    private ScriptedEmailSender Sender => (ScriptedEmailSender)GetRequiredService<IEmailSender>();

    [Test]
    public async Task TheEditor_GivesTheSitesLanguages_TheDefaultFirst_AndMailsOwnColoursInBothSchemes()
    {
        var answer = await CallAsync(Designer, HttpMethod.Get, "email-layout/editor");

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        Assert.Multiple(() =>
        {
            var first = answer.Body!["languages"]![0]!;
            Assert.That((first["culture"]!.GetValue<string>(), first["isDefault"]!.GetValue<bool>()), Is.EqualTo(("en-US", true)));
            Assert.That(answer.Body!["light"]!["accent"]!.GetValue<string>(), Is.EqualTo(EmailRenderer.LightColours.Accent));
            Assert.That(answer.Body!["dark"]!["surface"]!.GetValue<string>(), Is.EqualTo(EmailRenderer.DarkColours.Surface));
            Assert.That(answer.Body!["defaultLayout"], Is.Null, "the built-in card");
        });
    }

    [Test]
    public async Task ALayout_IsCreatedReadAndChanged_ItsLogoReadFromTheMediaLibrary_EachChangeAudited()
    {
        var created = await CallAsync(Designer, HttpMethod.Post, "email-layout", Brand);
        Assert.That(created.Outcome, Is.EqualTo("201"), created.ToString());
        var key = created.Body!["key"]!.GetValue<Guid>();
        var logo = Image("Logo", "/media/abc/logo.png");
        var read = await CallAsync(Designer, HttpMethod.Get, $"email-layout/{key}");
        var changed = await CallAsync(Designer, HttpMethod.Put, $"email-layout/{key}", new
        {
            concurrencyVersion = read.Body!["concurrencyVersion"]!.GetValue<int>(),
            layout = new { alias = "brand", name = "Brand", design = new { look = "card", logoMedia = logo, logoUrl = "https://elsewhere.jfb.test/old.png" } },
        });
        var rows = await CallAsync(Designer, HttpMethod.Get, "email-layout/library");
        var audit = await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { EntityKey = key }, PageRequest.First(10));

        Assert.Multiple(() =>
        {
            var design = read.Body!["design"]!;
            Assert.That(design["look"]!.GetValue<string>(), Is.EqualTo("letter"));
            Assert.That(design["logoUrl"]!.GetValue<string>(), Is.EqualTo("https://cdn.jfb.test/logo.png"));
            Assert.That(design["logoAlt"]!["de"]!.GetValue<string>(), Is.EqualTo("Acme GmbH"));
            Assert.That(design["light"]!["accent"]!.GetValue<string>(), Is.EqualTo("#8a1538"));
            Assert.That(design["light"]!["page"], Is.Null, "a colour the layout sets none of is mail's own");
            Assert.That(design["header"]!["de"]!.GetValue<string>(), Is.EqualTo("<p>Hallo von Acme</p>"));
            Assert.That(changed.Outcome, Is.EqualTo("200"), changed.ToString());
            Assert.That(changed.Body!["design"]!["logoMedia"]!.GetValue<Guid>(), Is.EqualTo(logo));
            Assert.That(changed.Body!["design"]!["logoUrl"]!.GetValue<string>(), Is.EqualTo("/media/abc/logo.png"), "a media item's address, read on save");
            var row = rows.Body!["items"]![0]!;
            Assert.That((row["name"]!.GetValue<string>(), row["look"]!.GetValue<string>(), row["usedBy"]!.GetValue<int>(), row["isDefault"]!.GetValue<bool>()), Is.EqualTo(("Brand", "card", 0, false)));
            Assert.That(row["swatch"]!.AsArray().Select(colour => colour!.GetValue<string>()), Is.EqualTo(new[] { EmailRenderer.LightColours.Accent, EmailRenderer.LightColours.Page }), "mail's own colours where it sets none");
            Assert.That(audit.Items.Select(entry => entry.Action), Is.EquivalentTo(new[] { EmailLayoutLibrary.CreatedAction, EmailLayoutLibrary.SavedAction }));
        });
    }

    [Test]
    public async Task ProblemsOfALayout_AreEachSaidAtTheirPath()
    {
        var refused = await CallAsync(Designer, HttpMethod.Post, "email-layout", new
        {
            layout = new
            {
                alias = "Brand mail",
                name = " ",
                design = new
                {
                    look = "card",
                    logoUrl = "javascript:alert(1)",
                    light = new { accent = "red" },
                    dark = new { rule = "#12345" },
                    footer = new Dictionary<string, string> { ["de"] = new string('x', EmailLayoutLibrary.MaxTextLength + 1) },
                },
            },
        });
        var look = await CallAsync(Designer, HttpMethod.Post, "email-layout", new { layout = new { alias = "poster", name = "Poster", design = new { look = "poster" } } });
        var gone = await CallAsync(Designer, HttpMethod.Post, "email-layout", new { layout = new { alias = "gone", name = "Gone", design = new { look = "card", logoMedia = Guid.NewGuid() } } });
        _ = await CallAsync(Designer, HttpMethod.Post, "email-layout", Brand);
        var taken = await CallAsync(Designer, HttpMethod.Post, "email-layout", new { layout = new { alias = "brand", name = "Again", design = new { look = "card" } } });

        Assert.Multiple(() =>
        {
            Assert.That(Problems(refused), Is.EqualTo("400 $.alias $.name $.design.light.accent $.design.dark.rule $.design.logoUrl $.design.footer['de']"), refused.ToString());
            Assert.That(Problems(look), Is.EqualTo("400 $.design.look"), look.ToString());
            Assert.That(Problems(gone), Is.EqualTo("400 $.design.logoMedia"), gone.ToString());
            Assert.That(Problems(taken), Is.EqualTo("400 $.alias"), taken.ToString());
            Assert.That(taken.Code, Is.EqualTo("alias_duplicate"));
        });
    }

    [Test]
    public async Task UsedBy_CountsTheEmailActionsThatNameTheLayout_AndTheDeleteWaits_AsItDoesForTheSitesDefault()
    {
        var created = await CallAsync(Designer, HttpMethod.Post, "email-layout", Brand);
        var key = created.Body!["key"]!.GetValue<Guid>();
        var form = await FormWithActionsAsync("Contact us", Mail(key), Mail(key), Mail(EmailLayouts.LetterKey));

        var rows = await CallAsync(Designer, HttpMethod.Get, "email-layout/library");
        var users = await CallAsync(Designer, HttpMethod.Get, $"email-layout/{key}/used-by");
        var inUse = await CallAsync(Designer, HttpMethod.Delete, $"email-layout/{key}?concurrencyVersion=1");
        await TrashFormAsync(form);
        var forms = GetRequiredService<IFormBuilderFormService>();
        await forms.DeleteAsync(form, (await forms.GetAsync(form))!.ConcurrencyVersion, null);
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var email = await settings.GetAsync<EmailSettings>();
        _ = await settings.UpdateAsync<EmailSettings>(current => current with { DefaultLayout = key }, email.Version, null);
        var isDefault = await CallAsync(Designer, HttpMethod.Delete, $"email-layout/{key}?concurrencyVersion=1");
        var defaultRow = await CallAsync(Designer, HttpMethod.Get, "email-layout/library");
        var editor = await CallAsync(Designer, HttpMethod.Get, "email-layout/editor");
        email = await settings.GetAsync<EmailSettings>();
        _ = await settings.UpdateAsync<EmailSettings>(current => current with { DefaultLayout = null }, email.Version, null);
        var deleted = await CallAsync(Designer, HttpMethod.Delete, $"email-layout/{key}?concurrencyVersion=1");
        var gone = await CallAsync(Designer, HttpMethod.Get, $"email-layout/{key}");

        Assert.Multiple(() =>
        {
            Assert.That(rows.Body!["items"]![0]!["usedBy"]!.GetValue<int>(), Is.EqualTo(2), "each email action that names it");
            Assert.That(users.Body!["forms"]!.AsArray().Select(each => each!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Contact us" }));
            Assert.That(inUse.Outcome, Is.EqualTo("409 in_use"));
            Assert.That(isDefault.Outcome, Is.EqualTo("409 is_default"));
            Assert.That(defaultRow.Body!["items"]![0]!["isDefault"]!.GetValue<bool>(), Is.True);
            Assert.That(editor.Body!["defaultLayout"]!.GetValue<Guid>(), Is.EqualTo(key));
            Assert.That(deleted.Outcome, Is.EqualTo("204"), deleted.ToString());
            Assert.That(gone.Outcome, Is.EqualTo("404 email_layout_not_found"));
        });
    }

    [Test]
    public async Task ADuplicate_TakesTheFirstFreeAlias_WithTheSameDesign()
    {
        var created = await CallAsync(Designer, HttpMethod.Post, "email-layout", Brand);
        var key = created.Body!["key"]!.GetValue<Guid>();

        var first = await CallAsync(Designer, HttpMethod.Post, $"email-layout/{key}/duplicate", new { name = "Brand mail (copy)" });
        var second = await CallAsync(Designer, HttpMethod.Post, $"email-layout/{key}/duplicate", new { name = "Brand mail (another copy)" });
        var missing = await CallAsync(Designer, HttpMethod.Post, $"email-layout/{Guid.NewGuid()}/duplicate", new { name = "Nothing" });

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo("201"), first.ToString());
            Assert.That((first.Body!["alias"]!.GetValue<string>(), first.Body!["name"]!.GetValue<string>()), Is.EqualTo(("brand2", "Brand mail (copy)")));
            Assert.That(JsonNode.DeepEquals(first.Body!["design"], created.Body!["design"]), Is.True, "the same design");
            Assert.That(second.Body!["alias"]!.GetValue<string>(), Is.EqualTo("brand3"));
            Assert.That(missing.Outcome, Is.EqualTo("404 email_layout_not_found"));
        });
    }

    [Test]
    public async Task ThePreview_DrawsASampleEntryInTheDesign_InTheCultureAsked_ALogoOnTheSiteUnderItsAddress()
    {
        var english = await CallAsync(Designer, HttpMethod.Post, "email-layout/preview", new
        {
            design = new { look = "letter", logoUrl = "/media/abc/logo.png", header = new Dictionary<string, string> { [""] = "<p>Hello from Acme</p>", ["de"] = "<p>Hallo von Acme</p>" } },
            culture = "en-US",
        });
        var german = await CallAsync(Designer, HttpMethod.Post, "email-layout/preview", new
        {
            design = new { look = "card", header = new Dictionary<string, string> { [""] = "<p>Hello from Acme</p>", ["de"] = "<p>Hallo von Acme</p>" } },
            culture = "de-DE",
        });
        var refused = await CallAsync(Designer, HttpMethod.Post, "email-layout/preview", new { design = new { look = "card", light = new { accent = "red" } }, culture = "en-US" });

        Assert.Multiple(() =>
        {
            Assert.That(english.Outcome, Is.EqualTo("200"), english.ToString());
            var html = english.Body!["html"]!.GetValue<string>();
            Assert.That(html, Does.Contain("Alex Sample").And.Contain("Hello from Acme").And.Not.Contain("border:1px solid"), "the sample entry in the letter look");
            Assert.That(html, Does.Match("<img src=\"https?://[^\"/]+/media/abc/logo.png\""), "a path on the site under the site's address");
            Assert.That(english.Body!["problems"]!.AsArray(), Is.Empty);
            Assert.That(german.Body!["html"]!.GetValue<string>(), Does.Contain("Alex Beispiel").And.Contain("Hallo von Acme").And.Contain("border:1px solid"), "the sample in German, in the card look");
            Assert.That(refused.Body!["html"]!.GetValue<string>(), Is.Empty);
            Assert.That(refused.Body!["problems"]!.AsArray().Select(each => each!["path"]!.GetValue<string>()), Is.EqualTo(new[] { "$.design.light.accent" }));
        });
    }

    [Test]
    public async Task ATestMail_GoesToTheUserWhoAsks_InTheSavedLayout_AndIsAudited()
    {
        var created = await CallAsync(Designer, HttpMethod.Post, "email-layout", Brand);
        var key = created.Body!["key"]!.GetValue<Guid>();

        const string Address = "api-designer@jfb.test";
        var answer = await CallAsync(Designer, HttpMethod.Post, $"email-layout/{key}/test", new { culture = "de-DE" });
        var mail = (await Emails.GetAsync((await Emails.DueAsync(10)).Single()))!;
        var missing = await CallAsync(Designer, HttpMethod.Post, $"email-layout/{Guid.NewGuid()}/test");
        Sender.CanSend = false;
        var unconfigured = await CallAsync(Designer, HttpMethod.Post, $"email-layout/{key}/test");
        var audit = await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { EntityKey = key }, PageRequest.First(10));

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("202"), answer.ToString());
            Assert.That(answer.Body!["to"]!.GetValue<string>(), Is.EqualTo(Address));
            Assert.That((mail.Kind, mail.To), Is.EqualTo((EmailKind.Test, Address)), "the address of the user who asks, never one the request gives");
            Assert.That(mail.HtmlBody, Does.Contain("Hallo von Acme").And.Contain("Acme GmbH").And.Contain("#f0a5b8").And.Not.Contain("border:1px solid"), "the saved layout: its letter look, header, logo text and dark accent, in the culture asked");
            Assert.That(missing.Outcome, Is.EqualTo("404 email_layout_not_found"));
            Assert.That(unconfigured.Outcome, Is.EqualTo("409 email_not_configured"));
            Assert.That(audit.Items.Select(entry => entry.Action), Is.EquivalentTo(new[] { EmailLayoutLibrary.CreatedAction, EmailLayoutLibrary.TestSentAction }), "one mail queued, one recorded");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique<IEmailSender, ScriptedEmailSender>();
    }

    // The status and the path of each problem the answer names, in its order, such as "400 $.alias $.name".
    private static string Problems(ApiAnswer answer) =>
        string.Join(' ', [((int)answer.Status).ToString(System.Globalization.CultureInfo.InvariantCulture), .. answer.Body?["errors"]?.AsArray().Select(error => error!["path"]!.GetValue<string>()) ?? []]);

    // An email action to the team in the layout of this key.
    private static FormAction Mail(Guid layout) => new()
    {
        Key = Guid.NewGuid(),
        Type = EmailActionType.Alias,
        Name = "Mail",
        Settings = SettingsObject.Parse($$$"""{"to":["team@jfb.test"],"subject":{"":"New entry"},"body":{"":"<p>Hello</p>"},"layout":"{{{layout}}}"}"""),
    };

    // A draft form with these actions.
    private async Task<Guid> FormWithActionsAsync(string name, params FormAction[] actions)
    {
        var key = await FormAsync(name);
        var forms = GetRequiredService<IFormBuilderFormService>();
        var draft = (await forms.GetDraftAsync(key))!;
        _ = await forms.SaveDraftAsync(key, draft.Form.ConcurrencyVersion, draft.Definition! with { Actions = actions }, null, null);
        return key;
    }

    // An image in the media library with its file.
    private Guid Image(string name, string file)
    {
        var media = GetRequiredService<IMediaService>();
        var image = media.CreateMedia(name, Constants.System.Root, Constants.Conventions.MediaTypes.Image);
        image.SetValue(Constants.Conventions.Media.File, $$"""{"src":"{{file}}"}""");
        Assert.That(media.Save(image).Success, Is.True);
        return image.Key;
    }
}
