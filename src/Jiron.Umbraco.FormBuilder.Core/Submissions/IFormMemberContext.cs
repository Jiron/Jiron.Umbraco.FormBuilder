using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// The member signed in on a request, as its transport knows them (ADR-0231): whom a form for members admits now, and the
/// groups the form's rules ask about, which the server's rules see as the page does (ADR-0138).
/// </summary>
internal interface IFormMemberContext
{
    /// <summary>Whether the form's access admits the member signed in on the request now; false when none is.</summary>
    /// <param name="availability">The form's availability, which says for whom it is.</param>
    Task<bool> AdmitsAsync(FormAvailabilitySettings availability);

    /// <summary>The groups among these, by key or name as a form's rules name them, that the member signed in is in.</summary>
    /// <param name="named">The groups the form's rules name.</param>
    Task<IReadOnlyList<string>> GroupsAmongAsync(IReadOnlySet<string> named);
}
