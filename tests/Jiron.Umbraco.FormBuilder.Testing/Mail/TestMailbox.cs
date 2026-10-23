using System.Diagnostics;
using MimeKit;

namespace Jiron.Umbraco.FormBuilder.Testing.Mail;

/// <summary>One email from a host's pickup folder, decoded.</summary>
/// <param name="File">The <c>.eml</c> file it was read from.</param>
/// <param name="Subject">The subject.</param>
/// <param name="From">The sender addresses.</param>
/// <param name="To">The recipient addresses.</param>
/// <param name="Cc">The copy addresses.</param>
/// <param name="ReplyTo">The reply-to addresses.</param>
/// <param name="Date">The date header.</param>
/// <param name="Html">The HTML body, or null.</param>
/// <param name="Text">The plain text body, or null.</param>
/// <param name="Headers">Every header in order, with its decoded value.</param>
public sealed record TestMail(
    string File,
    string Subject,
    IReadOnlyList<string> From,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> ReplyTo,
    DateTimeOffset Date,
    string? Html,
    string? Text,
    IReadOnlyList<KeyValuePair<string, string>> Headers);

/// <summary>
/// Reads the <c>.eml</c> files that a host writes to its mail pickup folder (Umbraco's
/// <c>SpecifiedPickupDirectory</c>), decoded with MimeKit, which wrote them. A file that is still being written cannot
/// be opened yet, so reading retries it until a deadline (Tandem's lesson: reading too early fails). Waiting polls the
/// folder until a mail matches or the time is up; it never sleeps a fixed time to wait for state (ADR-0011).
/// </summary>
public sealed class TestMailbox(string folder)
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan LockedFileDeadline = TimeSpan.FromSeconds(5);

    /// <summary>The pickup folder.</summary>
    public string Folder { get; } = folder;

    /// <summary>Every mail in the folder, oldest first (by write time, then file name); none when the folder is missing.</summary>
    public async Task<IReadOnlyList<TestMail>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var files = new DirectoryInfo(Folder).GetFiles("*.eml")
            .OrderBy(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
        var mails = new List<TestMail>(files.Count);
        foreach (var file in files)
        {
            mails.Add(await ReadAsync(file.FullName, cancellationToken));
        }

        return mails;
    }

    /// <summary>Waits until a mail matches, and returns the first that does.</summary>
    /// <exception cref="TimeoutException">No mail matched in time; the message says how many mails there were.</exception>
    public async Task<TestMail> WaitForAsync(Func<TestMail, bool> match, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(match);
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var mails = await ReadAllAsync(cancellationToken);
            if (mails.FirstOrDefault(match) is { } found)
            {
                return found;
            }

            if (clock.Elapsed >= timeout)
            {
                throw new TimeoutException($"No mail in {Folder} matched within {timeout.TotalSeconds:0.#} s ({mails.Count} mails there).");
            }

            await Task.Delay(Poll, cancellationToken);
        }
    }

    /// <summary>Deletes every mail in the folder.</summary>
    public void Clear()
    {
        if (!Directory.Exists(Folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(Folder, "*.eml"))
        {
            File.Delete(file);
        }
    }

    /// <summary>Reads one file, retrying while it is still being written.</summary>
    internal static async Task<TestMail> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                var message = await MimeMessage.LoadAsync(stream, cancellationToken);
                return Decode(path, message);
            }
            catch (IOException) when (clock.Elapsed < LockedFileDeadline && File.Exists(path))
            {
                await Task.Delay(Poll, cancellationToken);
            }
        }
    }

    private static TestMail Decode(string path, MimeMessage message) => new(
        path,
        message.Subject ?? string.Empty,
        Addresses(message.From),
        Addresses(message.To),
        Addresses(message.Cc),
        Addresses(message.ReplyTo),
        message.Date,
        message.HtmlBody,
        message.TextBody,
        message.Headers.Select(header => new KeyValuePair<string, string>(header.Field, header.Value)).ToList());

    private static List<string> Addresses(InternetAddressList list) =>
        list.Mailboxes.Select(mailbox => mailbox.Address).ToList();
}
