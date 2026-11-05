using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// The captcha providers a form's Spam protection group offers (ADR-0184): each one's name, label, whether it has its
/// keys, and the settings a form gives it, described as a field type's are. Never a key: those stay in the host's
/// configuration. Any user of the Forms section may ask, as the registry describes the site's code.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/captcha-provider")]
[ApiExplorerSettings(GroupName = "Form settings")]
internal sealed class CaptchaProviderController(FormBuilderCaptchaProviderCollection providers) : FormBuilderManagementApiController
{
    /// <summary>The providers a form can choose, one per name, in the order the site registers them.</summary>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(IReadOnlyList<CaptchaProviderResponseModel>), StatusCodes.Status200OK)]
    public IActionResult List() =>
        Ok(providers.Offered().Select(provider => new CaptchaProviderResponseModel
        {
            Name = provider.Name,
            Label = provider.Label,
            IsConfigured = provider.IsConfigured,
            Settings = FieldSettingResponseModel.Of(providers.SchemaOf(provider).Settings, []),
        }).ToList());
}

/// <summary>
/// What a form's Settings view needs of the site (ADR-0184): the site's defaults that "Site default" stands for, its time
/// zone, the themes and member groups a form may name, and the bounds the save checks hold settings to. It needs
/// <c>design</c> on the form, whose settings these are: the member groups' names are the Members section's to show, so
/// only who may set a form's access reads them here.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/setting-choices")]
[ApiExplorerSettings(GroupName = "Form settings")]
internal sealed class FormSettingsController(
    IFormBuilderSettingsService settings,
    FormBuilderThemes themes,
    IThemeRepository library,
    IMemberGroupService memberGroups) : FormBuilderManagementApiController
{
    // A page of the library's themes, read until the last.
    private const int ThemePage = 200;

    /// <summary>The site's defaults, time zone, themes, member groups and the settings' bounds, for a form.</summary>
    /// <param name="key">The form.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormSettingsChoicesResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Choices(Guid key, CancellationToken cancellationToken = default)
    {
        // The form's key is the permission's, which its filter read from the route before the action ran.
        _ = key;
        var general = (await settings.GetAsync<GeneralSettings>(cancellationToken: cancellationToken)).Value;
        var protection = (await settings.GetAsync<ProtectionSettings>(cancellationToken: cancellationToken)).Value;
        var stored = new List<StoredTheme>();
        for (var skip = 0; ; skip += ThemePage)
        {
            var page = await library.ListAsync(null, new PageRequest(skip, ThemePage), cancellationToken);
            stored.AddRange(page.Items);
            if (page.Items.Count < ThemePage || stored.Count >= page.Total)
            {
                break;
            }
        }

        return Ok(new FormSettingsChoicesResponseModel
        {
            TimeZone = general.TimeZone,
            Defaults = new FormSettingsDefaultsResponseModel
            {
                Protection = EnumNames<ProtectionLevel>.ByValue[protection.Level],
                CaptchaProvider = protection.CaptchaProvider,
                ContentRules = EnumNames<ContentRuleLevel>.ByValue[protection.ContentRules],
                RequiredMarker = EnumNames<RequiredMarker>.ByValue[general.RequiredMarker],
                Theme = general.Theme ?? BuiltInThemes.DefaultAlias,
                SubmitLabel = general.SubmitLabel,
                NextLabel = general.NextLabel,
                BackLabel = general.BackLabel,
            },
            Themes =
            [
                .. themes.All.Select(theme => new FormSettingsThemeResponseModel { Alias = theme.Name, Label = theme.Label }),
                .. stored.Select(theme => new FormSettingsThemeResponseModel { Alias = theme.Alias, Label = theme.Name }),
            ],
            MemberGroups =
            [
                .. (await memberGroups.GetAllAsync())
                    .Select(group => new FormSettingsMemberGroupResponseModel { Key = group.Key, Name = group.Name ?? string.Empty })
                    .OrderBy(group => group.Name, StringComparer.CurrentCultureIgnoreCase),
            ],
            Limits = new FormSettingsLimitsResponseModel
            {
                LineLength = DefinitionSaveChecks.LineLength,
                HtmlLength = DefinitionSaveChecks.HtmlLength,
                NameLength = DefinitionStructureChecks.MaxNameLength,
                VerificationDays = DefinitionSaveChecks.MaxVerificationDays,
                ReferenceDigits = DefinitionSaveChecks.MaxReferenceDigits,
            },
        });
    }
}

/// <summary>A captcha provider a form can choose (ADR-0184).</summary>
internal sealed record CaptchaProviderResponseModel
{
    /// <summary>The name a form's definition chooses it by, such as <c>hcaptcha</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The name the backoffice offers it by.</summary>
    public required string Label { get; init; }

    /// <summary>Whether the site has its keys; a provider without them is offered dimmed.</summary>
    public required bool IsConfigured { get; init; }

    /// <summary>The settings a form gives it, in their order, as a field type's are served; none for most.</summary>
    public required IReadOnlyList<FieldSettingResponseModel> Settings { get; init; }
}

/// <summary>What a form's Settings view needs of the site (ADR-0184).</summary>
internal sealed record FormSettingsChoicesResponseModel
{
    /// <summary>The site's time zone, an IANA name, in which the view shows when a form opens and closes.</summary>
    public required string TimeZone { get; init; }

    /// <summary>What a setting left to the site gets.</summary>
    public required FormSettingsDefaultsResponseModel Defaults { get; init; }

    /// <summary>The themes a form may choose: the site's code first, then its library's.</summary>
    public required IReadOnlyList<FormSettingsThemeResponseModel> Themes { get; init; }

    /// <summary>The member groups a form may admit, by name.</summary>
    public required IReadOnlyList<FormSettingsMemberGroupResponseModel> MemberGroups { get; init; }

    /// <summary>The bounds the save checks hold settings to.</summary>
    public required FormSettingsLimitsResponseModel Limits { get; init; }
}

/// <summary>The site's defaults that a form's "Site default" stands for, as the definition names each value.</summary>
internal sealed record FormSettingsDefaultsResponseModel
{
    /// <summary>The site's spam protection: <c>standard</c>, <c>proofOfWork</c> or <c>captcha</c>.</summary>
    public required string Protection { get; init; }

    /// <summary>For a site protected by a captcha, the provider's name.</summary>
    public string? CaptchaProvider { get; init; }

    /// <summary>How strict the site's content checks are: <c>off</c>, <c>normal</c> or <c>strict</c>.</summary>
    public required string ContentRules { get; init; }

    /// <summary>How the site marks fields: <c>required</c>, <c>optional</c> or <c>none</c>.</summary>
    public required string RequiredMarker { get; init; }

    /// <summary>The alias of the site's default theme.</summary>
    public required string Theme { get; init; }

    /// <summary>
    /// The site's label of the send button, per culture (<c>""</c> the default); none for the package's word in the
    /// page's language.
    /// </summary>
    public IReadOnlyDictionary<string, string>? SubmitLabel { get; init; }

    /// <summary>The site's label of the button to the next page, as <see cref="SubmitLabel"/>.</summary>
    public IReadOnlyDictionary<string, string>? NextLabel { get; init; }

    /// <summary>The site's label of the button to the page before, as <see cref="SubmitLabel"/>.</summary>
    public IReadOnlyDictionary<string, string>? BackLabel { get; init; }
}

/// <summary>A theme a form may choose.</summary>
internal sealed record FormSettingsThemeResponseModel
{
    /// <summary>The alias a form's definition names it by.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name.</summary>
    public required string Label { get; init; }
}

/// <summary>A member group a form may admit.</summary>
internal sealed record FormSettingsMemberGroupResponseModel
{
    /// <summary>The key a form's definition names it by.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }
}

/// <summary>The bounds the save checks hold a form's settings to.</summary>
internal sealed record FormSettingsLimitsResponseModel
{
    /// <summary>The most characters of a one-line text: a label, the entry title, a prefix, a CSS class.</summary>
    public required int LineLength { get; init; }

    /// <summary>The most characters of a message in rich text.</summary>
    public required int HtmlLength { get; init; }

    /// <summary>The most characters of a query or context value's name.</summary>
    public required int NameLength { get; init; }

    /// <summary>The most days a verification link stays valid; the least is 1.</summary>
    public required int VerificationDays { get; init; }

    /// <summary>The most digits an entry reference pads its number to; the least is 1.</summary>
    public required int ReferenceDigits { get; init; }
}
