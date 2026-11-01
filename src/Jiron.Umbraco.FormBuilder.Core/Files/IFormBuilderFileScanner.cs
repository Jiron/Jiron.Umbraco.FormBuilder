using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// Scans an uploaded file for malware (ADR-0006, ADR-0144), registered through <c>builder.JironFormBuilder().FileScanners()</c>.
/// A site has none by default. With one, every upload waits for its answer: a background job asks each scanner once per file,
/// off the visitor's request, and a file a scanner quarantines cannot be taken by an entry or downloaded.
/// </summary>
public interface IFormBuilderFileScanner
{
    /// <summary>
    /// What this scanner makes of a file. Throwing or taking too long counts as
    /// <see cref="FormBuilderFileScanOutcome.Unavailable"/>, and the file is asked about again later.
    /// </summary>
    /// <param name="file">The file with its bytes.</param>
    /// <param name="cancellationToken">Cancelled when the scan takes too long or the site stops.</param>
    Task<FormBuilderFileScanOutcome> ScanAsync(FormBuilderFileToScan file, CancellationToken cancellationToken);
}

/// <summary>What a scanner makes of a file.</summary>
public enum FormBuilderFileScanOutcome
{
    /// <summary>Nothing found.</summary>
    Clean,

    /// <summary>Malware: nobody gets the file.</summary>
    Quarantine,

    /// <summary>The scanner could not say, so the file waits and is asked about again.</summary>
    Unavailable,
}

/// <summary>A file for a scanner.</summary>
/// <param name="Key">The file's key.</param>
/// <param name="Name">The name its visitor's device gave it, cleaned. It is untrusted, so log neither it nor anything made from it.</param>
/// <param name="ContentType">The content type of its kind, never the client's.</param>
/// <param name="SizeBytes">Its size.</param>
/// <param name="Sha256">The SHA-256 of its bytes, in lowercase hex.</param>
/// <param name="Content">Its bytes, read from the start; closed after the scan.</param>
public sealed record FormBuilderFileToScan(Guid Key, string Name, string ContentType, long SizeBytes, string Sha256, Stream Content);

/// <summary>The file scanners a site has, in the order they are registered (ADR-0144).</summary>
public sealed class FormBuilderFileScannerCollection : BuilderCollectionBase<IFormBuilderFileScanner>
{
    /// <summary>A collection of the scanners the builder creates.</summary>
    public FormBuilderFileScannerCollection(Func<IEnumerable<IFormBuilderFileScanner>> items)
        : base(items)
    {
    }
}

/// <summary>The ordered collection of file scanners (ADR-0144), reached through <c>builder.JironFormBuilder().FileScanners()</c>.</summary>
public sealed class FormBuilderFileScannerCollectionBuilder
    : OrderedCollectionBuilderBase<FormBuilderFileScannerCollectionBuilder, FormBuilderFileScannerCollection, IFormBuilderFileScanner>
{
    /// <inheritdoc />
    protected override FormBuilderFileScannerCollectionBuilder This => this;
}
