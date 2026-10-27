using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Outbound;

/// <summary>The registrations of calls to other hosts (ADR-0076), which the core composer makes.</summary>
internal static class FormBuilderOutboundServices
{
    /// <summary>
    /// Registers the transport of every site, unless the Testing assembly registered its own, and the calls over it,
    /// which hold every address to the host configuration's hosts and schemes.
    /// </summary>
    public static IServiceCollection AddFormBuilderOutbound(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IFormBuilderHttpTransport>(_ => new FormBuilderHttpTransport());
        services.TryAddSingleton<OutboundHttp>();
        return services;
    }
}
