using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.ManagementApi;

/// <summary>
/// The management API's answers to refusals, its declarations, how the access filter reads a key, and the names the
/// OpenAPI document gives (ADR-0050), without a host.
/// </summary>
[TestFixture]
public sealed class FormBuilderProblemsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Test]
    public void EachRefusalOfTheServices_AnswersWithItsStatusCodeAndWhatItAdds()
    {
        var messages = new EventMessages();
        messages.Add(new EventMessage("Test", "Not today.", EventMessageType.Error));

        Assert.Multiple(() =>
        {
            Assert.That(Describe(new FormBuilderValidationException([new DefinitionProblem("limit_exceeded", "name", "Too long."), new DefinitionProblem("required", "alias", "Missing.")])), Is.EqualTo("""400 limit_exceeded Some values are not allowed. | Too long. | {"errors":[{"code":"limit_exceeded","path":"name","message":"Too long."},{"code":"required","path":"alias","message":"Missing."}]}"""), "every problem, the first named");
            Assert.That(Describe(new FormBuilderSettingsValidationException([new SettingsError("general.timeZone", "unknown_zone", "Not a time zone.")])), Is.EqualTo("""400 validation_failed Some values are not allowed. | Not a time zone. | {"errors":[{"code":"unknown_zone","path":"general.timeZone","message":"Not a time zone."}]}"""));
            Assert.That(Describe(new FormBuilderConcurrencyException()), Is.EqualTo("409 concurrency_conflict Someone else changed this meanwhile. Load it again, then make your change. |  | {}"));
            Assert.That(Describe(new FormBuilderConflictException(FormBuilderConflict.NotTrashed, "Only a folder in the trash is deleted.")), Is.EqualTo("409 not_trashed Only a folder in the trash is deleted. |  | {}"));
            Assert.That(Describe(new FormBuilderCancelledException(messages)), Is.EqualTo("""409 cancelled The change was stopped by the site. | Not today. | {"messages":["Not today."]}"""));
            Assert.That(Describe(new FormBuilderSettingLockedException("general.timeZone", "JironFormBuilder:General:TimeZone")), Is.EqualTo("""409 setting_locked The site's configuration sets this, so it cannot be changed here. |  | {"field":"general.timeZone","configurationKey":"JironFormBuilder:General:TimeZone"}"""));
            Assert.That(Describe(new FormBuilderQueryTimeoutException()), Is.EqualTo("503 query_timeout The list took too long to load. Try again, or narrow it down. |  | {}"));
            Assert.That(FormBuilderProblems.Of(new InvalidOperationException("internal")), Is.Null, "anything else stays an error");
            Assert.That(() => FormBuilderProblems.Of(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void TheAccessRefusals_SayWhatIsMissing_AndNoMore()
    {
        var http = new DefaultHttpContext();
        var missing = FormBuilderProblems.Missing(FormBuilderPermissions.DeleteEntries);
        var pending = FormBuilderProblems.MigrationsPending(http);
        var noSection = AuthorizationResult.Failed(AuthorizationFailure.Failed([new FormBuilderAccessDenial(Substitute.For<IAuthorizationHandler>(), FormBuilderDenialKind.NoSection, FormBuilderPermissions.None)]));

        Assert.Multiple(() =>
        {
            Assert.That(Describe(FormBuilderProblems.NoSection()), Is.EqualTo("403 no_section You do not have access to the Form Builder section. |  | {}"));
            Assert.That(Describe(missing), Is.EqualTo("""403 forbidden You do not have the permission to do this. | It needs the permission 'deleteEntries'. | {"permission":"deleteEntries"}"""));
            Assert.That(Describe(FormBuilderProblems.Forbidden()), Is.EqualTo("403 forbidden You do not have the permission to do this. |  | {}"));
            Assert.That(Describe(FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form)), Is.EqualTo("404 form_not_found The form does not exist. |  | {}"));
            Assert.That(Describe(FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder)), Is.EqualTo("404 folder_not_found The folder does not exist. |  | {}"));
            Assert.That(Describe(pending), Is.EqualTo("503 migrations_pending Jiron Form Builder is being set up on this site. Try again in a moment. |  | {}"));
            Assert.That(http.Response.Headers.RetryAfter.ToString(), Is.EqualTo("10"));
            Assert.That(Describe(FormBuilderProblems.Refused(noSection, FormBuilderAccessResourceKind.Folder)), Is.EqualTo("403 no_section You do not have access to the Form Builder section. |  | {}"), "a policy that found no section");
            Assert.That(Describe(FormBuilderProblems.Refused(AuthorizationResult.Failed(), FormBuilderAccessResourceKind.Folder)), Is.EqualTo("403 forbidden You do not have the permission to do this. |  | {}"), "a refusal without a denial fails closed");
            Assert.That(Describe(FormBuilderProblems.Missing(FormBuilderPermissions.ManagePermissions, new Dictionary<string, object?> { ["gains"] = Array.Empty<string>() })), Does.EndWith("""{"gains":[],"permission":"managePermissions"}"""));
        });
    }

    [Test]
    public void TheExceptionFilter_AnswersTheServicesRefusals_AsHandled_AndLeavesEverythingElse()
    {
        var filter = new FormBuilderExceptionFilter();
        var refusal = Context(new FormBuilderConcurrencyException());
        var other = Context(new InvalidOperationException("internal"));

        filter.OnException(refusal);
        filter.OnException(other);

        Assert.Multiple(() =>
        {
            Assert.That((refusal.Result as FormBuilderProblemResult)?.StatusCode, Is.EqualTo(StatusCodes.Status409Conflict));
            Assert.That(refusal.ExceptionHandled, Is.True, "no later filter answers again");
            Assert.That(other.Result, Is.Null);
            Assert.That(other.ExceptionHandled, Is.False, "anything else stays an error");
        });

        static ExceptionContext Context(Exception exception) =>
            new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), []) { Exception = exception };
    }

    [Test]
    public void AnInvalidRequest_NamesWhere_NeverWhyInTheServersWords()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("$.parentKey", "The JSON value could not be converted to System.Nullable`1[System.Guid].");
        modelState.AddModelError("take", "The field take must be between 1 and 500.");
        modelState.SetModelValue("skip", "0", "0");

        Assert.That(Describe(FormBuilderProblems.InvalidRequest(modelState)), Is.EqualTo("""400 invalid_request The request has values that cannot be read. |  | {"errors":[{"code":"invalid_value","path":"$.parentKey","message":"This value is missing or not valid."},{"code":"invalid_value","path":"take","message":"This value is missing or not valid."}]}"""));
    }

    [Test]
    public async Task AProblemResult_WritesProblemJson_WithItsExtensionsAtTheTop()
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        var result = FormBuilderProblems.Missing(FormBuilderPermissions.Design);

        await result.ExecuteResultAsync(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        http.Response.Body.Position = 0;
        var body = await JsonNode.ParseAsync(http.Response.Body);

        Assert.Multiple(() =>
        {
            Assert.That((http.Response.StatusCode, http.Response.ContentType, result.StatusCode), Is.EqualTo((403, "application/problem+json", (int?)403)));
            Assert.That(body!.AsObject().Select(property => property.Key), Is.EqualTo(new[] { "type", "title", "status", "detail", "code", "permission" }), "camel case, the extensions at the top");
            Assert.That(body["type"]!.GetValue<string>(), Is.EqualTo("about:blank"), "which Umbraco's client needs to read it as a problem");
            Assert.That((body["detail"]!.GetValue<string>(), body["code"]!.GetValue<string>(), body["permission"]!.GetValue<string>()), Is.EqualTo(("It needs the permission 'design'.", "forbidden", "design")));
        });
    }

    [Test]
    public void ADeclaration_NamesAtLeastOnePermission_OrNone_ForTheSection()
    {
        var section = new FormBuilderSectionAttribute { On = FormBuilderAccessResourceKind.Folder, Key = "key" };

        Assert.Multiple(() =>
        {
            Assert.That(new FormBuilderPermissionAttribute(FormBuilderPermissions.Design | FormBuilderPermissions.DeleteEntries).Permissions, Is.EqualTo(FormBuilderPermissions.Design | FormBuilderPermissions.DeleteEntries));
            Assert.That((new FormBuilderPermissionAttribute(FormBuilderPermissions.Export).On, new FormBuilderPermissionAttribute(FormBuilderPermissions.Export).Key), Is.EqualTo((FormBuilderAccessResourceKind.Global, (string?)null)));
            Assert.That((section.Permissions, section.On, section.Key), Is.EqualTo((FormBuilderPermissions.None, FormBuilderAccessResourceKind.Folder, "key")));
            Assert.That(() => new FormBuilderPermissionAttribute(FormBuilderPermissions.None), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new FormBuilderPermissionAttribute((FormBuilderPermissions)4096), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void TheKey_IsReadFromItsParameter_OrFromTheBody_AndAnythingElseIsSaidSo()
    {
        var key = Guid.NewGuid();
        var action = new ActionDescriptor
        {
            Parameters =
            [
                Parameter("key", typeof(Guid)),
                Parameter("parentKey", typeof(Guid?)),
                Parameter("skip", typeof(int)),
                Parameter("request", typeof(CreateFolderRequestModel), BindingSource.Body),
            ],
        };

        Assert.Multiple(() =>
        {
            Assert.That(Read(action, new Dictionary<string, object?> { ["key"] = key }, "key"), Is.EqualTo((FormBuilderAccessFilter.KeyRead.Read, (Guid?)key)));
            Assert.That(Read(action, new Dictionary<string, object?>(), "parentKey"), Is.EqualTo((FormBuilderAccessFilter.KeyRead.Read, (Guid?)null)), "missing: the root");
            Assert.That(Read(action, new Dictionary<string, object?> { ["key"] = Guid.Empty }, "key"), Is.EqualTo((FormBuilderAccessFilter.KeyRead.Read, (Guid?)null)), "empty: the root");
            Assert.That(Read(action, new Dictionary<string, object?> { ["request"] = new CreateFolderRequestModel { Name = "x", ParentKey = key } }, "ParentKey"), Is.EqualTo((FormBuilderAccessFilter.KeyRead.Read, (Guid?)key)), "a body property in any letter case");
            Assert.That(Read(action, new Dictionary<string, object?> { ["request"] = null }, "parentKey") with { Item2 = null }, Is.EqualTo((FormBuilderAccessFilter.KeyRead.Read, (Guid?)null)), "the parameter of that name comes first");
            Assert.That(Read(new ActionDescriptor { Parameters = [Parameter("request", typeof(CreateFolderRequestModel), BindingSource.Body)] }, new Dictionary<string, object?>(), "parentKey"), Is.EqualTo((FormBuilderAccessFilter.KeyRead.Unreadable, (Guid?)null)), "a body that could not be read");
            Assert.That(Read(action, new Dictionary<string, object?>(), "skip"), Is.EqualTo((FormBuilderAccessFilter.KeyRead.Undeclared, (Guid?)null)), "not a key's type");
            Assert.That(Read(action, new Dictionary<string, object?>(), "name"), Is.EqualTo((FormBuilderAccessFilter.KeyRead.Undeclared, (Guid?)null)), "a body property that is no key");
            Assert.That(Read(action, new Dictionary<string, object?>(), "other"), Is.EqualTo((FormBuilderAccessFilter.KeyRead.Undeclared, (Guid?)null)));
        });
    }

    [Test]
    public void TheDocument_NamesOnlyJironFormBuilders_OperationsAndSchemas()
    {
        var operations = new FormBuilderOperationIdHandler();
        var schemas = new FormBuilderSchemaIdHandler();
        var ours = Description(typeof(TreeController), "Root");
        var theirs = Description(typeof(ControllerBase), "Root");

        Assert.Multiple(() =>
        {
            Assert.That((operations.CanHandle(ours), operations.Handle(ours)), Is.EqualTo((true, "TreeRoot")));
            Assert.That(operations.CanHandle(theirs), Is.False);
            Assert.That(operations.CanHandle(new ApiDescription { ActionDescriptor = new ActionDescriptor() }), Is.False);
            Assert.That(schemas.CanHandle(typeof(FolderResponseModel)), Is.True);
            Assert.That(schemas.CanHandle(typeof(ProblemDetails)), Is.False);
        });
    }

    [Test]
    public void TheFeatureProvider_AddsEveryControllerOfTheManagementApi_AndNothingElse()
    {
        var feature = new ControllerFeature();

        new FormBuilderControllerFeatureProvider().PopulateFeature([], feature);
        new FormBuilderControllerFeatureProvider().PopulateFeature([], feature);

        Assert.That(feature.Controllers.Select(type => type.Name).Order(), Is.EqualTo(new[] { "ActionLastRunController", "ActionTestController", "ActionTypeController", "CanvasController", "CaptchaProviderController", "ContentDocumentTypeController", "DraftOptionsController", "EmailLayoutLibraryController", "EmailPreviewController", "EntryActionRunController", "EntryDigestController", "EntryExportController", "EntryFileController", "EntryFilterSetController", "EntryInboxController", "EntryListController", "EntryLocationController", "EntryPanelController", "EntryStateController", "FieldTypeController", "FolderController", "FormCheckController", "FormController", "FormExchangeController", "FormPreviewController", "FormReferenceController", "FormSettingsController", "InsightController", "LibraryListController", "MeController", "MergeTagPathController", "OptionListController", "OptionListLibraryController", "OptionSourceController", "OutcomePreviewController", "PickerController", "RecentEntryController", "SettingsController", "SetupController", "TemplateController", "TestEmailController", "ThemeLibraryController", "TrashController", "TreeController", "VersionController" }), "once each, never the abstract base");
    }

    private static ParameterDescriptor Parameter(string name, Type type, BindingSource? source = null) =>
        new() { Name = name, ParameterType = type, BindingInfo = new BindingInfo { BindingSource = source ?? BindingSource.Path } };

    private static (FormBuilderAccessFilter.KeyRead, Guid?) Read(ActionDescriptor action, Dictionary<string, object?> arguments, string name)
    {
        var read = FormBuilderAccessFilter.ReadKey(action, arguments, name, out var key);
        return (read, key);
    }

    private static ApiDescription Description(Type controller, string action) => new()
    {
        ActionDescriptor = new ControllerActionDescriptor { ControllerTypeInfo = controller.GetTypeInfo(), ControllerName = controller.Name.Replace("Controller", string.Empty, StringComparison.Ordinal), ActionName = action },
    };

    // Status, code, title | detail | the other extensions.
    private static string Describe(Exception exception) => Describe(FormBuilderProblems.Of(exception)!);

    private static string Describe(FormBuilderProblemResult result)
    {
        var problem = result.Problem;
        var others = new JsonObject();
        foreach (var (name, value) in problem.Extensions.Where(extension => extension.Key != "code"))
        {
            others[name] = JsonSerializer.SerializeToNode(value, Json);
        }

        return $"{problem.Status} {problem.Extensions["code"]} {problem.Title} | {problem.Detail} | {others.ToJsonString()}";
    }
}
