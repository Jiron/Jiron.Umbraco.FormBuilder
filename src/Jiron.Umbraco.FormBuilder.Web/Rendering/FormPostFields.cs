namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The names a form's post without JavaScript carries besides its fields' (ADR-0100), which the form writes and the
/// surface action reads. A field's alias never starts with <see cref="Prefix"/>, as an alias holds no hyphen.
/// </summary>
internal static class FormPostFields
{
    /// <summary>The prefix of every name of the package's own.</summary>
    public const string Prefix = "jfb-";

    /// <summary>Umbraco's route token, which routes the post to the surface action.</summary>
    public const string RouteToken = "ufprt";

    /// <summary>The route value in the token that names the placement's page after a send (ADR-0129).</summary>
    public const string Redirect = "jfbRedirect";

    /// <summary>The form's key.</summary>
    public const string Form = "jfb-form";

    /// <summary>The form's instance id on the page.</summary>
    public const string Instance = "jfb-instance";

    /// <summary>The public revision the page shows.</summary>
    public const string Revision = "jfb-revision";

    /// <summary>The key of the page shown.</summary>
    public const string Page = "jfb-page";

    /// <summary>The challenge rendered into the page of a form for members, issued to the member signed in (ADR-0012, ADR-0231).</summary>
    public const string Challenge = "jfb-challenge";

    /// <summary>
    /// The token of the saved draft the tab holds, which the runtime writes into a form it sends by post (ADR-0252): the entry
    /// takes the draft away, as an entry sent through the API does.
    /// </summary>
    public const string Resume = "jfb-resume";

    /// <summary>
    /// The query name in the action of a form that stores files, <c>?jfb-post=&lt;key&gt;.&lt;instance&gt;</c>, which tells the
    /// files' reader before routing which form a post is (ADR-0006, ADR-0102).
    /// </summary>
    public const string PostMarker = "jfb-post";

    /// <summary>The button the visitor pressed: <c>back</c>, <c>next</c>, <c>submit</c>, or a row's or a ranking's.</summary>
    public const string Nav = "jfb-nav";

    /// <summary>The prefix of a repeatable group's row count, before the group's alias.</summary>
    public const string RowsPrefix = "jfb-rows-";

    /// <summary>The prefix of a query value the post carries on, before its name.</summary>
    public const string QueryPrefix = "jfb-query-";

    /// <summary>
    /// The prefix of a ranking's order as the page shows it, before its control's name: one value per option, in that
    /// order, which a move acts on.
    /// </summary>
    public const string OrderPrefix = "jfb-order-";

    /// <summary>The button back to the previous page.</summary>
    public const string Back = "back";

    /// <summary>The button to the next page.</summary>
    public const string Next = "next";

    /// <summary>The button that sends the form.</summary>
    public const string Submit = "submit";

    /// <summary>A repeatable group's Add, before the group's alias: <c>add:&lt;alias&gt;</c>.</summary>
    public const string Add = "add";

    /// <summary>A row's Remove, before the group's alias and the row: <c>remove:&lt;alias&gt;:&lt;row&gt;</c>.</summary>
    public const string Remove = "remove";

    /// <summary>A ranking's move up, before its control's name and the option's place: <c>up:&lt;name&gt;:&lt;n&gt;</c>.</summary>
    public const string Up = "up";

    /// <summary>A ranking's move down, as <see cref="Up"/>.</summary>
    public const string Down = "down";

    /// <summary>
    /// An answer's "Change" on the summary page, before the field's alias: <c>goto:&lt;alias&gt;</c>, which opens the page
    /// that holds the field (ADR-0122).
    /// </summary>
    public const string Goto = "goto";

    /// <summary>What separates a button's parts.</summary>
    public const char Separator = ':';
}
