using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.Themes;

/// <summary>The registrations of the themes (ADR-0105), which the core composer makes.</summary>
internal static class FormBuilderThemeServices
{
    /// <summary>
    /// Registers the collection of themes (the built-in ones, and whatever the site's composers change), the registry over
    /// it and its check at start-up, and whether the site has a form's theme. Safe to call more than once.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderThemes(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Asking for the collection builder registers the collection when the container is built, whichever composer
        // asked first.
        builder.JironFormBuilder().Themes();
        builder.Services.TryAddSingleton(provider => new FormBuilderThemes(provider.GetRequiredService<FormBuilderThemeCollection>(), provider.GetRequiredService<ILogger<FormBuilderThemes>>()));
        builder.Services.TryAddSingleton<FormThemes>();
        builder.Services.TryAddSingleton<LibraryThemes>();
        builder.Services.TryAddSingleton<ThemeLibrary>();
        builder.CacheRefreshers().Add<FormBuilderThemeCacheRefresher>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderStartupChecks>, ThemeStartupCheck>());
        return builder;
    }
}
