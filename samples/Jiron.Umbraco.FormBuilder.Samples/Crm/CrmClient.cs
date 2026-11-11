using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Samples.Crm;

/// <summary>A contact for the site's CRM.</summary>
/// <param name="Email">Its email address.</param>
/// <param name="Name">Its name; empty for none.</param>
/// <param name="List">The list it goes on.</param>
public sealed record CrmContact(string Email, string Name, CrmList List);

/// <summary>
/// The site's CRM, as the CRM action reaches it. A site writes its own over its CRM's API; the sample's is a stub.
/// </summary>
public interface ICrmClient
{
    /// <summary>
    /// Adds a contact to a list, once for a delivery key: a repeated attempt of one delivery adds nothing more.
    /// </summary>
    /// <param name="contact">The contact.</param>
    /// <param name="deliveryKey">The delivery's key, the same on each attempt of it.</param>
    /// <param name="cancellationToken">Cancelled when the attempt's time is up.</param>
    /// <exception cref="CrmUnavailableException">The CRM cannot take the contact now; a later attempt may.</exception>
    Task AddContactAsync(CrmContact contact, string deliveryKey, CancellationToken cancellationToken);
}

/// <summary>The CRM cannot take a contact now; a later attempt may.</summary>
public sealed class CrmUnavailableException : Exception
{
    /// <summary>A new exception.</summary>
    public CrmUnavailableException()
        : base("The CRM is not available.")
    {
    }

    /// <summary>A new exception with its message.</summary>
    /// <param name="message">The message.</param>
    public CrmUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>A new exception with its message and cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public CrmUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The sample's CRM: it keeps the contacts it is given in memory, one per delivery key, and logs each by its list
/// only, never by the visitor's address or name. An address at <c>unavailable.test</c> finds it unavailable, so a test
/// sees the action try again. It is one site's memory: a real CRM is the store shared by every server.
/// </summary>
/// <param name="logger">The log.</param>
public sealed partial class StubCrmClient(ILogger<StubCrmClient> logger) : ICrmClient
{
    /// <summary>The domain whose addresses find the stub unavailable.</summary>
    public const string UnavailableDomain = "unavailable.test";

    private readonly ConcurrentDictionary<string, CrmContact> _contacts = new(StringComparer.Ordinal);

    /// <summary>The contacts added, by their delivery key.</summary>
    public IReadOnlyDictionary<string, CrmContact> Contacts => _contacts;

    /// <inheritdoc />
    public Task AddContactAsync(CrmContact contact, string deliveryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentException.ThrowIfNullOrEmpty(deliveryKey);
        cancellationToken.ThrowIfCancellationRequested();
        if (contact.Email.EndsWith("@" + UnavailableDomain, StringComparison.OrdinalIgnoreCase))
        {
            throw new CrmUnavailableException();
        }

        if (_contacts.TryAdd(deliveryKey, contact))
        {
            Added(contact.List);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The CRM stub added a contact to the list {List}.")]
    private partial void Added(CrmList list);
}
