using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The permission matrix (ADR-0013, ADR-0050): it finds every endpoint of the management API through the API explorer,
/// checks its declaration against <see cref="Declared"/>, written out by hand from ADR-0050's table, and calls each as
/// every persona that must be refused: without the Forms section, seeing the folder but lacking the permission, and
/// holding nothing on the folder or form. A new endpoint is called by itself, and fails until the table names it.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PermissionMatrixTests : ManagementApiTestBase
{
    /// <summary>What each endpoint needs: its permissions (none: any user of the section) and where, with the key's name.</summary>
    private static readonly Dictionary<string, (FormBuilderPermissions Needs, FormBuilderAccessResourceKind On, string? Key)> Declared = new()
    {
        ["GET tree/root"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET tree/children"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Folder, "parentKey"),
        ["GET folder/{key}"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Folder, "key"),
        ["POST folder"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "parentKey"),
        ["PUT folder/{key}"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "key"),
        ["PUT folder/{key}/move"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "key"),
        ["POST folder/{key}/trash"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "key"),
        ["POST folder/{key}/restore"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "key"),
        ["DELETE folder/{key}"] = (FormBuilderPermissions.Design | FormBuilderPermissions.DeleteEntries, FormBuilderAccessResourceKind.Folder, "key"),
        ["GET trash"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["DELETE trash"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET form/search"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET form/{key}"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/draft"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "folderKey"),
        ["PUT form/{key}/draft"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/publish"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/unpublish"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT form/{key}"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT form/{key}/move"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/trash"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/restore"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["DELETE form/{key}"] = (FormBuilderPermissions.Design | FormBuilderPermissions.DeleteEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/versions"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/versions/{number}"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/versions/compare"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/versions/{number}/restore"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT form/{key}/versions/{number}/pin"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT form/{key}/versions/{number}/comment"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
    };

    /// <summary>The reviewed exception list: endpoints without a declaration. Only the form picker (6.7) may join it.</summary>
    private static readonly string[] Exceptions = [];

    // The personas who may stand for "sees it but lacks the permission", in the order the test tries them.
    private static readonly string[] Seers = ["viewer", "designer", "entryHandler", "privacyOfficer", "noAccess"];

    private Guid _events;
    private Guid _hr;
    private Guid _spring;
    private Guid _payroll;

    [SetUp]
    public async Task CreateTheLibraryAsync()
    {
        var folders = GetRequiredService<IFormBuilderFolderService>();
        var access = GetRequiredService<IFormBuilderAccessService>();
        _events = (await folders.CreateAsync(new NewFolderRequest("Events"))).Key;
        _hr = (await folders.CreateAsync(new NewFolderRequest("HR"))).Key;
        await access.RestrictFolderAsync(_hr, true, null);
        await access.ChangeGrantAsync(new AccessGrantChange(People.GroupKeys["hr"], PermissionTarget.Folder, _hr, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries, null));
        var forms = GetRequiredService<IFormBuilderFormService>();
        _spring = (await forms.CreateAsync(new NewFormRequest("Spring", FolderKey: _events))).Key;
        _payroll = (await forms.CreateAsync(new NewFormRequest("Payroll", FolderKey: _hr))).Key;
    }

    [Test]
    public void EveryEndpoint_DeclaresItsAccess_AsTheTableSays()
    {
        var endpoints = Endpoints();

        Assert.Multiple(() =>
        {
            Assert.That(endpoints, Is.Not.Empty, "the API explorer lists the management API");
            Assert.That(endpoints.Select(endpoint => endpoint.Name), Is.EquivalentTo(Declared.Keys.Concat(Exceptions)), "every endpoint is in the table, and the table names no endpoint that is gone");
            Assert.That(Exceptions, Is.Empty, "the reviewed exception list grew");
            foreach (var endpoint in endpoints.Where(endpoint => !Exceptions.Contains(endpoint.Name)))
            {
                Assert.That(endpoint.Declarations, Has.Count.EqualTo(1), $"{endpoint.Name} carries exactly one declaration");
                if (endpoint.Declarations.Count == 1 && Declared.TryGetValue(endpoint.Name, out var expected))
                {
                    var declaration = endpoint.Declarations[0];
                    Assert.That((declaration.Permissions, declaration.On, declaration.Key), Is.EqualTo(expected), endpoint.Name);
                    Assert.That(declaration is FormBuilderSectionAttribute, Is.EqualTo(expected.Needs == FormBuilderPermissions.None), $"{endpoint.Name}: a section declaration exactly when it needs no permission");
                    if (declaration.Key is { } key)
                    {
                        Assert.That(endpoint.KeySource(key), Is.Not.Null, $"{endpoint.Name}: the key {key} names a parameter or a property of the body");
                    }
                }
            }
        });
    }

    [Test]
    public async Task EachEndpoint_RefusesAUserWithoutTheFormsSection()
    {
        var answers = new List<string>();
        foreach (var endpoint in Endpoints())
        {
            answers.Add($"{endpoint.Name}: {(await CallAsync("outsider", endpoint, Visible(endpoint))).Outcome}");
        }

        await WriteAsync(answers);
        Assert.That(answers, Is.All.EndsWith(": 403 no_section"), string.Join("\n", answers));
    }

    [Test]
    public async Task EachEndpointThatNeedsAPermission_RefusesAUserWhoSeesTheItemButLacksIt_NamingThePermission()
    {
        var answers = new List<string>();
        var expected = new List<string>();
        foreach (var endpoint in Endpoints().Where(endpoint => Declared.TryGetValue(endpoint.Name, out var declared) && declared.Needs != FormBuilderPermissions.None))
        {
            var needs = Declared[endpoint.Name].Needs;
            var on = Declared[endpoint.Name].On;
            var role = Seers.First(seer => Sees(seer, on) && (Held(seer, on) & needs) != needs);
            var missing = PermissionRules.Each.First(permission => (needs & permission) != FormBuilderPermissions.None && (Held(role, on) & permission) == FormBuilderPermissions.None);
            var answer = await CallAsync(role, endpoint, Visible(endpoint));
            answers.Add($"{endpoint.Name} as {role}: {answer.Outcome} {answer.Body?["permission"]?.GetValue<string>()}");
            expected.Add($"{endpoint.Name} as {role}: 403 forbidden {PermissionRules.NameOf(missing)}");
        }

        await WriteAsync(answers);
        Assert.That(answers, Is.Not.Empty);
        Assert.That(answers, Is.EqualTo(expected));
    }

    [Test]
    public async Task EachEndpointOnAFolderOrForm_AnswersNotFound_ToAUserWhoHoldsNothingOnIt_AndForAKeyThatIsNone()
    {
        var answers = new List<string>();
        var expected = new List<string>();
        foreach (var endpoint in Endpoints().Where(endpoint => Declared.TryGetValue(endpoint.Name, out var declared) && declared.On != FormBuilderAccessResourceKind.Global))
        {
            var code = Declared[endpoint.Name].On == FormBuilderAccessResourceKind.Form ? "form_not_found" : "folder_not_found";
            answers.Add($"{endpoint.Name} as designer on HR: {(await CallAsync("designer", endpoint, Declared[endpoint.Name].On == FormBuilderAccessResourceKind.Form ? _payroll : _hr)).Outcome}");
            expected.Add($"{endpoint.Name} as designer on HR: 404 {code}");
            answers.Add($"{endpoint.Name} as admin on none: {(await CallAsync("admin", endpoint, Guid.NewGuid())).Outcome}");
            expected.Add($"{endpoint.Name} as admin on none: 404 {code}");
        }

        await WriteAsync(answers);
        Assert.That(answers, Is.Not.Empty);
        Assert.That(answers, Is.EqualTo(expected));
    }

    [Test]
    public async Task EachEndpointForTheWholeSection_AnswersAUserOfTheSectionWithoutGrants()
    {
        var answers = new List<string>();
        foreach (var endpoint in Endpoints().Where(endpoint => Declared.TryGetValue(endpoint.Name, out var declared) && declared == (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null)))
        {
            answers.Add($"{endpoint.Name}: {(await CallAsync("noAccess", endpoint, _events)).Outcome}");
        }

        await WriteAsync(answers);
        Assert.That(answers, Is.Not.Empty);
        Assert.That(answers, Is.All.EndsWith(": 200"), string.Join("\n", answers));
    }

    // What a persona holds globally (its own group's grants, ADR-0013) or on Events, an unrestricted folder at the root,
    // which holds the global grants that work in folders.
    private static FormBuilderPermissions Held(string role, FormBuilderAccessResourceKind on)
    {
        var grants = AccessPersonas.Groups.FirstOrDefault(group => group.Role == role).Grants;
        return on == FormBuilderAccessResourceKind.Global ? grants : grants & PermissionRules.FolderLevel;
    }

    private static bool Sees(string role, FormBuilderAccessResourceKind on) => on == FormBuilderAccessResourceKind.Global || Held(role, on) != FormBuilderPermissions.None;

    // Names each endpoint the test called, with its answer, in the test's output.
    private static async Task WriteAsync(List<string> answers)
    {
        foreach (var answer in answers)
        {
            await TestContext.Out.WriteLineAsync(answer);
        }
    }

    private Task<ApiAnswer> CallAsync(string role, ManagementApiEndpoint endpoint, Guid key) => Api.CallAsync(role, endpoint, key);

    // The key that Events stands for: Events itself, or its form Spring for an endpoint on a form.
    private Guid Visible(ManagementApiEndpoint endpoint) =>
        Declared.TryGetValue(endpoint.Name, out var declared) && declared.On == FormBuilderAccessResourceKind.Form ? _spring : _events;

    private List<ManagementApiEndpoint> Endpoints() => ManagementApiEndpoints.All(Services);
}
