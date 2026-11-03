using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Testing;

/// <summary>
/// <c>POST /jfb-testing/form-restriction</c> on a real test host in Development (ADR-0166): it gives a form its own restriction
/// and takes it away again, as the access service does, so the browser tests can show what the library says of a restricted
/// form before a screen sets it; a form the host does not have is not found, and no request from elsewhere reaches it.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class TestFormRestrictionEndpointTests : FormBuilderTestServerTestBase
{
    [Test]
    public async Task FormRestriction_RestrictsTheForm_AndLiftsTheRestrictionAgain()
    {
        var forms = Services.GetRequiredService<IFormBuilderFormService>();
        var form = await forms.CreateAsync(new NewFormRequest("Salaries"));

        var restricted = await TestEndpointsHost.PostAsync(Factory.Server, "/jfb-testing/form-restriction", $$"""{"form":"{{form.Key}}","restricted":true}""");
        var afterRestricting = await forms.GetAsync(form.Key);
        var lifted = await TestEndpointsHost.PostAsync(Factory.Server, "/jfb-testing/form-restriction", $$"""{"form":"{{form.Key}}","restricted":false}""");
        var afterLifting = await forms.GetAsync(form.Key);

        Assert.Multiple(() =>
        {
            Assert.That(restricted.Response.StatusCode, Is.EqualTo(StatusCodes.Status204NoContent));
            Assert.That(afterRestricting?.IsRestricted, Is.True);
            Assert.That(afterRestricting?.ConcurrencyVersion, Is.GreaterThan(form.ConcurrencyVersion), "a change like any other");
            Assert.That(lifted.Response.StatusCode, Is.EqualTo(StatusCodes.Status204NoContent));
            Assert.That(afterLifting?.IsRestricted, Is.False);
        });
    }

    [Test]
    public async Task FormRestriction_ForAFormTheHostDoesNotHave_IsNotFound()
    {
        var context = await TestEndpointsHost.PostAsync(Factory.Server, "/jfb-testing/form-restriction", $$"""{"form":"{{Guid.NewGuid()}}","restricted":true}""");

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    [Test]
    public async Task FormRestriction_ReadWithAGet_IsNotThere()
    {
        var context = await TestEndpointsHost.FromAsync(Factory.Server, "/jfb-testing/form-restriction", IPAddress.Loopback);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status404NotFound));
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration) => TestEndpointsHost.SwitchOn(configuration);

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder) => TestEndpointsHost.UseDevelopment(builder);
}
