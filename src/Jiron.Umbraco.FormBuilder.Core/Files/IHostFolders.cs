namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// The site's folders that the file store's root is resolved and checked against (ADR-0041). Web supplies them from
/// ASP.NET Core's host environment, which Core does not reference (ADR-0001).
/// </summary>
internal interface IHostFolders
{
    /// <summary>The full path of the content root, which <c>~/</c> names.</summary>
    string ContentRoot { get; }

    /// <summary>The full path of the web root, whose files anyone can download.</summary>
    string WebRoot { get; }
}
