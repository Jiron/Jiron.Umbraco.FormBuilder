using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Settings;

/// <summary>
/// Settings, General's facts (ADR-0218): the address links start with when the scope sets none, the themes a site may
/// name, and whether pages can show forms yet, with whether the user may create the form block type (IA 8, section 10).
/// </summary>
/// <param name="baseUrls">The sites' base URLs.</param>
/// <param name="themes">The code's themes.</param>
/// <param name="library">The library's themes.</param>
/// <param name="contentTypes">Umbraco's content types, the form block's among them.</param>
/// <param name="security">The signed-in user, whose sections say whether they may create the block type.</param>
internal sealed class GeneralFacts(
    SiteBaseUrl baseUrls,
    FormBuilderThemes themes,
    IThemeRepository library,
    IContentTypeService contentTypes,
    IBackOfficeSecurityAccessor security) : ISettingsFacts<GeneralSettings>
{
    /// <inheritdoc />
    public async Task<SettingsFactsResponseModel> ReadAsync(Guid? site, CancellationToken cancellationToken)
    {
        var stored = await LibraryPages.AllAsync((page, token) => library.ListAsync(null, page, token), cancellationToken);
        var block = contentTypes.Get(FormBlockTypes.ElementTypeAlias);
        var sections = security.BackOfficeSecurity?.CurrentUser?.AllowedSections ?? [];
        return new()
        {
            General = new GeneralFactsResponseModel(
                await baseUrls.GetDefaultAsync(site),
                BuiltInThemes.DefaultAlias,
                [
                    .. themes.All.Select(theme => new FieldSettingChoiceResponseModel(theme.Name, theme.Label)),
                    .. stored.Select(theme => new FieldSettingChoiceResponseModel(theme.Alias, theme.Name)),
                ],
                new FormBlockFactsResponseModel(
                    block is not null,
                    block is null ? null : new DateTimeOffset(DateTime.SpecifyKind(block.CreateDate, DateTimeKind.Utc)),
                    sections.Contains(Constants.Applications.Settings, StringComparer.Ordinal))),
        };
    }
}

/// <summary>Settings, Email's facts: whether Umbraco can send mail, the key that sets it up (IA 8), and the layouts.</summary>
/// <param name="sender">Umbraco's mail sender.</param>
/// <param name="layouts">The library's email layouts.</param>
internal sealed class EmailFacts(IEmailSender sender, IEmailLayoutRepository layouts) : ISettingsFacts<EmailSettings>
{
    /// <summary>The configuration key that sets up sending.</summary>
    public const string SendingKey = "Umbraco:CMS:Global:Smtp";

    /// <inheritdoc />
    public async Task<SettingsFactsResponseModel> ReadAsync(Guid? site, CancellationToken cancellationToken)
    {
        var stored = await LibraryPages.AllAsync((page, token) => layouts.ListAsync(null, page, token), cancellationToken);
        return new SettingsFactsResponseModel
        {
            Email = new EmailFactsResponseModel(
                sender.CanSendRequiredEmail(),
                SendingKey,
                EmailLayouts.CardKey,
                [
                    new FieldSettingChoiceResponseModel(EmailLayouts.CardKey.ToString(), "#jfbGlobalSettings_email_defaultLayout_card"),
                    new FieldSettingChoiceResponseModel(EmailLayouts.LetterKey.ToString(), "#jfbGlobalSettings_email_defaultLayout_letter"),
                    .. stored.Select(layout => new FieldSettingChoiceResponseModel(layout.Key.ToString(), layout.Name)),
                ]),
        };
    }
}

/// <summary>
/// Settings, Spam protection's facts: the captchas with their keys, and whether the host allows the implicit consent mode,
/// whose choice the screen greys out otherwise, naming the key that allows it (ADR-0157, ADR-0218).
/// </summary>
/// <param name="captchas">The captcha providers.</param>
/// <param name="options">The host configuration.</param>
internal sealed class ProtectionFacts(FormBuilderCaptchaProviderCollection captchas, IOptions<FormBuilderOptions> options) : ISettingsFacts<ProtectionSettings>
{
    /// <inheritdoc />
    public Task<SettingsFactsResponseModel> ReadAsync(Guid? site, CancellationToken cancellationToken) =>
        Task.FromResult(new SettingsFactsResponseModel
        {
            Protection = new ProtectionFactsResponseModel(
                [.. captchas.Offered().Select(provider => new CaptchaKeysResponseModel(
                    provider.Name,
                    provider.Label,
                    provider.IsConfigured,
                    (provider as SiteVerifyCaptchaProvider)?.KeysAt ?? string.Empty))],
                options.Value.Privacy.AllowImplicitCaptchaConsent),
        });
}

/// <summary>
/// Settings, Files' facts (ADR-0218): the host's cap and blocked extensions with their keys, each group's extensions, the
/// malware scanners, and whether files are kept in the configured folder or in a store the site registers.
/// </summary>
/// <param name="options">The host configuration.</param>
/// <param name="host">The host's limits, its blocked extensions as uploads apply them.</param>
/// <param name="scanners">The registered malware scanners.</param>
/// <param name="store">The file store.</param>
internal sealed class FilesFacts(IOptions<FormBuilderOptions> options, FieldHostLimits host, FormBuilderFileScannerCollection scanners, IFormBuilderFileStore store) : ISettingsFacts<FilesSettings>
{
    /// <summary>The key of the extensions no field takes.</summary>
    public const string BlockedExtensionsKey = FormBuilderFilesOptions.SectionName + ":BlockedExtensions";

    /// <inheritdoc />
    public Task<SettingsFactsResponseModel> ReadAsync(Guid? site, CancellationToken cancellationToken) =>
        Task.FromResult(new SettingsFactsResponseModel
        {
            Files = new FilesFactsResponseModel(
                options.Value.Files.MaxFileBytes,
                FilesHostLimitRule.ConfigurationKey,
                [.. host.BlockedExtensions.Order(StringComparer.Ordinal)],
                BlockedExtensionsKey,
                [.. Enum.GetValues<FileTypeGroup>().Select(group => new FileTypeGroupResponseModel(EnumNames<FileTypeGroup>.ByValue[group], FileFieldType.ExtensionsOf(group)))],
                scanners.Count,
                store.GetType().Assembly == typeof(FilesSettings).Assembly,
                FormBuilderFilesOptions.RootKey),
        });
}

/// <summary>
/// Settings, Integrations' facts: the hosts webhooks may call, from the host configuration (IA 8), and the fields whose
/// saved reference names a secret the host configuration holds, so the screen can say which are found.
/// </summary>
/// <param name="options">The host configuration.</param>
/// <param name="secrets">The secrets references name.</param>
/// <param name="settings">The settings, whose saved references are looked up.</param>
internal sealed class IntegrationFacts(IOptions<FormBuilderOptions> options, FormBuilderSecrets secrets, IFormBuilderSettingsService settings) : ISettingsFacts<IntegrationSettings>
{
    /// <inheritdoc />
    public async Task<SettingsFactsResponseModel> ReadAsync(Guid? site, CancellationToken cancellationToken)
    {
        var saved = (await settings.GetAsync<IntegrationSettings>(cancellationToken: cancellationToken)).Value;
        (string Name, string? Reference)[] references = [("signingSecret", saved.SigningSecret), ("slackWebhook", saved.SlackWebhook), ("teamsWebhook", saved.TeamsWebhook)];
        return new SettingsFactsResponseModel
        {
            Integrations = new IntegrationsFactsResponseModel(
                [.. options.Value.Webhooks.AllowedHosts],
                options.Value.Webhooks.AllowHttp,
                [.. references.Where(each => FormBuilderSecrets.IsReference(each.Reference) && secrets.Resolve(each.Reference) is not null).Select(each => each.Name)]),
        };
    }
}

/// <summary>Reads every page of a library list, for the choices a section offers.</summary>
internal static class LibraryPages
{
    // A page of the library's themes or layouts, read until the last.
    private const int Size = 200;

    /// <summary>Every item of the list, page by page.</summary>
    /// <param name="list">Reads one page.</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    public static async Task<List<T>> AllAsync<T>(Func<PageRequest, CancellationToken, Task<Page<T>>> list, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        var all = new List<T>();
        for (var skip = 0; ; skip += Size)
        {
            var page = await list(new PageRequest(skip, Size), cancellationToken);
            all.AddRange(page.Items);
            if (page.Items.Count < Size || all.Count >= page.Total)
            {
                return all;
            }
        }
    }
}

/// <summary>
/// Privacy, Data collection's facts (ADR-0224): whether the host allows full IP addresses, whose choice the screen greys out
/// otherwise, naming the key that allows it.
/// </summary>
/// <param name="options">The host configuration.</param>
internal sealed class PrivacyFacts(IOptions<FormBuilderOptions> options) : ISettingsFacts<PrivacySettings>
{
    /// <inheritdoc />
    public Task<SettingsFactsResponseModel> ReadAsync(Guid? site, CancellationToken cancellationToken) =>
        Task.FromResult(new SettingsFactsResponseModel { Privacy = new PrivacyFactsResponseModel(options.Value.Privacy.AllowFullIp) });
}
