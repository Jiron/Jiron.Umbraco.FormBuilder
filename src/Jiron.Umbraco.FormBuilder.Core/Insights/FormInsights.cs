using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Core.Insights;

/// <summary>What a beacon names (ADR-0211): its type and, by its type, a page's key or a field's path and kind of error.</summary>
/// <param name="Type">The type: <c>view</c>, <c>start</c>, <c>page</c> or <c>invalid</c>.</param>
/// <param name="Page">For <c>page</c>: the key of the page reached.</param>
/// <param name="Field">For <c>invalid</c>: the field's path as the page names it (<c>data-jfb-alias</c>).</param>
/// <param name="Kind">For <c>invalid</c>: the kind of error.</param>
internal sealed record InsightBeacon(string Type, string? Page = null, string? Field = null, string? Kind = null);

/// <summary>
/// Where a beacon's page came from (ADR-0211): one the server rendered for a post of a visit whose view and start were
/// counted as it began, and the page a Next by post reached, if it did.
/// </summary>
/// <param name="Continued">Whether the server rendered the page for a post.</param>
/// <param name="Reached">The page a Next by post reached; null for any other post.</param>
internal sealed record BeaconSource(bool Continued, Guid? Reached = null);

/// <summary>
/// What a form counts of its visitors (ADR-0211): whether its insights are on, for the site and for the form, the beacons
/// its page sends, and the fields the server refuses in a post sent without JavaScript. A beacon is held to the published
/// version: a page it has, a field it has and a kind of error the form knows; whatever else a beacon holds is never read.
/// </summary>
/// <param name="settings">The site's privacy settings.</param>
/// <param name="counts">This server's sums, which the writer writes.</param>
internal sealed class FormInsights(IFormBuilderSettingsService settings, InsightCounts counts)
{
    /// <summary>The most bytes a beacon holds.</summary>
    public const int MaxBeaconBytes = 1024;

    // The longest path of a field a beacon may name.
    private const int MaxPathLength = 512;

    /// <summary>
    /// The kinds of error a beacon or a refusal counts: a field's kinds, its own checks and the validation rules. An error
    /// the page keeps from the server's render of a refused post (the runtime's <c>server</c>) counts nothing: the server
    /// counted that refusal itself.
    /// </summary>
    public static readonly FrozenSet<string> Kinds = FieldMessageProperties.All.Select(property => property.Name)
        .Concat([SubmissionProblems.Check, SubmissionProblems.Rule])
        .ToFrozenSet(StringComparer.Ordinal);

    private readonly ConditionalWeakTable<FormDefinition, FieldMap> _maps = [];

    /// <summary>Whether the form counts what visitors do with it: the site's insights and the form's own are both on.</summary>
    /// <param name="definition">The version the site serves.</param>
    /// <param name="cancellationToken">Cancels the read of the settings.</param>
    public async Task<bool> IsOnAsync(FormDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Settings.Privacy.Insights && (await settings.GetAsync<PrivacySettings>(cancellationToken: cancellationToken)).Value.Insights;
    }

    /// <summary>
    /// A beacon from its body: one JSON object whose <c>type</c>, <c>page</c>, <c>field</c> and <c>kind</c> are strings
    /// where present; every other member is skipped unread. Null for a body that is no such object.
    /// </summary>
    /// <param name="body">The body, at most <see cref="MaxBeaconBytes"/>.</param>
    public static InsightBeacon? Read(ReadOnlySpan<byte> body)
    {
        if (body.Length is 0 or > MaxBeaconBytes)
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(body, new JsonReaderOptions { MaxDepth = 4 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return null;
            }

            string? type = null, page = null, field = null, kind = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.ValueTextEquals("type"u8) ? 1 : reader.ValueTextEquals("page"u8) ? 2 : reader.ValueTextEquals("field"u8) ? 3 : reader.ValueTextEquals("kind"u8) ? 4 : 0;
                if (!reader.Read())
                {
                    return null;
                }

                if (name == 0)
                {
                    reader.Skip();
                    continue;
                }

                if (reader.TokenType != JsonTokenType.String)
                {
                    return null;
                }

                var value = reader.GetString();
                switch (name)
                {
                    case 1 when type is null:
                        type = value;
                        break;
                    case 2 when page is null:
                        page = value;
                        break;
                    case 3 when field is null:
                        field = value;
                        break;
                    case 4 when kind is null:
                        kind = value;
                        break;
                    default:
                        // A member named twice.
                        return null;
                }
            }

            return reader.TokenType == JsonTokenType.EndObject && !reader.Read() && type is not null ? new InsightBeacon(type, page, field, kind) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Counts a beacon of the form's page, when the form's insights are on and the beacon names what the version has;
    /// whether it counted.
    /// </summary>
    /// <param name="form">The form as the site serves it.</param>
    /// <param name="beacon">The beacon.</param>
    /// <param name="source">Where its page came from; null for a first render.</param>
    /// <param name="cancellationToken">Cancels the read of the settings.</param>
    public async Task<bool> CountAsync(PublishedForm form, InsightBeacon beacon, BeaconSource? source = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(beacon);
        if (!InsightMetrics.Beacons.TryGetValue(beacon.Type, out var metric))
        {
            return false;
        }

        if (source is { Continued: true } && metric is InsightMetrics.Views or InsightMetrics.Starts)
        {
            // A page the server rendered for a post: its visit's view and start were counted as it began, and the view of a
            // page a Next by post reached is that page reached, which no jfb:page told.
            if (metric == InsightMetrics.Starts || source.Reached is not { } reached)
            {
                return false;
            }

            (metric, beacon) = (InsightMetrics.Page, beacon with { Page = reached.ToString("D") });
        }

        if (DimensionOf(form.Definition, metric, beacon) is not { } dimension)
        {
            return false;
        }

        return await IsOnAsync(form.Definition, cancellationToken) && counts.Add(form.Form.Key, metric, dimension);
    }

    /// <summary>
    /// Counts the fields the server refused in a post sent without JavaScript, whose page sends no beacons, each field
    /// and kind once, when the form's insights are on (<c>refused</c>, ADR-0211).
    /// </summary>
    /// <param name="form">The form as the site serves it.</param>
    /// <param name="problems">The problems that refused it, by their paths and codes.</param>
    /// <param name="cancellationToken">Cancels the read of the settings.</param>
    public async Task RefusedAsync(PublishedForm form, IEnumerable<SubmissionProblem> problems, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(problems);
        var map = MapOf(form.Definition);
        var refused = problems.Where(problem => Kinds.Contains(problem.Code))
            .Select(problem => (Field: map.FieldAt(problem.Path), Kind: problem.Code))
            .Where(problem => problem.Field is not null)
            .Distinct()
            .ToList();
        if (refused.Count == 0 || !await IsOnAsync(form.Definition, cancellationToken))
        {
            return;
        }

        foreach (var (field, kind) in refused)
        {
            counts.Add(form.Form.Key, InsightMetrics.Refused, InsightMetrics.FieldKindOf(field!.Value, kind));
        }
    }

    // What a beacon counts its metric by; null when it names what the version does not have.
    private string? DimensionOf(FormDefinition definition, string metric, InsightBeacon beacon) =>
        metric switch
        {
            InsightMetrics.Views or InsightMetrics.Starts => string.Empty,
            InsightMetrics.Page => Guid.TryParseExact(beacon.Page, "D", out var page) && definition.Pages.Any(each => each.Key == page) ? InsightMetrics.KeyOf(page) : null,
            InsightMetrics.Invalid => beacon.Kind is { } kind && Kinds.Contains(kind) && MapOf(definition).FieldAt(beacon.Field) is { } field ? InsightMetrics.FieldKindOf(field, kind) : null,
            _ => null,
        };

    private FieldMap MapOf(FormDefinition definition) => _maps.GetValue(definition, static each => new FieldMap(each));

    // A version's fields and repeatable groups' children by the paths the page names them by.
    private sealed class FieldMap
    {
        private readonly Dictionary<string, Guid> _paths = new(StringComparer.Ordinal);

        public FieldMap(FormDefinition definition)
        {
            foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
            {
                Add(field, field.Alias);
                foreach (var child in field.Fields)
                {
                    Add(child, field.Alias is { } group && child.Alias is { } alias ? $"{group}.{alias}" : null);
                }
            }
        }

        // The field a path names, rows' numbers left out: the longest start of it that is a field's path, so a part or a
        // grid row counts under its field.
        public Guid? FieldAt(string? path)
        {
            if (path is not { Length: > 0 and <= MaxPathLength })
            {
                return null;
            }

            var plain = WithoutRows(path);
            while (plain.Length > 0)
            {
                if (_paths.TryGetValue(plain, out var key))
                {
                    return key;
                }

                var dot = plain.LastIndexOf('.');
                plain = dot < 0 ? string.Empty : plain[..dot];
            }

            return null;
        }

        private void Add(FormField field, string? path)
        {
            if (path is not null)
            {
                _paths.TryAdd(path, field.Key);
            }
        }

        // The path without its rows' numbers: guests[2].name is guests.name.
        private static string WithoutRows(string path)
        {
            if (!path.Contains('[', StringComparison.Ordinal))
            {
                return path;
            }

            var plain = new System.Text.StringBuilder(path.Length);
            for (var at = 0; at < path.Length; at++)
            {
                if (path[at] == '[' && path.IndexOf(']', at) is var end and > 0 && end > at + 1 && path.AsSpan(at + 1, end - at - 1).ContainsAnyExceptInRange('0', '9') is false)
                {
                    at = end;
                    continue;
                }

                plain.Append(path[at]);
            }

            return plain.ToString();
        }
    }
}
