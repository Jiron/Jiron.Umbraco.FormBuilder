using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// The checks a change of a form makes before it writes, shared by the form services (ADR-0044, ADR-0053): its own unit,
/// the version the caller read, the trash, the name, the alias and the comment.
/// </summary>
internal static class FormChecks
{
    /// <summary>What a form is called in the refusals of the trash check.</summary>
    public const string Item = "form";

    /// <summary>The refusal of a change to a form inside a caller's unit of work or an Umbraco scope.</summary>
    public const string OwnUnit = "A change to a form is a unit of work of its own: it tells handlers once it has committed (ADR-0044).";

    /// <summary>
    /// A change starts from the version the caller read, on a form outside the trash unless it applies in the trash
    /// (<paramref name="inTrash"/>: a restore, an unpublish, a trash or a delete).
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">Someone changed the form meanwhile.</exception>
    /// <exception cref="FormBuilderConflictException">The form is in the trash.</exception>
    public static void Current(StoredForm form, int expectedConcurrencyVersion, bool inTrash = false)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.ConcurrencyVersion != expectedConcurrencyVersion)
        {
            throw new FormBuilderConcurrencyException($"The form was changed by someone else meanwhile (version {form.ConcurrencyVersion}, not {expectedConcurrencyVersion}).");
        }

        if (!inTrash)
        {
            OutsideTrash(form);
        }
    }

    /// <summary>A form in the trash takes no change and gives no copy until it is restored.</summary>
    /// <exception cref="FormBuilderConflictException">The form is in the trash.</exception>
    public static void OutsideTrash(StoredForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        LibraryTrash.CheckOutside(form.TrashedUtc, Item);
    }

    /// <summary>A form's name once trimmed (ADR-0044); <paramref name="path"/> is where a refusal points.</summary>
    /// <exception cref="FormBuilderValidationException">The name is not allowed.</exception>
    public static string Name(string name, string path = "name") => LibraryNames.Check(name, Item, FormRepository.MaxNameLength, path);

    /// <summary>An alias the caller chose, when it chose one.</summary>
    /// <exception cref="FormBuilderValidationException">The alias is not allowed.</exception>
    public static void Alias(string? alias)
    {
        if (alias is not null && !FormAliases.IsValid(alias))
        {
            throw FormBuilderValidationException.For(DefinitionErrors.AliasInvalid, "alias", "An alias is a lowercase letter, then letters and digits, at most 64 characters.");
        }
    }

    /// <summary>A version's comment, when there is one.</summary>
    /// <exception cref="FormBuilderValidationException">The comment is too long or holds a lone surrogate.</exception>
    public static void Comment(string? comment)
    {
        if (comment is null)
        {
            return;
        }

        if (comment.Length > FormRepository.MaxCommentLength)
        {
            throw FormBuilderValidationException.For(DefinitionErrors.LimitExceeded, "comment", $"A comment has at most {FormRepository.MaxCommentLength} characters.");
        }

        if (!StrictStringConverter.IsWellFormed(comment))
        {
            throw FormBuilderValidationException.For(DefinitionErrors.InvalidValue, "comment", "The comment holds a lone surrogate.");
        }
    }
}
