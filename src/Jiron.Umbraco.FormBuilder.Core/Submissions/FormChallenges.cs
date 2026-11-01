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

    /// <summary>A challenge's id as an entry stores it.</summary>
    /// <param name="ticket">The challenge.</param>
    public static string StoredId(ChallengeTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return ticket.Id.ToString("N");
    }
}
