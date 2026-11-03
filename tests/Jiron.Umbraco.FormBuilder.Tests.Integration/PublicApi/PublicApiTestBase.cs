using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Cache;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The public API on a real site with Jiron Form Builder's tables (ADR-0084), with a runtime cache that keeps what it is
/// given, as a site's does: forms published through the form service, and calls that read the answer as JSON.
/// Fixtures boot the runtime (<c>[UmbracoTest(Boot = true)]</c>), so the site routes requests.
/// </summary>
public abstract class PublicApiTestBase : FormBuilderTestServerTestBase
{
    /// <summary>The public API's root with the default base path.</summary>
    public const string Root = "/jfb/api/v1";

    private static readonly System.Text.RegularExpressions.Regex PostsChallenge = new(
        "/forms/(?<form>[0-9a-fA-F-]{36})/(?<endpoint>entries|drafts|validate|receipt|uploads)(\\?|$)",
        System.Text.RegularExpressions.RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private readonly Dictionary<string, string> _challenges = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Form, string Submission), string> _solved = [];

    /// <summary>The user the tests' changes are made by.</summary>
    private protected static readonly Guid Editor = Guid.Parse("7d1c3a52-31f4-4e57-9d2e-61b0f6a1c084");

    /// <summary>The form service.</summary>
    private protected IFormBuilderFormService Forms => GetRequiredService<IFormBuilderFormService>();

    /// <summary>The form service as the site has it, whose reads of published forms a test can count, fail or hold.</summary>
    private protected WatchedForms Watched => GetRequiredService<WatchedForms>();

    /// <summary>A text field labelled "Name", the field most tests' forms hold.</summary>
    private protected static FormField NameField { get; } = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000000084"),
        Type = "text",
        Alias = "name",
        Label = new LocalizedText([new(string.Empty, "Name"), new("de", "Name (de)")]),
    };

    /// <summary>
    /// Creates a form named <paramref name="name"/> and publishes its first group with <paramref name="fields"/>, served
    /// to headless sites unless <paramref name="headless"/> says otherwise; <paramref name="change"/> changes the definition
    /// before the save.
    /// </summary>
    private protected async Task<Guid> PublishAsync(string name = "Contact us", bool headless = true, Func<FormDefinition, FormDefinition>? change = null, params FormField[] fields)
    {
        var created = await Forms.CreateAsync(new NewFormRequest(name, UserKey: Editor));
        await SaveAndPublishAsync(created.Key, definition => (change ?? (d => d))(Headless(WithFields(definition, fields.Length == 0 ? [NameField] : fields), headless)));
        return created.Key;
    }

    /// <summary>Saves a new draft of a form, made from its current one, and publishes it.</summary>
    private protected async Task SaveAndPublishAsync(Guid key, Func<FormDefinition, FormDefinition> change)
    {
        var draft = (await Forms.GetDraftAsync(key))!;
        var saved = (await Forms.SaveDraftAsync(key, draft.Form.ConcurrencyVersion, change(draft.Definition!), null, Editor))!;
        _ = (await Forms.PublishAsync(key, saved.Form.ConcurrencyVersion, null, Editor))!;
    }

    /// <summary>The definition with these fields in its first group.</summary>
    private protected static FormDefinition WithFields(FormDefinition definition, params FormField[] fields) =>
        definition with { Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = fields }] }] };

    /// <summary>The definition, served to headless sites or not.</summary>
    private protected static FormDefinition Headless(FormDefinition definition, bool headless = true) =>
        definition with { Settings = definition.Settings with { Developer = definition.Settings.Developer with { Headless = headless } } };

    /// <summary>Sends a request to the site, after <paramref name="prepare"/> sets its headers or body.</summary>
    private protected async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Action<HttpRequestMessage>? prepare = null)
    {
        using var request = new HttpRequestMessage(method, path);
        prepare?.Invoke(request);
        var challenged = await AddChallengeAsync(request);
        var response = await Client.SendAsync(request);
        if (challenged is { Endpoint: "entries" or "drafts" } && response.StatusCode == HttpStatusCode.OK)
        {
            // A solved challenge has made its entry: a retry of that submission holds it, the id the server made too, and
            // anything else asks anew.
            _challenges.Remove(challenged.Form);
            var made = (await JsonAsync(response))["receipt"]?["submissionId"]?.GetValue<string>();
            foreach (var submission in new[] { challenged.Submission, made }.OfType<string>())
            {
                _solved[(challenged.Form, submission)] = challenged.Token;
            }
        }

        return response;
    }

    /// <summary>Whether this fixture signs its requests with the test server key, because its tests are of bodies or files.</summary>
    private protected bool SendsWithServerKey { get; set; }

    /// <summary>Asks the form's challenge endpoint for a challenge, as a page does when its visitor first touches the form.</summary>
    private protected async Task<string> ChallengeAsync(Guid form)
    {
        using var answer = await Client.PostAsync($"{Root}/forms/{form}/challenge", null);
        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
        return (await JsonAsync(answer))["challenge"]!.GetValue<string>();
    }

    // What a page does for the requests a test writes without one: it holds one challenge per form, posts it with each page
    // check, upload and submission, and asks for a new one once it has made an entry. A test of the challenge writes its
    // own, even a null one, and a request with a server key has none.
    private async Task<Challenged?> AddChallengeAsync(HttpRequestMessage request)
    {
        if (request.Method != HttpMethod.Post || request.Headers.Authorization is not null || request.Headers.Contains("Authorization")
            || request.RequestUri is null || PostsChallenge.Match(request.RequestUri.OriginalString) is not { Success: true } match)
        {
            return null;
        }

        if (SendsWithServerKey)
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Jfb-Key " + TestServerKey);
            return null;
        }

        var form = match.Groups["form"].Value;
        var endpoint = match.Groups["endpoint"].Value;
        if (request.Content is MultipartFormDataContent parts)
        {
            if (endpoint != "uploads" || parts.Any(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "challenge") || await TokenAsync(form, null) is not { } token)
            {
                return null;
            }

            var added = new MultipartFormDataContent { { new StringContent(token), "challenge" } };
            foreach (var part in parts)
            {
                added.Add(part);
            }

            request.Content = added;
            return null;
        }

        if (request.Content is null || request.Content.Headers.ContentType?.MediaType != "application/json")
        {
            return null;
        }

        JsonObject? body;
        var text = await request.Content.ReadAsStringAsync();
        try
        {
            body = JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        if (body is null || body.ContainsKey("challenge"))
        {
            return null;
        }

        string? submission;
        try
        {
            submission = body["submissionId"]?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        if (await TokenAsync(form, submission) is not { } held)
        {
            return null;
        }

        body["challenge"] = held;
        var contentType = request.Content.Headers.ContentType;
        try
        {
            request.Content = new StringContent(body.ToJsonString());
        }
        catch (InvalidOperationException)
        {
            // A body with a lone surrogate, which the endpoint refuses before it looks for a challenge.
            return null;
        }

        request.Content.Headers.ContentType = contentType;
        return new Challenged(form, endpoint, submission, held);
    }

    // The challenge a page holds for a form, or the one that made this submission's entry; null when the form gives none.
    private async Task<string?> TokenAsync(string form, string? submission)
    {
        if (submission is not null && _solved.TryGetValue((form, submission), out var solved))
        {
            return solved;
        }

        if (_challenges.TryGetValue(form, out var held))
        {
            return held;
        }

        using var answer = await Client.PostAsync($"{Root}/forms/{form}/challenge", null);
        if (answer.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        return _challenges[form] = (await JsonAsync(answer))["challenge"]!.GetValue<string>();
    }

    /// <summary>Gets a path of the site.</summary>
    private protected Task<HttpResponseMessage> GetAsync(string path, Action<HttpRequestMessage>? prepare = null) => SendAsync(HttpMethod.Get, path, prepare);

    /// <summary>The answer's body as JSON.</summary>
    private protected static async Task<JsonNode> JsonAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync()) ?? throw new InvalidOperationException("The answer has no JSON.");

    /// <summary>The status and the problem code of a refusal, such as <c>404 form_not_found</c>.</summary>
    private protected static async Task<string> RefusalAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var code = body.Length > 0 && JsonNode.Parse(body) is JsonObject problem ? problem["code"]?.GetValue<string>() : null;
        return $"{(int)response.StatusCode} {code ?? "(no code)"}";
    }

    /// <summary>The value of a header of the answer or its content, or null.</summary>
    private protected static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values) ? string.Join(", ", values) : null;

    /// <summary>Whether the answer is a success.</summary>
    private protected static bool Succeeded(HttpResponseMessage response) => response.StatusCode == HttpStatusCode.OK;

    private sealed record Challenged(string Form, string Endpoint, string? Submission, string Token);

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique(new AppCaches(new ObjectCacheAppCache(), NoAppCache.Instance, new IsolatedCaches(_ => NoAppCache.Instance)));
        builder.Services.AddSingleton(provider => WatchedForms.Over(ActivatorUtilities.CreateInstance<FormBuilderFormService>(provider)));
        builder.Services.AddUnique<IFormBuilderFormService>(provider => provider.GetRequiredService<WatchedForms>() as IFormBuilderFormService ?? throw new InvalidOperationException("The proxy is the service."));
    }
}
