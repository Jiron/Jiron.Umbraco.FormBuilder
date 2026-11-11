namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// What a signed challenge holds once its signature is checked (ADR-0012, ADR-0138). It is bound to the form and the time,
/// never to an address, because phones change networks while a form is open.
/// </summary>
/// <param name="Id">Its id, 128 random bits, which an entry stores so that a challenge makes one entry.</param>
/// <param name="IssuedUtc">When the visitor first asked, which a renewal carries over.</param>
/// <param name="Member">On a form for members, the member it was issued to; null otherwise.</param>
/// <param name="ProofBits">How many zero bits its puzzle asks for (ADR-0141); 0 for a challenge without a puzzle.</param>
internal sealed record ChallengeTicket(Guid Id, DateTimeOffset IssuedUtc, Guid? Member, int ProofBits = 0);

/// <summary>Reads a transport's challenges (ADR-0138); only the transport knows how they are protected.</summary>
internal interface IChallengeReader
{
    /// <summary>
    /// What a challenge holds, when it was issued for this form and has not expired, or expired less than
    /// <paramref name="grace"/> ago; null for anything else: missing, malformed, changed, another form's or too old.
    /// </summary>
    /// <param name="token">The challenge as a request posts it.</param>
    /// <param name="form">The form the request is for.</param>
    /// <param name="grace">How long past its expiry it still counts.</param>
    ChallengeTicket? Read(string? token, Guid form, TimeSpan grace);
}

/// <summary>The challenge's rules (ADR-0012).</summary>
internal static class FormChallenges
{
    /// <summary>How long a challenge counts.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>How long past its expiry a challenge still finds its own entry's receipt.</summary>
    public static readonly TimeSpan ReceiptGrace = TimeSpan.FromHours(24);

    /// <summary>The fewest seconds a person takes from a challenge to a submission.</summary>
    public const int MinFillSeconds = 3;

    // The label that keeps a post's submission id apart from any other use of a challenge's id.
    private const string PostLabel = "Jiron.FormBuilder.PostWithoutScript.v1:";

    /// <summary>A challenge's id as an entry stores it.</summary>
    /// <param name="ticket">The challenge.</param>
    public static string StoredId(ChallengeTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return ticket.Id.ToString("N");
    }

    /// <summary>
    /// The submission id of a post without JavaScript that carries the challenge rendered into its page (ADR-0012,
    /// ADR-0231): the first 16 bytes of SHA-256 over a fixed label and the challenge's id, as a version-8 GUID, so a double
    /// click or a reload of the post is a retry of its entry rather than a second one.
    /// </summary>
    /// <param name="ticket">The challenge the post carried.</param>
    public static Guid PostSubmissionId(ChallengeTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(PostLabel + ticket.Id.ToString("N")), hash);
        // RFC 9562: the version in the high nibble of byte 6 and the variant in the top bits of byte 8, in big-endian order.
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash[..16], bigEndian: true);
    }
}
