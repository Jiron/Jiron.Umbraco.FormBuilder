using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Access;

/// <summary>
/// Every policy of the management API as each persona of ADR-0013, signed in through Umbraco's token endpoint
/// (ADR-0049): globally, at the root, and against a folder and a form outside and inside a restricted folder. The
/// library:
/// <code>
/// Events: Spring
/// HR (restricted, granted to the HR team): Payroll (granted to the entry handlers on its own)
/// </code>
/// What each persona holds and sees is written out below by hand from ADR-0013's table, not computed.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class AccessPolicyTests : FormBuilderTestServerTestBase
{
    private const FormBuilderPermissions D = FormBuilderPermissions.Design;
    private const FormBuilderPermissions MA = FormBuilderPermissions.ManageActions;
    private const FormBuilderPermissions V = FormBuilderPermissions.ViewEntries;
    private const FormBuilderPermissions VS = FormBuilderPermissions.ViewSensitive;
    private const FormBuilderPermissions E = FormBuilderPermissions.EditEntries;
    private const FormBuilderPermissions DE = FormBuilderPermissions.DeleteEntries;
    private const FormBuilderPermissions X = FormBuilderPermissions.Export;
    private const FormBuilderPermissions ML = FormBuilderPermissions.ManageLibrary;
    private const FormBuilderPermissions PT = FormBuilderPermissions.PrivacyTools;
    private const FormBuilderPermissions None = FormBuilderPermissions.None;
    private const FormBuilderPermissions All = PermissionRules.All;
    private const FormBuilderPermissions FolderLevel = PermissionRules.FolderLevel;

    // What each persona holds on each resource, and whether it sees it (lists it): global, root, events, spring, hr, payroll.
    private static readonly Dictionary<string, (FormBuilderPermissions Held, bool Seen)[]> Expected = new()
    {
        ["admin"] = [(All, true), (FolderLevel, true), (FolderLevel, true), (FolderLevel, true), (FolderLevel, true), (FolderLevel, true)],
        ["designer"] = [(D | MA | ML, true), (D | MA, true), (D | MA, true), (D | MA, true), (None, false), (None, false)],
        ["entryHandler"] = [(V | E | X, true), (V | E | X, true), (V | E | X, true), (V | E | X, true), (None, true), (V, true)],
        ["viewer"] = [(V, true), (V, true), (V, true), (V, true), (None, false), (None, false)],
        ["privacyOfficer"] = [(PT | V | VS | DE, true), (V | VS | DE, true), (V | VS | DE, true), (V | VS | DE, true), (None, false), (None, false)],
        ["noAccess"] = [(None, true), (None, true), (None, false), (None, false), (None, false), (None, false)],
        ["hr"] = [(None, true), (None, true), (None, false), (None, false), (V | E, true), (V | E, true)],
        ["outsider"] = [(None, false), (None, false), (None, false), (None, false), (None, false), (None, false)],
    };

    private static readonly string[] ResourceNames = ["global", "root", "events", "spring", "hr", "payroll"];

    private readonly Dictionary<string, string> _tokens = [];
    private AccessProbeResource[] _resources = [];

    private IFormBuilderAccessService Access => GetRequiredService<IFormBuilderAccessService>();

    [SetUp]
    public async Task CreateTheLibraryAndSignInAsync()
    {
        // The people need a scope: the test server checks that nothing scoped comes from the root.
        await using var scope = Services.CreateAsyncScope();
        var personas = await ActivatorUtilities.CreateInstance<AccessPersonas>(scope.ServiceProvider).CreateAsync();
        var folders = GetRequiredService<IFormBuilderFolderService>();
        var forms = GetRequiredService<IFormBuilderFormService>();
        var events = await folders.CreateAsync(new NewFolderRequest("Events"));
        var spring = await forms.CreateAsync(new NewFormRequest("Spring", FolderKey: events.Key));
        var hr = await folders.CreateAsync(new NewFolderRequest("HR"));
        await Access.RestrictFolderAsync(hr.Key, true, null);
        await Access.ChangeGrantAsync(new AccessGrantChange(personas.GroupKeys["hr"], PermissionTarget.Folder, hr.Key, None, V | E, null));
        var payroll = await forms.CreateAsync(new NewFormRequest("Payroll", FolderKey: hr.Key));
        await Access.ChangeGrantAsync(new AccessGrantChange(personas.GroupKeys["entryHandler"], PermissionTarget.Form, payroll.Key, None, V, null));
        _resources =
        [
            new("global", "global"),
            new("root", "folder"),
            new("events", "folder", events.Key),
            new("spring", "form", spring.Key),
            new("hr", "folder", hr.Key),
            new("payroll", "form", payroll.Key),
        ];

        foreach (var role in AccessPersonas.Roles)
        {
            _tokens[role] = await BackOfficeApiUsers.SignInAsync(Services, Client, role, AccessPersonas.GroupOf(role));
        }
    }

    [TestCaseSource(nameof(Roles))]
    public async Task EveryPolicy_AsThePersona_AnswersAsItsGrantsSay_GloballyAndOnFoldersAndForms(string role)
    {
        using var request = Request(HttpMethod.Post, "check", role);
        request.Content = JsonContent.Create(new AccessProbeRequest(_resources));
        using var response = await Client.SendAsync(request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());

        var answers = await response.Content.ReadFromJsonAsync<Dictionary<string, Dictionary<string, string>>>();

        Assert.Multiple(() =>
        {
            for (var i = 0; i < ResourceNames.Length; i++)
            {
                Assert.That(answers![ResourceNames[i]], Is.EqualTo(OutcomesOf(role, Expected[role][i])), ResourceNames[i]);
            }
        });
    }

    [Test]
    public async Task APolicyOnTheAttribute_TakesTheBackofficesToken_AndChecksGlobally()
    {
        using var anonymous = await Client.GetAsync(new Uri(AccessProbeController.Route + "/section", UriKind.Relative));

        Assert.Multiple(async () =>
        {
            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "no token");
            Assert.That(await StatusAsync("section", "noAccess"), Is.EqualTo(HttpStatusCode.NoContent), "any user of the section");
            Assert.That(await StatusAsync("section", "outsider"), Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await StatusAsync("design", "designer"), Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await StatusAsync("design", "admin"), Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await StatusAsync("design", "viewer"), Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(await StatusAsync("design", "outsider"), Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    [Test]
    public void ThePersonasTable_CoversEveryPersona_AndEveryResource()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Expected.Keys, Is.EquivalentTo(AccessPersonas.Roles));
            Assert.That(Expected.Values.Select(row => row.Length), Is.All.EqualTo(ResourceNames.Length));
            Assert.That(_resources.Select(resource => resource.Name), Is.EqualTo(ResourceNames));
        });
    }

    /// <inheritdoc />
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddMvcCore().AddApplicationPart(typeof(AccessProbeController).Assembly);
    }

    private static string[] Roles() => AccessPersonas.Roles;

    // The answer each policy should give: the section's to any user of it; a permission's when the user holds it on the
    // resource, otherwise the denial that 4.7 turns into 403 or 404 (ADR-0049).
    private static Dictionary<string, string> OutcomesOf(string role, (FormBuilderPermissions Held, bool Seen) expected)
    {
        var section = role != "outsider";
        var outcomes = new Dictionary<string, string> { ["section"] = section ? "allowed" : "NoSection:section" };
        foreach (var permission in PermissionRules.Each)
        {
            var name = PermissionRules.NameOf(permission);
            outcomes[name] = !section ? $"NoSection:{name}"
                : (expected.Held & permission) != 0 ? "allowed"
                : expected.Seen ? $"MissingPermission:{name}"
                : $"NotFound:{name}";
        }

        return outcomes;
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string role)
    {
        var request = new HttpRequestMessage(method, new Uri($"{AccessProbeController.Route}/{path}", UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens[role]);
        return request;
    }

    private async Task<HttpStatusCode> StatusAsync(string path, string role)
    {
        using var request = Request(HttpMethod.Get, path, role);
        using var response = await Client.SendAsync(request);
        return response.StatusCode;
    }
}
