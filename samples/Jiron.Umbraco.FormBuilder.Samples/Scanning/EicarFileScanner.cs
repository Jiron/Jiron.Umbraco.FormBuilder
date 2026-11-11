using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Samples.Scanning;

/// <summary>
/// The file scanner sample (docs/extending.md): a stub in the place of a real scanner, such as a ClamAV daemon a site
/// asks over its socket, which quarantines every file whose name holds "eicar", the name of the industry's harmless test
/// file, so a site sees quarantine work without malware. It reads none of the file's bytes, and logs nothing of its name,
/// which the visitor's device gave and is untrusted.
/// </summary>
public sealed class EicarFileScanner : IFormBuilderFileScanner
{
    /// <inheritdoc />
    public Task<FormBuilderFileScanOutcome> ScanAsync(FormBuilderFileToScan file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(file.Name.Contains("eicar", StringComparison.OrdinalIgnoreCase)
            ? FormBuilderFileScanOutcome.Quarantine
            : FormBuilderFileScanOutcome.Clean);
    }
}
