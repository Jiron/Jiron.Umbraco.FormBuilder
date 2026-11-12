namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// The host configuration of uploaded files (section 4.8, <c>JironFormBuilder:Files</c>), part of
/// <see cref="Configuration.FormBuilderOptions"/> (ADR-0042): where the default store keeps the files (ADR-0006,
/// ADR-0041), the most bytes any file may have, and the extensions no field takes (ADR-0087).
/// </summary>
public sealed class FormBuilderFilesOptions
{
    /// <summary>The configuration key of these options.</summary>
    public const string SectionName = Configuration.FormBuilderOptions.SectionName + ":Files";

    /// <summary>The configuration key of <see cref="Root"/>.</summary>
    public const string RootKey = SectionName + ":" + nameof(Root);

    /// <summary>The folder of the default store unless the host sets another.</summary>
    public const string DefaultRoot = "~/umbraco/Data/JironFormBuilder/Uploads";

    /// <summary>The most bytes a file has unless the host sets another: 50 MiB.</summary>
    public const long DefaultMaxFileBytes = 52_428_800;

    /// <summary>The highest <see cref="MaxFileBytes"/> a host may set, the highest a file field's own limit takes.</summary>
    public const long HighestMaxFileBytes = 999_999_999_999_999;

    /// <summary>The most bytes of uploads may wait for an entry unless the host sets another: 10 GiB.</summary>
    public const long DefaultMaxWaitingBytes = 10_737_418_240;

    /// <summary>The most bytes one address may upload in the time an upload waits unless the host sets another: 1 GiB.</summary>
    public const long DefaultMaxWaitingBytesPerAddress = 1_073_741_824;

    /// <summary>The longest extension, as a file field's own extensions are.</summary>
    public const int MaxExtensionLength = 10;

    /// <summary>The extensions blocked unless the host lists its own: programs, scripts, and pages that run script (ADR-0143).</summary>
    public static IReadOnlyList<string> DefaultBlockedExtensions { get; } =
    [
        "exe", "dll", "js", "mjs", "bat", "cmd", "ps1", "sh", "msi", "msp", "scr", "pif", "cpl", "jar", "vbs", "wsf", "hta", "lnk", "reg",
        "html", "htm", "xhtml", "shtml", "svg", "php", "phtml", "asp", "aspx", "jsp", "cgi",
    ];

    /// <summary>
    /// The folder the default store keeps the files in: <c>~/</c> followed by a path relative to the site's content root, or an
    /// absolute path, for example a folder that the servers of a load-balanced site share (the default lies on each
    /// server's own disk, and a load-balanced site that keeps it is told so at start-up). It must not lie inside the web
    /// root or contain it, as written or with its links followed: a site configured so does not start. Name the folder by
    /// its own path, because the check cannot see through an administrative share, a drive letter made with <c>subst</c>, a
    /// short 8.3 name or a link inside the web root that points to the folder.
    /// </summary>
    public string Root { get; set; } = DefaultRoot;

    /// <summary>
    /// The most bytes any uploaded file may have, 1 to 999,999,999,999,999: 50 MiB unless the site sets another. A file
    /// field's own limit is held to it, and publishing refuses a field whose limit is higher. The upload endpoint sets
    /// each request's limit from its field's, a little above the file's for the parts around it; a server or proxy in
    /// front of the site with a lower limit of its own (IIS's maxAllowedContentLength, for example) refuses larger
    /// files first.
    /// </summary>
    public long MaxFileBytes { get; set; } = DefaultMaxFileBytes;

    /// <summary>
    /// The most bytes of uploads may wait for an entry together, 0 to 999,999,999,999,999: 10 GiB unless the site sets
    /// another, and 0 leaves the cap out. Past it, an upload is refused before a byte of it is stored, until entries take
    /// what waits or the cleanup removes it a day later (ADR-0148). Set it to what the disk can spare: the count is read every
    /// few seconds, so it can pass the cap by what other servers take in that time.
    /// </summary>
    public long MaxWaitingBytes { get; set; } = DefaultMaxWaitingBytes;

    /// <summary>
    /// The most bytes one address may upload in the day an upload waits for an entry, 0 to 999,999,999,999,999: 1 GiB unless the site
    /// sets another, and 0 leaves it out, so that a few addresses cannot use up <see cref="MaxWaitingBytes"/> for everybody (ADR-0148).
    /// An IPv6 address counts as its /64 network, and the /48 around it holds four shares together. Counted on each server, in
    /// whole mebibytes. Raise it for a form that takes large files from visitors behind one address.
    /// </summary>
    public long MaxWaitingBytesPerAddress { get; set; } = DefaultMaxWaitingBytesPerAddress;

    /// <summary>
    /// The extensions no field takes, in lowercase letters and digits without the point, each once, such as dwg. A
    /// site's own list replaces the defaults (exe, dll, js, mjs, bat, cmd, ps1, sh, msi, msp, scr, pif, cpl, jar, vbs, wsf, hta, lnk, reg,
    /// html, htm, xhtml, shtml, svg, php, phtml, asp, aspx, jsp and cgi), which apply while it lists none. Publishing refuses a field that lists one, and a field's groups of file types leave them out.
    /// Programs and scripts are refused by their first bytes, whatever their name.
    /// </summary>
    public IList<string> BlockedExtensions { get; } = [];
}
