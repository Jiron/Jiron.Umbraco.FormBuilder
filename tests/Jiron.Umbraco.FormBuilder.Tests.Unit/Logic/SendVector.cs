using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Runs a vector's <c>send</c> as the pipeline refuses a send (ADR-0112, ADR-0266): the page's post read by the no-JS
/// post's reader, its answers read and checked by <see cref="SubmissionReader"/>, each error named by its path, and a
/// send with no answer at all refused with its fields' errors, or with the empty submission's problem when no field has
/// one. The runtime's checks in <c>packages/logic</c> run the same cases.
/// </summary>
internal static class SendVector
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The refusal as both runners write it: <c>{ "problems": [{ "code", "path" }] }</c>, none for a send that passes.</summary>
    public static async Task<JsonObject> RunAsync(string id, JsonElement send)
    {
        var definition = PostVector.Definition(id, send);
        var (fields, rows) = FormPostReader.Read(PostVector.Collection(send), definition, TestFieldTypes.Registry).Posted();
        var top = definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Where(field => field.Alias is not null).ToDictionary(field => field.Alias!, StringComparer.Ordinal);
        Guid KeyOf(string alias) => top[alias].Key;
        Guid ChildOf(string group, string alias) => top[group].Fields.Single(child => child.Alias == alias).Key;

        var options = new OptionListTestSite().Options();
        var site = MergeTagTestSite.Create(options: options);
        var read = await new SubmissionReader(TestFieldTypes.Registry, site.Service, options, new RecordingLogger<SubmissionReader>()).ReadAsync(new PostedSubmission
        {
            Scope = MergeTagTestSite.Scope(form: definition),
            Now = Now,
            TimeZone = "Europe/Zurich",
            Fields = fields.ToDictionary(pair => KeyOf(pair.Key), pair => pair.Value),
            Rows = rows.ToDictionary(
                pair => KeyOf(pair.Key),
                pair => (IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>)[.. pair.Value.Select(row => (IReadOnlyDictionary<Guid, PostedAnswer>)row.ToDictionary(child => ChildOf(pair.Key, child.Key), child => child.Value))]),
        });

        var paths = new FormSubmissions.FieldPaths(definition);
        IReadOnlyList<SubmissionProblem> problems = [.. read.Errors.Select(paths.Problem)];
        if (FormSubmissions.IsEmpty(fields, rows))
        {
            problems = FormSubmissions.EmptySend(problems);
        }

        return new JsonObject
        {
            ["problems"] = new JsonArray([.. problems.Select(problem => (JsonNode)new JsonObject { ["code"] = problem.Code, ["path"] = problem.Path })]),
        };
    }
}
