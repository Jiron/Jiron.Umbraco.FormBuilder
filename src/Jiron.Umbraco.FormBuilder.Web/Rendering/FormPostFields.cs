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

    /// <summary>The form's key.</summary>
    public const string Form = "jfb-form";

    /// <summary>The form's instance id on the page.</summary>
    public const string Instance = "jfb-instance";

    /// <summary>The public revision the page shows.</summary>
    public const string Revision = "jfb-revision";

    /// <summary>The key of the page shown.</summary>
    public const string Page = "jfb-page";

    /// <summary>The button the visitor pressed: <c>back</c>, <c>next</c>, <c>submit</c>, or a row's or a ranking's (6.2i).</summary>
    public const string Nav = "jfb-nav";

    /// <summary>The prefix of a repeatable group's row count, before the group's alias.</summary>
    public const string RowsPrefix = "jfb-rows-";

    /// <summary>The prefix of a query value the post carries on, before its name.</summary>
    public const string QueryPrefix = "jfb-query-";

    /// <summary>The button back to the previous page.</summary>
    public const string Back = "back";

    /// <summary>The button to the next page.</summary>
    public const string Next = "next";

    /// <summary>The button that sends the form.</summary>
    public const string Submit = "submit";
}
