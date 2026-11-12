using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.AI;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.AI.ManagementApi;

/// <summary>
/// The AI add-on's endpoints in the permission model (16.1's review), as the management API's matrix holds its own: each
/// declares exactly one access of Form Builder's own and no anonymous access, answers <c>401</c> without a token and
/// <c>403 no_section</c> to a user without the Forms section, whatever the route names.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed partial class AIPermissionMatrixTests : AITestBase
{
    [Test]
    public void EveryEndpoint_DeclaresOneAccess_AndNoneIsAnonymous()
    {
        var endpoints = Endpoints();
        var declarations = endpoints.Select(endpoint => (endpoint.Name, Count: endpoint.Action.MethodInfo.GetCustomAttributes<FormBuilderAccessAttribute>(true).Count())).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(endpoints, Has.Count.GreaterThanOrEqualTo(6), "the AI add-on's endpoints were found");
            Assert.That(declarations.Where(each => each.Count != 1).Select(each => each.Name), Is.Empty, "each carries exactly one declaration");
            Assert.That(endpoints.Where(endpoint => endpoint.Action.EndpointMetadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any()).Select(endpoint => endpoint.Name), Is.Empty, "none is anonymous");
        });
    }

    [Test]
    public async Task EachEndpoint_RefusesACallWithoutAToken_AndAUserWithoutTheFormsSection()
    {
        var answers = new List<string>();
        foreach (var endpoint in Endpoints())
        {
            var anonymous = await CallAsync(null, endpoint.Method, endpoint.Path, new { });
            var outsider = await CallAsync("outsider", endpoint.Method, endpoint.Path, new { });
            answers.Add($"{endpoint.Name}: {(int)anonymous.Status} / {outsider.Outcome}");
        }

        Assert.That(answers, Is.All.EndsWith($": {(int)HttpStatusCode.Unauthorized} / 403 no_section"), string.Join(Environment.NewLine, answers));
    }

    // Every endpoint of the AI add-on, its route's values filled with one key, so the access check answers, not routing.
    private List<(string Name, HttpMethod Method, string Path, ControllerActionDescriptor Action)> Endpoints()
    {
        var key = Guid.NewGuid().ToString();
        return [.. GetRequiredService<IApiDescriptionGroupCollectionProvider>().ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Where(description => description.ActionDescriptor is ControllerActionDescriptor action
                && action.ControllerTypeInfo.Assembly == typeof(FormBuilderAIComposer).Assembly
                && typeof(FormBuilderManagementApiController).IsAssignableFrom(action.ControllerTypeInfo))
            .Select(description => (
                Name: $"{description.HttpMethod} {description.RelativePath}",
                Method: new HttpMethod(description.HttpMethod!),
                Path: RouteValue().Replace(description.RelativePath!.Replace("v{version}", "v1", StringComparison.Ordinal), key)[Prefix.Length..],
                Action: (ControllerActionDescriptor)description.ActionDescriptor))
            .DistinctBy(endpoint => endpoint.Name)];
    }

    // What every management API route starts with, which the caller adds.
    private static string Prefix => "umbraco/jfb/management/api/v1/";

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteValue();
}
