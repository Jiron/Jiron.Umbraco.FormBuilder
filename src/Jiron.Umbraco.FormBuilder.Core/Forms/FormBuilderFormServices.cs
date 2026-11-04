using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>The registrations of the form services (ADR-0044, ADR-0053), which the core composer makes.</summary>
internal static class FormBuilderFormServices
{
    /// <summary>
    /// Registers the publish checks in the order they run, the form service with its versions and its exchange and what
    /// they share, the folder service, the permanent delete the form and folder services share (ADR-0047), and the
    /// published forms the website serves with their cache refresher (ADR-0084), with the culture and the page of a
    /// visitor's request (ADR-0088).
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderForms(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormPublishCheck, StructuralPublishCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormPublishCheck, CaptchaPublishCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, FieldsMissingFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, LabelFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, MergeTagFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, CaptchaKeysFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, EmailReplyFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, RequiredHiddenFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, UnreachablePageFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, ConsentFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, CardAndPasswordFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, FileLimitFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, FullOptionsFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, KeptLimitsFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, LastOutcomeFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, ThemeFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, SiteReferencesFormCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, DictionaryFormCheck>());
        builder.Services.TryAddSingleton<RuleSearchScopes>();
        builder.Services.TryAddSingleton<FormCheck>();
        builder.Services.TryAddSingleton<FormRemoval>();
        builder.Services.TryAddSingleton<LibraryPlaces>();
        builder.Services.TryAddSingleton<FormDocuments>();
        builder.Services.TryAddSingleton<FormInfos>();
        builder.Services.TryAddSingleton<FormCreation>();
        builder.Services.TryAddSingleton<FormImportLists>();
        builder.Services.TryAddSingleton<IFormBuilderFormService, FormBuilderFormService>();
        builder.Services.TryAddSingleton<IFormBuilderFormVersionService, FormBuilderFormVersionService>();
        builder.Services.TryAddSingleton<IFormBuilderFormExchangeService, FormBuilderFormExchangeService>();
        builder.Services.TryAddSingleton<IFormBuilderFolderService, FormBuilderFolderService>();
        builder.Services.TryAddSingleton<IFormBuilderLibraryService, FormBuilderLibraryService>();
        builder.Services.TryAddSingleton<LibraryLists>();
        builder.Services.TryAddSingleton<FormTemplateGallery>();
        builder.Services.TryAddSingleton<IFormBuilderPickerService, FormBuilderPickerService>();
        builder.Services.TryAddSingleton<FormCacheInvalidator>();
        builder.Services.TryAddSingleton<PublishedFormCache>();
        builder.Services.TryAddSingleton<EditorHtml>();
        builder.Services.TryAddSingleton<VisitorForms>();
        builder.Services.TryAddSingleton<FormPages>();
        builder.CacheRefreshers().Add<FormBuilderFormCacheRefresher>();
        return builder;
    }
}
