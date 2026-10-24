using Jiron.Umbraco.FormBuilder.Core.Files;
using Microsoft.AspNetCore.Hosting;

namespace Jiron.Umbraco.FormBuilder.Web.Files;

/// <summary>
/// The site's content root and web root from ASP.NET Core's host environment (ADR-0041). Without a configured web root
/// ASP.NET Core uses <c>wwwroot</c> under the content root, so that is the web root then, as for Umbraco.
/// </summary>
internal sealed class WebHostFolders(IWebHostEnvironment environment) : IHostFolders
{
    /// <inheritdoc />
    public string ContentRoot { get; } = Path.GetFullPath(environment.ContentRootPath);

    /// <inheritdoc />
    public string WebRoot { get; } = Path.GetFullPath(
        string.IsNullOrWhiteSpace(environment.WebRootPath) ? Path.Combine(environment.ContentRootPath, "wwwroot") : environment.WebRootPath);
}
