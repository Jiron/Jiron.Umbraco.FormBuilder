using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Privacy;
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
/// What a form's Settings and Actions views need of the site (ADR-0184, ADR-0189): the site's defaults that "Site default"
/// stands for, its time zone, the themes, member groups and email layouts a form may name, and the bounds the save checks
/// hold settings to. It needs
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
    IEmailLayoutRepository layouts,
    IMemberGroupService memberGroups,
    DefinitionLimits limits) : FormBuilderManagementApiController
{
    // A page of the library's themes or layouts, read until the last.
    private const int LibraryPage = 200;

    /// <summary>The site's defaults, time zone, themes, member groups, email layouts and the settings' bounds, for a form.</summary>
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
        var email = (await settings.GetAsync<EmailSettings>(cancellationToken: cancellationToken)).Value;
        var privacy = (await settings.GetAsync<PrivacySettings>(cancellationToken: cancellationToken)).Value;
        var stored = await AllAsync((page, token) => library.ListAsync(null, page, token), cancellationToken);
        var storedLayouts = await AllAsync((page, token) => layouts.ListAsync(null, page, token), cancellationToken);

        // A default that is gone gives way to the card, as a mail's does (ADR-0159).
        var defaultLayout = EmailLayouts.KeyOf(email.DefaultLayout, storedLayouts.Select(layout => layout.Key));

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
                EmailLayout = defaultLayout,
                SubmitLabel = general.SubmitLabel,
                NextLabel = general.NextLabel,
                BackLabel = general.BackLabel,
                Insights = privacy.Insights,
                Retention = RetentionDaysResponseModel.Of(privacy.Retention ?? new SiteRetention()),
            },
            Themes =
            [
                .. themes.All.Select(theme => new FormSettingsThemeResponseModel { Alias = theme.Name, Label = theme.Label }),
                .. stored.Select(theme => new FormSettingsThemeResponseModel { Alias = theme.Alias, Label = theme.Name }),
            ],
            EmailLayouts =
            [
                new FormSettingsEmailLayoutResponseModel { Key = EmailLayouts.CardKey, Name = "#jfbEmailLayouts_card" },
                new FormSettingsEmailLayoutResponseModel { Key = EmailLayouts.LetterKey, Name = "#jfbEmailLayouts_letter" },
                .. storedLayouts.Select(layout => new FormSettingsEmailLayoutResponseModel { Key = layout.Key, Name = layout.Name }),
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
                RetentionDays = DefinitionSaveChecks.MaxDays,
                ReferenceDigits = DefinitionSaveChecks.MaxReferenceDigits,
                Outcomes = limits.MaxOutcomes,
                Actions = limits.MaxActions,
            },
        });
    }

    private static async Task<List<T>> AllAsync<T>(Func<PageRequest, CancellationToken, Task<Page<T>>> list, CancellationToken cancellationToken)
    {
        var all = new List<T>();
        for (var skip = 0; ; skip += LibraryPage)
        {
            var page = await list(new PageRequest(skip, LibraryPage), cancellationToken);
            all.AddRange(page.Items);
            if (page.Items.Count < LibraryPage || all.Count >= page.Total)
            {
                return all;
            }
        }
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

    /// <summary>The layouts an email action may choose: the built-in card and letter, then the library's in name order.</summary>
    public required IReadOnlyList<FormSettingsEmailLayoutResponseModel> EmailLayouts { get; init; }

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

    /// <summary>The key of the layout a mail has without one of its own, one of <see cref="FormSettingsChoicesResponseModel.EmailLayouts"/>.</summary>
    public required Guid EmailLayout { get; init; }

    /// <summary>
    /// The site's label of the send button, per culture (<c>""</c> the default); none for the package's word in the
    /// page's language.
    /// </summary>
    public IReadOnlyDictionary<string, string>? SubmitLabel { get; init; }

    /// <summary>The site's label of the button to the next page, as <see cref="SubmitLabel"/>.</summary>
    public IReadOnlyDictionary<string, string>? NextLabel { get; init; }

    /// <summary>The site's label of the button to the page before, as <see cref="SubmitLabel"/>.</summary>
    public IReadOnlyDictionary<string, string>? BackLabel { get; init; }

    /// <summary>Whether the site's insights are on (Privacy, Data collection, ADR-0211): a form counts only while they and its own are.</summary>
    public required bool Insights { get; init; }

    /// <summary>The site's days per state (Privacy, Retention, ADR-0222), which a form's empty day stands for; none for kept for good.</summary>
    public required RetentionDaysResponseModel Retention { get; init; }
}

/// <summary>A theme a form may choose.</summary>
internal sealed record FormSettingsThemeResponseModel
{
    /// <summary>The alias a form's definition names it by.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name.</summary>
    public required string Label { get; init; }
}

/// <summary>An email layout an action may choose (ADR-0159).</summary>
internal sealed record FormSettingsEmailLayoutResponseModel
{
    /// <summary>The key an action's settings name it by.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name; for a built-in one, a key of the backoffice's localisation that starts with <c>#</c>.</summary>
    public required string Name { get; init; }
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

    /// <summary>The most days a form keeps entries, in a state or for a number of days (ADR-0222); the least is 1.</summary>
    public required int RetentionDays { get; init; }

    /// <summary>The most digits an entry reference pads its number to; the least is 1.</summary>
    public required int ReferenceDigits { get; init; }

    /// <summary>The most outcomes a form has, as the host sets it (ADR-0185).</summary>
    public required int Outcomes { get; init; }

    /// <summary>The most actions a form has, as the host sets it (ADR-0188).</summary>
    public required int Actions { get; init; }
}
