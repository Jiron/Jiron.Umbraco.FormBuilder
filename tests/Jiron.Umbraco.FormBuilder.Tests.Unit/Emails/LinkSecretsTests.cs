using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Emails;

/// <summary>The codes of the confirmation and resume links leave no stored copy of a sent mail (ADR-0226).</summary>
public sealed class LinkSecretsTests
{
    [Test]
    public void TheConfirmationLinksCode_IsRemoved_InHtmlAndText_AndNothingElse()
    {
        const string Html = """<p><a href="https://site.jfb.test/jfb/api/v1/verify?t=CfDJ8%2BabC-_x.y%3D%3D">Confirm</a></p><p><a href="https://site.jfb.test/privacy?t=keep">Privacy</a></p>""";
        const string Text = "Confirm (https://site.jfb.test/jfb/api/v1/verify?t=CfDJ8%2BabC-_x.y%3D%3D)";

        Assert.Multiple(() =>
        {
            Assert.That(LinkSecrets.Mask(Html), Is.EqualTo("""<p><a href="https://site.jfb.test/jfb/api/v1/verify?t=removed">Confirm</a></p><p><a href="https://site.jfb.test/privacy?t=keep">Privacy</a></p>"""));
            Assert.That(LinkSecrets.Mask(Text), Is.EqualTo("Confirm (https://site.jfb.test/jfb/api/v1/verify?t=removed)"), "up to the bracket that ends it");
        });
    }

    [Test]
    public void TheResumeLinksCode_IsRemoved_AndTheLinkStillLeadsToItsPage()
    {
        Assert.That(
            LinkSecrets.Mask("""<a href="https://site.jfb.test/apply#jfb-resume=q83v_-AbC9">Continue</a> https://site.jfb.test/apply#jfb-resume=q83v_-AbC9"""),
            Is.EqualTo("""<a href="https://site.jfb.test/apply#jfb-resume=removed">Continue</a> https://site.jfb.test/apply#jfb-resume=removed"""));
    }

    [TestCase("Verification", true)]
    [TestCase("Resume", true)]
    [TestCase("Action", false)]
    [TestCase("Forward", false)]
    public void OnlyTheLinkMails_AreMasked(string kind, bool holds) => Assert.That(LinkSecrets.Holds(Enum.Parse<EmailKind>(kind)), Is.EqualTo(holds));
}
