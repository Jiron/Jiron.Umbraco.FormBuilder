using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>A test mail through the management API (ROADMAP 8.2, ADR-0159): it reaches the user who asks, and nobody else.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class TestEmailApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private IEmailRepository Emails => GetRequiredService<IEmailRepository>();

    private ScriptedEmailSender Sender => (ScriptedEmailSender)GetRequiredService<IEmailSender>();

    [Test]
    public async Task ATestMail_IsQueuedToTheUserWhoAsks_WhateverTheRequestSays()
    {
        var answer = await Api.CallAsync(Admin, HttpMethod.Post, "email/test", new { to = "someone-else@jfb.test", layout = EmailLayouts.LetterKey });

        var due = await Emails.DueAsync(10);
        var mail = (await Emails.GetAsync(due.Single()))!;
        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("202"), answer.ToString());
            Assert.That(answer.Body!["to"]!.GetValue<string>(), Is.EqualTo(mail.To));
            Assert.That(mail.Kind, Is.EqualTo(EmailKind.Test));
            Assert.That(mail.To, Does.Not.Contain("someone-else"));
            Assert.That(mail.RecipientKey, Is.Null);
            Assert.That(mail.HtmlBody, Does.Not.Contain("border:1px solid"), "the layout it was asked for");
        });
    }

    [Test]
    public async Task ATestMail_WithoutARequest_ShowsTheSitesOwn()
    {
        var answer = await Api.CallAsync(Admin, HttpMethod.Post, "email/test");

        var mail = (await Emails.GetAsync((await Emails.DueAsync(10)).Single()))!;
        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("202"), answer.ToString());
            Assert.That(mail.HtmlBody, Does.Contain("border:1px solid"));
        });
    }

    [Test]
    public async Task WhileUmbracoCannotSend_ATestMailIsRefused_AndNothingIsQueued()
    {
        Sender.CanSend = false;

        var answer = await Api.CallAsync(Admin, HttpMethod.Post, "email/test");

        Assert.Multiple(async () =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("409 email_not_configured"), answer.ToString());
            Assert.That(await Emails.DueAsync(10), Is.Empty);
        });
    }

    [Test]
    public async Task ATestMail_NeedsTheRightToManageSettings()
    {
        var answer = await Api.CallAsync("noAccess", HttpMethod.Post, "email/test");

        Assert.Multiple(async () =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("403 forbidden"), answer.ToString());
            Assert.That(await Emails.DueAsync(10), Is.Empty);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique<IEmailSender, ScriptedEmailSender>();
    }
}
