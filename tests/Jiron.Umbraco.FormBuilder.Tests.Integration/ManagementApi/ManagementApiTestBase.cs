using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The management API on a real site with Jiron Form Builder's tables (ADR-0050): the personas of ADR-0013, each signed
/// in through Umbraco's token endpoint the first time a test calls as them, and calls that read the answer as JSON.
/// Fixtures boot the runtime (<c>[UmbracoTest(Boot = true)]</c>), so the Management API answers.
/// </summary>
public abstract class ManagementApiTestBase : FormBuilderTestServerTestBase
{
    /// <summary>The personas, created before each test.</summary>
    internal Personas People { get; private set; } = null!;

    /// <summary>Calls as the personas.</summary>
    internal ManagementApiCaller Api { get; private set; } = null!;

    /// <summary>Creates the personas; they sign in when a test first calls as them.</summary>
    [SetUp]
    public async Task CreateThePersonasAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        People = await ActivatorUtilities.CreateInstance<AccessPersonas>(scope.ServiceProvider).CreateAsync();
        Api = new ManagementApiCaller(Client, Services, role => [AccessPersonas.GroupOf(role)]);
    }

    /// <summary>Calls the management API as <paramref name="role"/> (null: without a token), with a JSON body when one is given.</summary>
    internal Task<ApiAnswer> CallAsync(string? role, HttpMethod method, string path, object? body = null) => Api.CallAsync(role, method, path, body);

    /// <summary>Creates a folder through the folder service, at the root or in a folder.</summary>
    internal async Task<Guid> FolderAsync(string name, Guid? parentKey = null) =>
        (await GetRequiredService<IFormBuilderFolderService>().CreateAsync(new NewFolderRequest(name, parentKey))).Key;

    /// <summary>Creates a form through the form service, at the root or in a folder.</summary>
    internal async Task<Guid> FormAsync(string name, Guid? folderKey = null) =>
        (await GetRequiredService<IFormBuilderFormService>().CreateAsync(new NewFormRequest(name, FolderKey: folderKey))).Key;

    /// <summary>A text field labelled "Name", the field a form needs before it is published.</summary>
    internal static FormField NameField { get; } = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-0000000000a1"),
        Type = "text",
        Alias = "name",
        Label = new LocalizedText([new(string.Empty, "Name")]),
    };

    /// <summary>
    /// The definition with <see cref="NameField"/> in its first group: a blank form has no field to fill in, which the
    /// form check refuses to publish (ADR-0181).
    /// </summary>
    internal static FormDefinition Fillable(FormDefinition definition) =>
        definition with { Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [NameField] }] }, .. definition.Pages.Skip(1)] };

    /// <summary>Saves a form's draft with <see cref="NameField"/> in its first group, so it can be published.</summary>
    internal async Task FillAsync(Guid formKey)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var draft = (await forms.GetDraftAsync(formKey))!;
        _ = (await forms.SaveDraftAsync(formKey, draft.Form.ConcurrencyVersion, Fillable(draft.Definition!), null, null))!;
    }

    /// <summary>Restricts a folder and grants a persona's group permissions on it.</summary>
    internal async Task RestrictAsync(Guid folderKey, string role, FormBuilderPermissions permissions)
    {
        var access = GetRequiredService<IFormBuilderAccessService>();
        await access.RestrictFolderAsync(folderKey, true, null);
        await GrantAsync(People.GroupKeys[role], PermissionTarget.Folder, folderKey, permissions);
    }

    /// <summary>Grants a group permissions globally, on a folder or on a form, from none.</summary>
    internal Task GrantAsync(Guid groupKey, PermissionTarget target, Guid targetKey, FormBuilderPermissions permissions) =>
        GetRequiredService<IFormBuilderAccessService>().ChangeGrantAsync(new AccessGrantChange(groupKey, target, targetKey, FormBuilderPermissions.None, permissions, null));

    /// <summary>Puts a folder in the trash through the folder service.</summary>
    internal Task TrashFolderAsync(Guid folderKey) => GetRequiredService<IFormBuilderFolderService>().TrashAsync(folderKey, null);

    /// <summary>Puts a form in the trash through the form service.</summary>
    internal async Task TrashFormAsync(Guid formKey)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var form = await forms.GetAsync(formKey) ?? throw new InvalidOperationException("The form is missing.");
        await forms.TrashAsync(formKey, form.ConcurrencyVersion, null);
    }
}

/// <summary>
/// Calls the management API as named users, each an API user in its groups who signs in through Umbraco's token endpoint
/// on the first call (<see cref="BackOfficeApiUsers"/>), and reads the answer as JSON.
/// </summary>
/// <param name="client">The test server's client.</param>
/// <param name="services">The site's services.</param>
/// <param name="groupsOf">The user groups of each name.</param>
internal sealed class ManagementApiCaller(HttpClient client, IServiceProvider services, Func<string, string[]> groupsOf)
{
    /// <summary>The management API's base path.</summary>
    public const string BasePath = "/umbraco/jfb/management/api/v1/";

    private readonly Dictionary<string, string> _tokens = [];

    /// <summary>
    /// Calls as <paramref name="role"/> (null: without a token), with a JSON body when one is given. A path that starts with
    /// a slash is a whole path, for one of Umbraco's own endpoints; another is below <see cref="BasePath"/>.
    /// </summary>
    public async Task<ApiAnswer> CallAsync(string? role, HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path.StartsWith('/') ? path : BasePath + path, UriKind.Relative));
        if (role is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(role));
        }

        if (body is not null)
        {
            request.Content = body as HttpContent ?? JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new ApiAnswer(
            response.StatusCode,
            text.Length == 0 ? null : JsonNode.Parse(text),
            response.Headers.TryGetValues("Umb-Generated-Resource", out var created) ? created.Single() : null,
            response.Headers.RetryAfter?.Delta,
            response.Content.Headers.ContentType?.MediaType,
            text,
            response.Headers.CacheControl?.NoStore == true);
    }

    /// <summary>Calls as <paramref name="role"/> (null: without a token) for an answer that may be no JSON, such as a file; the caller reads and disposes it.</summary>
    public Task<HttpResponseMessage> SendAsync(string? role, string path) => SendAsync(role, HttpMethod.Get, path, body: null);

    /// <summary>Calls as <paramref name="role"/> with a JSON body when one is given, for an answer that may be no JSON, such as a file; the caller reads and disposes it.</summary>
    public async Task<HttpResponseMessage> SendAsync(string? role, HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, new Uri(BasePath + path, UriKind.Relative));
        if (role is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(role));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private async Task<string> TokenAsync(string role)
    {
        if (!_tokens.TryGetValue(role, out var token))
        {
            _tokens[role] = token = await BackOfficeApiUsers.SignInAsync(services, client, role, groupsOf(role));
        }

        return token;
    }
}

/// <summary>An answer of the management API.</summary>
/// <param name="Status">Its status.</param>
/// <param name="Body">Its JSON body, or null for none.</param>
/// <param name="Created">The key in <c>Umb-Generated-Resource</c>, or null.</param>
/// <param name="RetryAfter">The <c>Retry-After</c> delay, or null.</param>
/// <param name="MediaType">The body's media type, or null.</param>
/// <param name="Text">The body as it came.</param>
/// <param name="NoStore">Whether the answer says <c>Cache-Control: no-store</c>.</param>
internal sealed record ApiAnswer(HttpStatusCode Status, JsonNode? Body, string? Created, TimeSpan? RetryAfter, string? MediaType, string Text, bool NoStore = false)
{
    /// <summary>The ProblemDetails code, or null.</summary>
    public string? Code => Body is JsonObject problem && problem["code"] is JsonValue code ? code.GetValue<string>() : null;

    /// <summary>The status and the code, for messages and comparisons, such as <c>404 folder_not_found</c>.</summary>
    public string Outcome => Code is { } code ? $"{(int)Status} {code}" : ((int)Status).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public override string ToString() => $"{Outcome}: {Text}";
}
