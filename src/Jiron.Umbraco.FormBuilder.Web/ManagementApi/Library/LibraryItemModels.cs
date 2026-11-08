using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>The forms that use an item of the library: an option list, a theme or an email layout (ADR-0214, ADR-0215).</summary>
internal sealed record LibraryUsersResponseModel
{
    /// <summary>The forms the user may open, by name.</summary>
    public required IReadOnlyList<LibraryUserResponseModel> Forms { get; init; }

    /// <summary>How many more forms use it that the user may not open.</summary>
    public required int Others { get; init; }

    /// <summary>The forms as the service gives them.</summary>
    public static LibraryUsersResponseModel Of(LibraryItemUsers users)
    {
        ArgumentNullException.ThrowIfNull(users);
        return new LibraryUsersResponseModel
        {
            Forms = [.. users.Forms.Select(form => new LibraryUserResponseModel { Key = form.FormKey, Name = form.FormName, InDraft = form.InDraft, InLive = form.InLive, IsTrashed = form.IsTrashed, Uses = form.Uses })],
            Others = users.Others,
        };
    }
}

/// <summary>A form that uses an item of the library.</summary>
internal sealed record LibraryUserResponseModel
{
    /// <summary>The form's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether its draft uses the item.</summary>
    public required bool InDraft { get; init; }

    /// <summary>Whether the version the site serves uses it.</summary>
    public required bool InLive { get; init; }

    /// <summary>Whether the form is in the recycle bin.</summary>
    public required bool IsTrashed { get; init; }

    /// <summary>How many of its elements use the item, in the version that uses it most: fields for a list, actions for a layout.</summary>
    public required int Uses { get; init; }
}

/// <summary>Why something an editor holds does not read, at its path, as a save would refuse it.</summary>
internal sealed record LibraryProblemResponseModel
{
    /// <summary>The problem's code, such as <c>setting_invalid</c>.</summary>
    public required string Code { get; init; }

    /// <summary>Where it is, such as <c>$.config.url</c>.</summary>
    public required string Path { get; init; }

    /// <summary>What is wrong, in English.</summary>
    public required string Message { get; init; }

    /// <summary>A problem as the checks give it.</summary>
    public static LibraryProblemResponseModel Of(DefinitionProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return new LibraryProblemResponseModel { Code = problem.Code, Path = problem.Path, Message = problem.Message };
    }
}
