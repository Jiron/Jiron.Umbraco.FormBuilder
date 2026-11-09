using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>Gives each request the address a test names, as the connection would; none leaves the test server's own.</summary>
/// <param name="address">The address the next requests come from, or null.</param>
internal sealed class RemoteAddressFilter(Func<string?> address) : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, go) =>
        {
            if (address() is { } named)
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(named);
            }

            return go();
        });
        next(app);
    };
}
