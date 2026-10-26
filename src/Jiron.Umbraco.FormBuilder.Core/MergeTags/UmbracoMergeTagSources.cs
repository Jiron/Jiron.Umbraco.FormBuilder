using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// <c>page.name</c>, <c>page.url</c>, <c>page.&lt;alias&gt;</c> and <c>page.up.&lt;alias&gt;</c> (ADR-0014, ADR-0067):
/// the content page the form is on, its name in the culture, its address, a property's value in the culture, and the
/// value of the page or its nearest ancestor that has one. Without a page, or for a page that is gone, every path is
/// empty; so is a property the page's type lacks.
/// </summary>
/// <param name="umbraco">The content.</param>
/// <param name="baseUrls">The sites' base URLs.</param>
/// <param name="cultures">The culture formats.</param>
internal sealed class PageMergeTagSource(MergeTagUmbraco umbraco, SiteBaseUrl baseUrls, MergeTagCultures cultures) : IFiniteMergeTagSource
{
    /// <inheritdoc />
    public string Name => "page";

    /// <inheritdoc />
    public bool IsPerVisitor => false;

    /// <inheritdoc />
    public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) => [new("page.name"), new("page.url")];

    /// <inheritdoc />
    /// <remarks>The name, the address, any property alias, and <c>up.</c> with any property alias.</remarks>
    public bool Knows(string path, FormDefinition form, MergeTagPlace place) => path.StartsWith("page.", StringComparison.Ordinal) && IsPath(path["page.".Length..]);

    /// <inheritdoc />
    public async ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
    {
        if (!IsPath(request.Path))
        {
            return null;
        }

        var scope = request.Scope;
        if (scope.Page is not { } key || await umbraco.ContentAsync(key) is not { } page)
        {
            return MergeTagValue.Empty;
        }

        var culture = await umbraco.CultureAsync(scope.Culture);
        switch (request.Path)
        {
            case "name":
                return MergeTagValue.Of(MergeTagPropertyValues.Name(page, culture));
            case "url":
                return await baseUrls.GetAsync(scope.Site, cancellationToken) is { } baseUrl && umbraco.Url(page, culture, baseUrl) is { } url
                    ? MergeTagValue.Of(url)
                    : MergeTagValue.Empty;
        }

        var formats = cultures.For(scope.Culture);
        if (!request.Path.StartsWith("up.", StringComparison.Ordinal))
        {
            return MergeTagPropertyValues.Of(page, request.Path, culture, formats) ?? MergeTagValue.Empty;
        }

        var alias = request.Path["up.".Length..];
        if (MergeTagPropertyValues.Of(page, alias, culture, formats) is { } own)
        {
            return own;
        }

        foreach (var ancestorKey in umbraco.Ancestors(page.Key))
        {
            if (await umbraco.ContentAsync(ancestorKey) is { } ancestor && MergeTagPropertyValues.Of(ancestor, alias, culture, formats) is { } inherited)
            {
                return inherited;
            }
        }

        return MergeTagValue.Empty;
    }

    /// <summary>Whether a path after <c>page.</c> is one of the source's: one segment, or <c>up.</c> and one segment.</summary>
    private static bool IsPath(string path) =>
        path.StartsWith("up.", StringComparison.Ordinal) ? IsSegment(path["up.".Length..]) : path != "up" && IsSegment(path);

    private static bool IsSegment(string text) => text.Length > 0 && !text.Contains('.', StringComparison.Ordinal);
}

/// <summary>
/// <c>site.name</c> and <c>site.url</c> (ADR-0014, ADR-0067): the name of the site's root content in the culture and the
/// site's base URL; empty without a site, and the base URL empty when there is none.
/// </summary>
/// <param name="umbraco">The content.</param>
/// <param name="baseUrls">The sites' base URLs.</param>
internal sealed class SiteMergeTagSource(MergeTagUmbraco umbraco, SiteBaseUrl baseUrls) : IFiniteMergeTagSource
{
    /// <inheritdoc />
    public string Name => "site";

    /// <inheritdoc />
    public bool IsPerVisitor => false;

    /// <inheritdoc />
    public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) => [new("site.name"), new("site.url")];

    /// <inheritdoc />
    public async ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
    {
        var scope = request.Scope;
        switch (request.Path)
        {
            case "name":
                return scope.Site is { } key && await umbraco.ContentAsync(key) is { } root
                    ? MergeTagValue.Of(MergeTagPropertyValues.Name(root, await umbraco.CultureAsync(scope.Culture)))
                    : MergeTagValue.Empty;
            case "url":
                return await baseUrls.GetAsync(scope.Site, cancellationToken) is { } baseUrl ? MergeTagValue.Of(baseUrl) : MergeTagValue.Empty;
            default:
                return null;
        }
    }
}

/// <summary>
/// <c>member.name</c>, <c>member.email</c> and <c>member.&lt;alias&gt;</c> (ADR-0014, ADR-0067): the signed-in member's
/// name, email address and a property's value. A property shows only when the member type lets the member see it and
/// does not mark it sensitive, so a merge tag never shows a member, or puts in an entry, what Umbraco keeps from them.
/// Without a member, every path is empty.
/// </summary>
/// <param name="umbraco">The members.</param>
/// <param name="cultures">The culture formats.</param>
internal sealed class MemberMergeTagSource(MergeTagUmbraco umbraco, MergeTagCultures cultures) : IFiniteMergeTagSource
{
    /// <inheritdoc />
    public string Name => "member";

    /// <inheritdoc />
    public bool IsPerVisitor => true;

    /// <inheritdoc />
    public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) => [new("member.name"), new("member.email")];

    /// <inheritdoc />
    /// <remarks>The name, the email address and any property alias.</remarks>
    public bool Knows(string path, FormDefinition form, MergeTagPlace place) =>
        path.StartsWith("member.", StringComparison.Ordinal) && path.Length > "member.".Length && path.IndexOf('.', "member.".Length) < 0;

    /// <inheritdoc />
    public async ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
    {
        var path = request.Path;
        if (path.Length == 0 || path.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        var scope = request.Scope;
        if (scope.Member is not { } key || await umbraco.MemberAsync(key) is not { } found)
        {
            return MergeTagValue.Empty;
        }

        switch (path)
        {
            case "name":
                return MergeTagValue.Of(found.Member.Name ?? string.Empty);
            case "email":
                return MergeTagValue.Of(found.Member.Email ?? string.Empty);
        }

        if (found.Published is not { } published
            || published.GetProperty(path) is not { } property
            || found.Type is not { } type
            || !type.MemberCanViewProperty(property.Alias)
            || type.IsSensitiveProperty(property.Alias))
        {
            return MergeTagValue.Empty;
        }

        return MergeTagPropertyValues.Of(published, path, await umbraco.CultureAsync(scope.Culture), cultures.For(scope.Culture)) ?? MergeTagValue.Empty;
    }
}

/// <summary>
/// <c>dictionary.&lt;key&gt;</c> (ADR-0014, ADR-0067): an item of Umbraco's dictionary in the culture, with its fallback
/// through Umbraco's languages; the rest of the path is the key, dots included. An item that is not there renders empty
/// with a warning, as an unknown path does.
/// </summary>
/// <param name="dictionary">The dictionary.</param>
internal sealed class DictionaryMergeTagSource(MergeTagDictionary dictionary) : IFiniteMergeTagSource
{
    /// <inheritdoc />
    public string Name => "dictionary";

    /// <inheritdoc />
    public bool IsPerVisitor => false;

    /// <inheritdoc />
    /// <remarks>Any key: whether an item has it shows only when the template renders.</remarks>
    public bool Knows(string path, FormDefinition form, MergeTagPlace place) => path.StartsWith("dictionary.", StringComparison.Ordinal) && path.Length > "dictionary.".Length;

    /// <inheritdoc />
    public async ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken) =>
        request.Path.Length > 0 && await dictionary.TextAsync(request.Path, request.Scope.Culture) is { } text ? MergeTagValue.Of(text) : null;
}
