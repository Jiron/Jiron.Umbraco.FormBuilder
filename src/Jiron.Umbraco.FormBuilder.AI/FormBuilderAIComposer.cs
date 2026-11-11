using Jiron.Umbraco.FormBuilder.AI.Chat;
using Jiron.Umbraco.FormBuilder.AI.Drafting;
using Jiron.Umbraco.FormBuilder.AI.Health;
using Jiron.Umbraco.FormBuilder.AI.ManagementApi;
using Jiron.Umbraco.FormBuilder.AI.Marks;
using Jiron.Umbraco.FormBuilder.AI.Profiles;
using Jiron.Umbraco.FormBuilder.AI.Suggesting;
using Jiron.Umbraco.FormBuilder.AI.Translating;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.Composing;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.AI;

/// <summary>Registers the AI add-on (ADR-0018) after Jiron Form Builder's own registrations.</summary>
[ComposeAfter(typeof(FormBuilderWebComposer))]
public sealed class FormBuilderAIComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder) => builder.AddFormBuilderAI();
}

/// <summary>The add-on's registrations.</summary>
internal static class FormBuilderAIServices
{
    /// <summary>
    /// Adds the add-on's services, its endpoints in the document <c>jfb-ai</c>, its health probe for the dashboard and
    /// the overview, and the cleanup of a deleted form's mark. Every service is a singleton: Umbraco.AI's chat service
    /// is one, and nothing here holds a request's state. Safe to call more than once.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderAI(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddFormBuilderControllers(typeof(FormBuilderAIController).Assembly);
        builder.Services.TryAddSingleton<AIProfileChoice>();
        builder.Services.TryAddSingleton<AIChat>();
        builder.Services.TryAddSingleton<DraftDefinitions>();
        builder.Services.TryAddSingleton<FormDrafter>();
        builder.Services.TryAddSingleton<FormTranslator>();
        builder.Services.TryAddSingleton<FieldSuggester>();
        builder.Services.TryAddSingleton<DraftMarks>();
        builder.Services.TryAddSingleton<AIExceptionFilter>();
        builder.Services.TryAddSingleton<AIHealth>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<HealthProbe, AIHealth>(provider => provider.GetRequiredService<AIHealth>()));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<SwaggerGenOptions>, ConfigureFormBuilderAISwagger>());
        builder.AddNotificationAsyncHandler<FormDeletedNotification, DraftMarkCleanup>();
        builder.AddNotificationAsyncHandler<FolderDeletedNotification, DraftMarkCleanup>();
        builder.AddNotificationAsyncHandler<TrashEmptiedNotification, DraftMarkCleanup>();
        return builder;
    }
}
