using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// The puzzle of a form at the proof-of-work level (ADR-0012, ADR-0141): find a number such that the SHA-256 of
/// <c>nonce:number</c> starts with as many zero bits as the challenge asks. The nonce is the challenge's own id, so the
/// signature and the expiry of the challenge hold for it, and a challenge makes one entry, so an answer counts once.
/// </summary>
internal static class ProofOfWork
{
    /// <summary>The longest answer read; a number that long is no answer anyone has worked out.</summary>
    public const int MaxAnswerLength = 20;

    /// <summary>The nonce a challenge's puzzle is on.</summary>
    /// <param name="ticket">The challenge.</param>
    public static string NonceOf(ChallengeTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return ticket.Id.ToString("N");
    }

    /// <summary>Whether a challenge asks for a puzzle and the answer solves it.</summary>
    /// <param name="ticket">The challenge.</param>
    /// <param name="answer">The number, in digits, as the request posted it.</param>
    public static bool IsSolved(ChallengeTicket ticket, string? answer)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return ticket.ProofBits > 0
            && answer is { Length: > 0 and <= MaxAnswerLength }
            && answer.All(char.IsAsciiDigit)
            && LeadingZeroBits(NonceOf(ticket), answer) >= ticket.ProofBits;
    }

    /// <summary>How many zero bits the hash of a nonce and an answer starts with, up to the 32 no puzzle comes near.</summary>
    /// <param name="nonce">The nonce.</param>
    /// <param name="answer">The answer.</param>
    public static int LeadingZeroBits(string nonce, string answer)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.ASCII.GetBytes($"{nonce}:{answer}"), hash);
        return BitOperations.LeadingZeroCount(BinaryPrimitives.ReadUInt32BigEndian(hash));
    }
}
