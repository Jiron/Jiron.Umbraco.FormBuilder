using System.Text;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Forms;

/// <summary>
/// Every notification of the form and folder services a site published, whether a unit of work was open when a handler
/// got it, and switches that make the handlers cancel, fail or change the library meanwhile.
/// </summary>
internal sealed class FormNotificationLog
{
    private readonly List<(INotification Notification, bool InUnitOfWork)> _seen = [];

    /// <summary>The notifications so far, oldest first.</summary>
    public IReadOnlyList<INotification> Seen => _seen.Select(entry => entry.Notification).ToList();

    /// <summary>Whether any handler got its notification while a unit of work was open.</summary>
    public bool AnyInUnitOfWork => _seen.Any(entry => entry.InUnitOfWork);

    /// <summary>True to make the handlers cancel every change they are told about beforehand.</summary>
    public bool Cancel { get; set; }

    /// <summary>True to make the handlers fail after a change, once they recorded it.</summary>
    public bool Fail { get; set; }

    /// <summary>Runs in the handler of every notification before a change, before it would cancel.</summary>
    public Func<CancelableNotification, Task>? Before { get; set; }

    /// <summary>Records a notification.</summary>
    public void Add(INotification notification, bool inUnitOfWork) => _seen.Add((notification, inUnitOfWork));
}

/// <summary>Records a notification of the form or folder service as an extension's handler gets it, and acts on the log's switches.</summary>
/// <typeparam name="T">The notification.</typeparam>
internal sealed class FormNotificationRecorder<T>(FormNotificationLog log, FormBuilderDatabase database) : INotificationAsyncHandler<T>
    where T : INotification
{
    /// <summary>The category of the message a cancelling handler gives.</summary>
    public const string Category = "JFB_TEST";

    /// <summary>The message a cancelling handler gives.</summary>
    public const string Message = "Not now.";

    /// <inheritdoc />
    public async Task HandleAsync(T notification, CancellationToken cancellationToken)
    {
        log.Add(notification, database.InUnitOfWork);
        if (notification is CancelableNotification before)
        {
            if (log.Before is { } change)
            {
                await change(before);
            }

            if (log.Cancel)
            {
                before.CancelOperation(new EventMessage(Category, Message, EventMessageType.Error));
            }
        }
        else if (log.Fail)
        {
            throw new InvalidOperationException("JFB_TEST: a handler that fails");
        }
    }
}

/// <summary>A publish check beside the structural one, whose problems a test sets.</summary>
internal sealed class TestPublishCheck : IFormPublishCheck
{
    /// <summary>The problems the check finds in every definition; none at first.</summary>
    public IReadOnlyList<DefinitionProblem> Problems { get; set; } = [];

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DefinitionProblem>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Problems);
}

/// <summary>What the tests of the form service share.</summary>
internal static class FormServiceTesting
{
    /// <summary>
    /// Asserts that <paramref name="change"/> is refused with <paramref name="code"/>, the code the APIs answer with,
    /// and, when given, that the first problem is at <paramref name="path"/>. The refusal, or null.
    /// </summary>
    public static Exception? AssertCode(AsyncTestDelegate change, string code, string? path = null)
    {
        var exception = Assert.CatchAsync(change);
        Assert.That(exception is null ? null : FormBuilderErrorCodes.Of(exception), Is.EqualTo(code), exception?.Message);
        if (path is not null)
        {
            Assert.That((exception as FormBuilderValidationException)?.Problems[0].Path, Is.EqualTo(path));
        }

        return exception;
    }

    /// <summary>The definition with these fields in its first group.</summary>
    public static FormDefinition WithFields(FormDefinition definition, params FormField[] fields) =>
        definition with { Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = fields }] }, .. definition.Pages.Skip(1)] };

    /// <summary>The stored document of a definition, as the service writes it, for a draft the service would refuse now.</summary>
    public static VersionDocument Stored(FormDefinition definition)
    {
        var canonical = FormDefinitionSerializer.Write(definition);
        return new VersionDocument(
            FormDefinition.CurrentSchemaVersion,
            Encoding.UTF8.GetString(canonical),
            FormDefinitionSerializer.Hash(canonical),
            PublicProjection.Hash(definition, FieldTypeCatalog.BuiltIn));
    }

    /// <summary>The hash of a definition's canonical form, to compare two definitions.</summary>
    public static string Hash(FormDefinition definition) => FormDefinitionSerializer.Hash(FormDefinitionSerializer.Write(definition));
}
