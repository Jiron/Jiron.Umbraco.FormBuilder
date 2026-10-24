using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The refusals of the form service and their stable codes (ADR-0036, ADR-0044): one mapping, a code for every conflict,
/// and exceptions that carry what the APIs answer with.
/// </summary>
[TestFixture]
public sealed partial class FormRefusalTests
{
    // Written out apart from the code: a code is part of the APIs and never changes once released.
    private static readonly Dictionary<FormBuilderConflict, string> Codes = new()
    {
        [FormBuilderConflict.DuplicateAlias] = "alias_taken",
        [FormBuilderConflict.DuplicateKey] = "key_taken",
        [FormBuilderConflict.MissingFolder] = "folder_missing",
        [FormBuilderConflict.FolderIntoItself] = "folder_into_itself",
        [FormBuilderConflict.FolderTooDeep] = "folder_too_deep",
        [FormBuilderConflict.FolderNotEmpty] = "folder_not_empty",
        [FormBuilderConflict.InUse] = "in_use",
        [FormBuilderConflict.DuplicateSubmission] = "duplicate_submission",
        [FormBuilderConflict.ChallengeUsed] = "challenge_used",
        [FormBuilderConflict.MissingFile] = "file_missing",
        [FormBuilderConflict.MissingField] = "field_missing",
        [FormBuilderConflict.MissingVersion] = "version_missing",
        [FormBuilderConflict.Trashed] = "trashed",
    };

    [Test]
    public void EveryConflict_HasItsOwnCode_InSnakeCase()
    {
        var codes = Enum.GetValues<FormBuilderConflict>().Select(FormBuilderErrorCodes.Of).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(Enum.GetValues<FormBuilderConflict>(), Is.EquivalentTo(Codes.Keys), "every conflict is written out here");
            foreach (var (conflict, code) in Codes)
            {
                Assert.That(FormBuilderErrorCodes.Of(conflict), Is.EqualTo(code), conflict.ToString());
            }

            Assert.That(codes, Is.Unique);
            Assert.That(codes.Concat([FormBuilderErrorCodes.ConcurrencyConflict, FormBuilderErrorCodes.Cancelled]), Has.All.Matches<string>(code => SnakeCase().IsMatch(code)));
            Assert.That(() => FormBuilderErrorCodes.Of((FormBuilderConflict)99), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void ARefusal_AnswersWithItsCode()
    {
        var validation = new FormBuilderValidationException([Problem(DefinitionErrors.LimitExceeded), Problem(DefinitionErrors.AliasInvalid)]);

        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderErrorCodes.Of(new FormBuilderConcurrencyException("Stale.")), Is.EqualTo("concurrency_conflict"));
            Assert.That(FormBuilderErrorCodes.Of(new FormBuilderConflictException(FormBuilderConflict.Trashed, "In the trash.")), Is.EqualTo("trashed"));
            Assert.That(FormBuilderErrorCodes.Of(validation), Is.EqualTo("limit_exceeded"), "the first problem's code");
            Assert.That(FormBuilderErrorCodes.Of(new FormBuilderCancelledException(new EventMessages())), Is.EqualTo("cancelled"));
            Assert.That(FormBuilderErrorCodes.Of(new InvalidOperationException()), Is.Null, "no refusal");
            Assert.That(() => FormBuilderErrorCodes.Of((Exception)null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void AValidationRefusal_KeepsEveryProblem_AndNamesTheFirst()
    {
        var one = FormBuilderValidationException.For(DefinitionErrors.InvalidValue, "name", "A form needs a name.");
        var two = new FormBuilderValidationException([Problem(DefinitionErrors.LimitExceeded), Problem(DefinitionErrors.AliasInvalid)]);

        Assert.Multiple(() =>
        {
            Assert.That(one.Problems, Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.InvalidValue, "name", "A form needs a name.") }));
            Assert.That(one.Message, Is.EqualTo("invalid_value at name: A form needs a name."));
            Assert.That(two.Problems.Select(problem => problem.Code), Is.EqualTo(new[] { "limit_exceeded", "alias_invalid" }));
            Assert.That(two.Message, Is.EqualTo("limit_exceeded at $.pages: Too many. (and 1 more)"));
            Assert.That(() => new FormBuilderValidationException([]), Throws.InstanceOf<ArgumentOutOfRangeException>(), "a refusal has a problem");
            Assert.That(() => new FormBuilderValidationException(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void ACancelledChange_CarriesTheHandlersMessages()
    {
        var messages = new EventMessages();
        messages.Add(new EventMessage("Review policy", "Forms are not published on Fridays.", EventMessageType.Error));
        messages.Add(new EventMessage("Naming", "Use the house prefix.", EventMessageType.Warning));

        var cancelled = new FormBuilderCancelledException(messages);
        var silent = new FormBuilderCancelledException(new EventMessages());

        Assert.Multiple(() =>
        {
            Assert.That(cancelled.Messages, Is.EqualTo(new[]
            {
                new CancelMessage("Review policy", "Forms are not published on Fridays."),
                new CancelMessage("Naming", "Use the house prefix."),
            }));
            Assert.That(cancelled.Message, Is.EqualTo("A notification handler cancelled the change: Forms are not published on Fridays. Use the house prefix."));
            Assert.That(silent.Messages, Is.Empty);
            Assert.That(silent.Message, Is.EqualTo("A notification handler cancelled the change."));
            Assert.That(() => new FormBuilderCancelledException(null!), Throws.ArgumentNullException);
        });
    }

    private static DefinitionProblem Problem(string code) => new(code, "$.pages", "Too many.");

    [GeneratedRegex("^[a-z]+(_[a-z]+)*$")]
    private static partial Regex SnakeCase();
}
