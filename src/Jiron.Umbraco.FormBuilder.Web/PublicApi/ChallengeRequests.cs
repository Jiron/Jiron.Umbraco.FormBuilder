using System.Text.Json;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// What the challenge endpoint answers (ADR-0012, ADR-0138): a signed challenge and what cached markup cannot carry, the
/// places left on the form's own options and the member values its rules use. A form for members gives one only to a
/// member it admits, bound to that member, and never to a server key. The preview's challenge is for a test member, with
/// no member, and asks no captcha and no puzzle: its ticket shows who asks, as a server key does, and a captcha's
/// provider takes no opaque origin (ADR-0203).
/// </summary>
/// <param name="tokens">The challenges.</param>
/// <param name="entries">The entries, whose counts say how many places are left.</param>
/// <param name="forms">The forms' rows.</param>
/// <param name="types">The field types, which say whose options can have places.</param>
/// <param name="cascading">The options of a field from a list or another source.</param>
/// <param name="pages">The site's default culture, which a list's labels would be in, and the site of the page.</param>
/// <param name="protection">The settings that protect a form, whose level the challenge carries.</param>
/// <param name="captchas">The captcha providers.</param>
internal sealed class ChallengeRequests(ChallengeTokens tokens, IEntryRepository entries, IFormRepository forms, FieldTypeRegistry types, CascadingOptions cascading, FormPages pages, SubmissionProtection protection, FormBuilderCaptchaProviderCollection captchas)
{
    /// <summary>The most bytes the endpoint's body holds: the longest earlier challenge, with room for escapes.</summary>
    public const int MaxBodyBytes = 16_384;

    private static readonly JsonSerializerOptions Json = FrozenOptions.Of(new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });

    /// <summary>Whether a form for members takes this request: from a member it admits, and never from a server key.</summary>
    /// <param name="http">The request.</param>
    /// <param name="availability">The form's availability, which says for whom it is.</param>
    public static async Task<bool> AdmitsAsync(HttpContext http, FormAvailabilitySettings availability)
    {
        ArgumentNullException.ThrowIfNull(http);
        return ServerKeyRequest.NameOf(http) is null && await http.RequestServices.GetRequiredService<FormMembers>().GateAsync(availability) is null;
    }

    /// <summary>Issues a challenge for a published form, or refuses.</summary>
    /// <param name="http">The request.</param>
    /// <param name="form">The published form.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    public async Task<IActionResult> IssueAsync(HttpContext http, PublishedForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(form);
        var body = await EntryRequests.ReadOptionalAsync(http, MaxBodyBytes, cancellationToken);
        if (body.Refusal is { } refusal)
        {
            return refusal;
        }

        var invalid = new List<string>();
        var (previous, content) = body.Bytes.Length == 0 ? default : EntryBodies.ReadChallengeRequest(body.Bytes, invalid);
        if (invalid.Count > 0)
        {
            return FormBuilderPublicProblems.InvalidRequest([.. invalid]);
        }

        var preview = PreviewRequest.Of(http) is not null;
        var members = http.RequestServices.GetRequiredService<FormMembers>();
        var member = preview ? null : await members.KeyAsync();
        var definition = form.Definition;
        var forMembers = definition.Settings.Availability.Access != MemberAccess.Anyone;
        if (forMembers && !preview && !await AdmitsAsync(http, definition.Settings.Availability))
        {
            return FormBuilderPublicProblems.MemberRequired();
        }

        // The level comes from the settings that protect the form, as a submission's does: only a server's own request skips the puzzle.
        var site = await protection.SettingsOfAsync(form.Form.Key, await pages.PageAsync(content), cancellationToken);
        var proofBits = !preview && site.LevelOf(definition) == ProtectionLevel.ProofOfWork ? site.ProofBits : 0;
        var challenge = tokens.Issue(form.Form.Key, forMembers ? member : null, previous, proofBits);
        var model = new ChallengeResponseModel
        {
            Challenge = challenge,
            MinFillSeconds = FormChallenges.MinFillSeconds,
            Captcha = preview ? null : Captcha(site.CaptchaOf(definition), site),
            ProofOfWork = proofBits == 0 ? null : new ChallengeProofResponseModel { Nonce = ProofOfWork.NonceOf(tokens.Read(challenge, form.Form.Key, TimeSpan.Zero)!), Bits = proofBits },
            Places = await PlacesAsync(form, cancellationToken),
            Member = member is null ? null : await MemberAsync(members, definition),
        };
        return new FileContentResult(JsonSerializer.SerializeToUtf8Bytes(model, Json), "application/json; charset=utf-8");
    }

    // What a page needs to show the form's captcha, and no secret; nothing when its provider cannot be shown.
    private ChallengeCaptchaResponseModel? Captcha(CaptchaChoice? chosen, ProtectionSettings site) =>
        captchas.Find(chosen?.Provider)?.Client is { } client
            ? new ChallengeCaptchaResponseModel
            {
                Provider = chosen!.Provider,
                SiteKey = client.SiteKey,
                ScriptAddress = client.ScriptAddress,
                Action = client.Action,
                Consent = JsonNamingPolicy.CamelCase.ConvertName(site.CaptchaConsent.ToString()),
                Category = site.CaptchaConsent == CaptchaConsentMode.Cookiebot ? site.CookiebotCategory : null,
            }
            : null;

    // The places left, by the field's key and the option's value, of every option known without a visitor's answers: the
    // form's own, and those of a list or a source no parent's answer filters (ADR-0138).
    private async Task<Dictionary<string, Dictionary<string, int>>?> PlacesAsync(PublishedForm form, CancellationToken cancellationToken)
    {
        var culture = await pages.CultureAsync(null);
        var limited = new List<(OptionPlace Option, int Places)>();
        foreach (var field in form.Definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)))
        {
            limited.AddRange((await LimitsAsync(form.Definition, field, culture, cancellationToken)).Select(limit => (new OptionPlace(field.Key, limit.Value), Math.Max(0, limit.Places))));
        }

        if (limited.Count == 0 || await forms.GetAsync(form.Form.Key, cancellationToken) is not { } row)
        {
            return null;
        }

        var taken = await entries.GetPlacesTakenAsync(row.Id, [.. limited.Select(limit => limit.Option).Distinct()], cancellationToken);
        var places = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (option, most) in limited)
        {
            var field = DefinitionLogic.Id(option.FieldKey);
            if (!places.TryGetValue(field, out var left))
            {
                places[field] = left = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            left[option.Value] = Math.Max(0, most - taken.GetValueOrDefault(option));
        }

        return places;
    }

    // A field's options that have places. A source that fails or waits for a parent's answer gives none, and the server
    // still refuses a full one.
    private async Task<IEnumerable<(string Value, int Places)>> LimitsAsync(FormDefinition definition, FormField field, string culture, CancellationToken cancellationToken)
    {
        if (field.Options is not { } options || types.FindType(field.Type)?.Declaration.Features.HasFlag(FieldFeatures.Places) != true)
        {
            return [];
        }

        if (options.Source == OptionSource.Items)
        {
            return options.Items.Where(item => item.Places is not null).Select(item => (item.Value, item.Places!.Value));
        }

        var resolved = await cascading.GetAsync(definition, field, _ => [], culture, null, cancellationToken);
        return resolved.Options.Where(option => option.Places is not null).Select(option => (option.Value, option.Places!.Value));
    }

    // Only what the form's rules ask of the member: that one is signed in, and which of the groups they name are theirs.
    private static async Task<ChallengeMemberResponseModel?> MemberAsync(FormMembers members, FormDefinition definition)
    {
        var uses = FormMemberConditions.Of(definition);
        if (!uses.Any)
        {
            return null;
        }

        return new ChallengeMemberResponseModel { SignedIn = true, Groups = uses.Groups.Count == 0 ? [] : await members.GroupsAmongAsync(uses.Groups) };
    }
}

/// <summary>Asks for a challenge (ADR-0012); the whole body may be left out, and any other member is refused.</summary>
internal sealed record ChallengeRequestModel
{
    /// <summary>The earlier challenge the page holds, even an expired one, whose issue time a valid one carries over.</summary>
    public string? Previous { get; init; }

    /// <summary>The page the form is on, whose site says what the protection is for a form that inherits it.</summary>
    public Guid? ContentKey { get; init; }
}

/// <summary>What a page needs to show a form's captcha (ADR-0142), which holds no secret.</summary>
internal sealed record ChallengeCaptchaResponseModel
{
    /// <summary>The provider's alias.</summary>
    public required string Provider { get; init; }

    /// <summary>The provider's public key.</summary>
    public required string SiteKey { get; init; }

    /// <summary>The address of the provider's script, which a page loads only after consent.</summary>
    public required string ScriptAddress { get; init; }

    /// <summary>The action a score-based provider's token is asked for; left out for the others.</summary>
    public string? Action { get; init; }

    /// <summary>When the page may load the script: <c>explicit</c>, <c>cookiebot</c>, <c>cmp</c> or <c>implicit</c> (ADR-0157).</summary>
    public required string Consent { get; init; }

    /// <summary>In the <c>cookiebot</c> mode, the category whose consent loads the script; left out for the others.</summary>
    public string? Category { get; init; }
}

/// <summary>A challenge's puzzle (ADR-0141): the number to find makes the SHA-256 of <c>nonce:number</c> start with so many zero bits.</summary>
internal sealed record ChallengeProofResponseModel
{
    /// <summary>The puzzle's nonce.</summary>
    public required string Nonce { get; init; }

    /// <summary>How many zero bits the hash starts with.</summary>
    public required int Bits { get; init; }
}

/// <summary>A challenge and what cached markup cannot carry (ADR-0012).</summary>
internal sealed record ChallengeResponseModel
{
    /// <summary>The signed challenge, which the page's requests post back.</summary>
    public required string Challenge { get; init; }

    /// <summary>The fewest seconds a person takes from the challenge's first issue to a submission.</summary>
    public required int MinFillSeconds { get; init; }

    /// <summary>The places left, by the field's key and the option's value, of the form's own options that have places; left out when none has.</summary>
    public IReadOnlyDictionary<string, Dictionary<string, int>>? Places { get; init; }

    /// <summary>What a page needs to show the form's captcha; left out when the form has none or its provider has no keys.</summary>
    public ChallengeCaptchaResponseModel? Captcha { get; init; }

    /// <summary>The puzzle a page solves before it sends; left out when the form's level has none.</summary>
    public ChallengeProofResponseModel? ProofOfWork { get; init; }

    /// <summary>What the form's rules ask of a signed-in member; left out for a visitor, and when no rule asks.</summary>
    public ChallengeMemberResponseModel? Member { get; init; }
}

/// <summary>What the form's rules ask of a signed-in member (section 4.3.3).</summary>
internal sealed record ChallengeMemberResponseModel
{
    /// <summary>That a member is signed in.</summary>
    public required bool SignedIn { get; init; }

    /// <summary>The groups the form's rules name that the member is in, by their keys.</summary>
    public required IReadOnlyList<string> Groups { get; init; }
}
