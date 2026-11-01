using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>The captcha for the designer on a site where a provider has its keys (ROADMAP 7.2b, ADR-0142): available, with no reason.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FieldTypeApiCaptchaTests : ManagementApiTestBase
{
    [Test]
    public async Task OnceAProviderHasItsKeys_TheCaptchaIsAvailable()
    {
        var body = (await CallAsync("designer", HttpMethod.Get, "field-type")).Body!;

        var availability = body["types"]!.AsArray().Single(type => (string)type!["alias"]! == "captcha")!["availability"]!;

        Assert.Multiple(() =>
        {
            Assert.That((bool)availability["isAvailable"]!, Is.True);
            Assert.That(availability["reason"], Is.Null);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.Configure<FormBuilderOptions>(options =>
        {
            options.Captcha.Hcaptcha.SiteKey = "JFB_FAKE_captcha_site_key";
            options.Captcha.Hcaptcha.SecretKey = "JFB_FAKE_captcha_secret_key";
        });
    }
}
