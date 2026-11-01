using System.Net.Mime;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>
/// An entry's files (ADR-0006, ADR-0145): the only way to them, each as an attachment that no browser runs or guesses at, whatever
/// it holds. A file needs <c>viewEntries</c> on its form, and a sensitive field's also <c>viewSensitive</c>; the archive of a form's
/// files needs <c>export</c>, and leaves out a sensitive field's for a user without <c>viewSensitive</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/files")]
[ApiExplorerSettings(GroupName = "Entry file")]
internal sealed class EntryFileController(EntryFileDownloads downloads, IFormBuilderAccessService access) : FormBuilderManagementApiController
{
    private const string ArchiveName = "files.zip";

    /// <summary>Downloads an entry's file.</summary>
    /// <param name="key">The form.</param>
    /// <param name="fileKey">The file.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{fileKey:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, MediaTypeNames.Application.Octet)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Download(Guid key, Guid fileKey, CancellationToken cancellationToken = default)
    {
        var download = await downloads.OpenAsync(key, fileKey, await MaySeeSensitiveAsync(cancellationToken), RequestAccess.User.Key, cancellationToken);
        switch (download.Status)
        {
            case EntryFileStatus.Found:
                HoldToAttachment();
                return File(download.Content!, download.ContentType!, download.Name!);
            case EntryFileStatus.Quarantined:
                return FormBuilderProblems.FileQuarantined();
            case EntryFileStatus.NotScanned:
                return FormBuilderProblems.FileNotScanned();
            case EntryFileStatus.Sensitive:
                return FormBuilderProblems.Missing(FormBuilderPermissions.ViewSensitive);
            default:
                return FormBuilderProblems.FileNotFound();
        }
    }

    /// <summary>Downloads a form's files as a ZIP, each entry's in a folder of its own, streamed as it is written.</summary>
    /// <param name="key">The form.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("archive")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Export, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, MediaTypeNames.Application.Zip)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid key, CancellationToken cancellationToken = default)
    {
        var maySeeSensitive = await MaySeeSensitiveAsync(cancellationToken);
        var user = RequestAccess.User.Key;
        HoldToAttachment();
        Response.Headers.ContentDisposition = new ContentDisposition { FileName = ArchiveName, Inline = false }.ToString();
        return new StreamedArchive((body, written) => downloads.WriteArchiveAsync(key, body, maySeeSensitive, user, written));
    }

    private async Task<bool> MaySeeSensitiveAsync(CancellationToken cancellationToken)
    {
        var held = await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(RequestAccess.Form!), cancellationToken);
        return (held & FormBuilderPermissions.ViewSensitive) != FormBuilderPermissions.None;
    }

    // An archive written straight to the response, once the filters of the result have run, as nothing may touch the headers after its first byte.
    private sealed class StreamedArchive(Func<Stream, CancellationToken, Task> write) : ActionResult
    {
        public override Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.ContentType = MediaTypeNames.Application.Zip;
            return write(response.Body, context.HttpContext.RequestAborted);
        }
    }

    // Whatever the bytes hold, a browser saves them and neither guesses at them nor runs them. The management API's own filter keeps them from any cache.
    private void HoldToAttachment()
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
    }
}
