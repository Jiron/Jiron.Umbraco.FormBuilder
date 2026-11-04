using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// For the tests of what both runtimes do with a tag its place refuses (ADR-0065, ADR-0068): a form published before the
/// form check of 9.5b may hold one, and both runtimes write it as it stands (ADR-0177). These tests publish such forms, so
/// their site's form check leaves the merge tags out, the rest of it as every site has it.
/// </summary>
internal static class RefusedTags
{
    /// <summary>Publishes forms whose texts hold tags their places refuse.</summary>
    public static void PublishesRefusedTags(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Registered before the composer's TryAdd, or after it as the last registration: the one the site resolves.
        builder.Services.AddSingleton(provider => new FormCheck(
            provider.GetServices<IFormPublishCheck>(),
            provider.GetServices<IFormCheck>().Where(check => check is not MergeTagFormCheck),
            provider.GetRequiredService<FieldSettingLocks>(),
            provider.GetRequiredService<ActionSettingLocks>()));
    }
}
