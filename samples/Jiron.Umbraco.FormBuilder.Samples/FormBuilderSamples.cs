using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Samples.Blob;
using Jiron.Umbraco.FormBuilder.Samples.Calendar;
using Jiron.Umbraco.FormBuilder.Samples.Captcha;
using Jiron.Umbraco.FormBuilder.Samples.Colour;
using Jiron.Umbraco.FormBuilder.Samples.Crm;
using Jiron.Umbraco.FormBuilder.Samples.Notifications;
using Jiron.Umbraco.FormBuilder.Samples.Products;
using Jiron.Umbraco.FormBuilder.Samples.Scanning;
using Jiron.Umbraco.FormBuilder.Samples.SiteSettings;
using Jiron.Umbraco.FormBuilder.Samples.Spam;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Samples;

/// <summary>
/// The samples by name (docs/extending.md, ADR-0234). Each is what a site writes in its own composer; none registers
/// itself, so a site, the demo or a test turns on only the ones it names, and a sample turned on twice is on once.
/// </summary>
public static class FormBuilderSamples
{
    /// <summary>
    /// A colour choice: a field type whose colours setting has the sample's own editor, shown in the designer by the
    /// sample's own preview, on the website by its view and on a headless site by its control.
    /// </summary>
    public const string Colour = "colour";

    /// <summary>A view on the form workspace that shows a developer how to place the open form on a page.</summary>
    public const string EmbedView = "embedView";

    /// <summary>An action that adds the visitor as a contact to a list of the site's CRM, here a stub.</summary>
    public const string Crm = "crm";

    /// <summary>An option source of the products a fake shop's API has in stock, under their categories.</summary>
    public const string Products = "products";

    /// <summary>An export format that writes a form of bookings as an iCalendar file.</summary>
    public const string ICalendar = "iCalendar";

    /// <summary>A captcha of the site's own: a small sum the visitor works out.</summary>
    public const string ArithmeticCaptcha = "arithmeticCaptcha";

    /// <summary>A spam check that counts the links in the answers against their words.</summary>
    public const string LinkDensity = "linkDensity";

    /// <summary>A merge-tag source of the site's settings node: <c>{{ siteSettings.phone }}</c>.</summary>
    public const string SiteSettings = "siteSettings";

    /// <summary>A file scanner stub that quarantines every file whose name holds "eicar".</summary>
    public const string EicarScanner = "eicarScanner";

    /// <summary>The uploaded files in a private container of Azure Blob Storage, through Umbraco's official provider.</summary>
    public const string BlobStore = "blobStore";

    /// <summary>A notification handler that refuses email addresses at the domains the site blocks, at their field.</summary>
    public const string BlockedDomains = "blockedDomains";

    /// <summary>Every sample's name.</summary>
    public static IReadOnlyList<string> Names { get; } =
        [Colour, EmbedView, Crm, Products, ICalendar, ArithmeticCaptcha, LinkDensity, SiteSettings, EicarScanner, BlobStore, BlockedDomains];

    /// <summary>Turns a sample on by its name.</summary>
    /// <param name="builder">The site's Umbraco builder, in a composer.</param>
    /// <param name="name">The sample's name, one of <see cref="Names"/>.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">No sample has the name.</exception>
    public static IUmbracoBuilder AddFormBuilderSample(this IUmbracoBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!Names.Contains(name, StringComparer.Ordinal))
        {
            throw new ArgumentException($"There is no sample '{name}'; the samples are {string.Join(", ", Names)}.", nameof(name));
        }

        if (!SampleManifests.TurnOn(builder.Services, name, ManifestsOf(name)))
        {
            return builder;
        }

        var forms = builder.JironFormBuilder();
        switch (name)
        {
            case Colour:
                forms.FieldTypes().Append<ColourFieldType>();

                // Its view is compiled into this library, which the site finds only as an application part: a library whose
                // only MVC code is its views is not one of the parts the site lists for itself.
                builder.Services.AddMvcCore().AddApplicationPart(typeof(ColourFieldType).Assembly);
                break;
            case Crm:
                forms.ActionTypes().Append<CrmActionType>();
                builder.Services.AddSingleton<StubCrmClient>();
                builder.Services.AddSingleton<ICrmClient>(services => services.GetRequiredService<StubCrmClient>());
                break;
            case Products:
                forms.OptionSources().Append<ProductOptionSource>();
                builder.Services.AddSingleton<IProductCatalogue, FakeProductCatalogue>();
                break;
            case ICalendar:
                forms.ExportFormats().Append<ICalendarExportFormat>();
                break;
            case ArithmeticCaptcha:
                forms.CaptchaProviders().Append<ArithmeticCaptchaProvider>();
                builder.Services.AddHttpContextAccessor();
                break;
            case LinkDensity:
                forms.SpamChecks().Append<LinkDensitySpamCheck>();
                break;
            case SiteSettings:
                forms.MergeTagSources().Append<SiteSettingsMergeTagSource>();
                break;
            case EicarScanner:
                forms.FileScanners().Append<EicarFileScanner>();
                break;
            case BlobStore:
                // The site's container: its connection, name and root path from Umbraco:Storage:AzureBlob:JironFormBuilder.
                // Without a root path the provider would put its virtual path in every blob's name.
                builder.AddAzureBlobFileSystem(BlobFileStore.FileSystemName, options =>
                {
                    options.VirtualPath = string.IsNullOrEmpty(options.VirtualPath) ? "~/jfb-blob-files" : options.VirtualPath;
                    options.ContainerRootPath = string.IsNullOrEmpty(options.ContainerRootPath) ? "uploads" : options.ContainerRootPath;
                });
                builder.Services.AddUnique<IFormBuilderFileStore, BlobFileStore>();
                break;
            case BlockedDomains:
                builder.Services.Configure<BlockedDomainsOptions>(builder.Config.GetSection(BlockedDomainsOptions.Section));
                builder.AddNotificationAsyncHandler<EntrySubmittingNotification, BlockedDomainsHandler>();
                break;
        }

        return builder;
    }

    // A sample's extensions of the backoffice, as an umbraco-package.json lists them; none for most.
    private static object[] ManifestsOf(string name) => name switch
    {
        Colour =>
        [
            new
            {
                type = "jfbSettingEditor",
                alias = "Jiron.FormBuilder.Samples.SettingEditor.Swatches",
                name = "Swatches",
                element = SampleManifests.Folder + "colour-swatches.js",
                meta = new { editor = ColourSettings.SwatchesEditor },
            },
            new
            {
                type = "jfbFieldPreview",
                alias = "Jiron.FormBuilder.Samples.FieldPreview.Colour",
                name = "Colour preview",
                element = SampleManifests.Folder + "colour-preview.js",
                meta = new { fieldType = ColourFieldType.TypeAlias },
            },
        ],
        EmbedView =>
        [
            new
            {
                type = "workspaceView",
                alias = "Jiron.FormBuilder.Samples.WorkspaceView.Embed",
                name = "Embed",
                element = SampleManifests.Folder + "embed-view.js",
                // Umbraco puts the higher weight first: after the form's own views.
                weight = 10,
                meta = new { label = "#jfbSamples_embed", pathname = "embed", icon = "icon-code" },
                conditions = new[] { new { alias = "Umb.Condition.WorkspaceAlias", match = "Jiron.FormBuilder.Workspace.Form" } },
            },
        ],
        _ => [],
    };
}
