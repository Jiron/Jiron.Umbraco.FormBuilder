using System.Globalization;

namespace Jiron.Umbraco.FormBuilder.Core.Outbound;

/// <summary>Why a call to another host did not give an answer to use (ADR-0076).</summary>
internal enum OutboundFailure
{
    /// <summary>The address is not absolute, or names a user or a password.</summary>
    AddressInvalid,

    /// <summary>The address is <c>http</c> and the host configuration does not allow it, or another scheme.</summary>
    SchemeNotAllowed,

    /// <summary>The host is not one of <c>JironFormBuilder:Webhooks:AllowedHosts</c>.</summary>
    HostNotAllowed,

    /// <summary>The name resolves to an address that is not globally reachable.</summary>
    AddressNotPublic,

    /// <summary>An answer redirects to another host.</summary>
    RedirectToOtherHost,

    /// <summary>The answers redirect more often than a call follows.</summary>
    TooManyRedirects,

    /// <summary>The answer is larger than the call takes.</summary>
    AnswerTooLarge,

    /// <summary>The call took longer than it may.</summary>
    TimedOut,

    /// <summary>The answer's status is not a success.</summary>
    Refused,

    /// <summary>No connection: the name does not resolve, or no address answers.</summary>
    NoConnection,

    /// <summary>The answer is not what the caller reads, such as JSON without a list where the list should be.</summary>
    AnswerUnreadable,
}

/// <summary>
/// A call to another host that gave no answer to use (ADR-0076). Its message names the reason and a refused answer's
/// status only, never the address, which may carry a token, nor what it resolved to.
/// </summary>
internal sealed class FormBuilderOutboundException : Exception
{
    /// <summary>A failure for <paramref name="reason"/>.</summary>
    public FormBuilderOutboundException(OutboundFailure reason, int? status = null, Exception? innerException = null)
        : base(Describe(reason, status), innerException)
    {
        Reason = reason;
        Status = status;
    }

    /// <summary>Why the call failed.</summary>
    public OutboundFailure Reason { get; }

    /// <summary>The status of a refused answer.</summary>
    public int? Status { get; }

    private static string Describe(OutboundFailure reason, int? status) => status is { } code
        ? string.Create(CultureInfo.InvariantCulture, $"The call to another host failed: {reason} ({code}).")
        : $"The call to another host failed: {reason}.";
}
