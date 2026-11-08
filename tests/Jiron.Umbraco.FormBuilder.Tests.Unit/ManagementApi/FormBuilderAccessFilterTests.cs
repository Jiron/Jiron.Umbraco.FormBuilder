using System.Security.Claims;
using System.Security.Principal;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security.Authorization;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.ManagementApi;

/// <summary>
/// The access filter of the management API (ADR-0050) without a host: the order of its checks, what each refusal
/// answers, that it fails closed for an action that declares its access wrongly, and the visibility handler it asks.
/// </summary>
[TestFixture]
public sealed class FormBuilderAccessFilterTests
{
    private const string Section = "Jiron.FormBuilder.Section";
    private static readonly Guid Events = Guid.Parse("8d1f3b2a-4c5e-4f60-8a71-9b0c1d2e3f01");
    private static readonly FormInfo Spring = new(Guid.Parse("8d1f3b2a-4c5e-4f60-8a71-9b0c1d2e3f02"), "spring", "Spring", Events, 1, false, 0, false, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null);
    private static readonly FolderInfo EventsFolder = new(Events, "Events", null, 0, false, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null);

    private IAuthorizationService _authorization = null!;
    private Migrations _migrations = null!;
    private IFormBuilderFormService _forms = null!;
    private IFormBuilderFolderService _folders = null!;
    private IFormBuilderAccessService _access = null!;
    private RecordingLogger _logger = null!;
    private List<string> _asked = null!;

    [SetUp]
    public void CreateTheServices()
    {
        _asked = [];
        _authorization = Substitute.For<IAuthorizationService>();
        _authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>()).Returns(call =>
        {
            _asked.Add($"{call.ArgAt<string>(2)}@{Describe(call.ArgAt<object?>(1))}");
            return AuthorizationResult.Success();
        });
        _migrations = new Migrations();
        _forms = Substitute.For<IFormBuilderFormService>();
        _forms.GetAsync(Spring.Key, Arg.Any<CancellationToken>()).Returns(Spring);
        _folders = Substitute.For<IFormBuilderFolderService>();
        _folders.GetAsync(Events, Arg.Any<CancellationToken>()).Returns(EventsFolder);
        _access = Substitute.For<IFormBuilderAccessService>();
        _logger = new RecordingLogger();
    }

    [Test]
    public async Task AnActionWithoutExactlyOneDeclaration_IsRefused_AndLogged()
    {
        var none = await RunAsync([]);
        var two = await RunAsync([new FormBuilderSectionAttribute(), new FormBuilderPermissionAttribute(FormBuilderPermissions.Design)]);

        Assert.Multiple(() =>
        {
            Assert.That(none.Outcome, Is.EqualTo("403 forbidden"));
            Assert.That(two.Outcome, Is.EqualTo("403 forbidden"));
            Assert.That(_logger.Errors, Is.EqualTo(new[]
            {
                "The management API action Test.Action carries 0 access declarations instead of one; it is refused.",
                "The management API action Test.Action carries 2 access declarations instead of one; it is refused.",
            }));
            Assert.That(_asked, Is.Empty, "nothing is evaluated");
        });
    }

    [Test]
    public async Task TheSectionComesFirst_ThenTheMigrations_BeforeAnythingIsRead()
    {
        _authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), FormBuilderPolicies.Section).Returns(AuthorizationResult.Failed());
        var outsider = await RunAsync([Folder(FormBuilderPermissions.Design)], ("key", Events));
        var askedWhileRefused = _migrations.Asked;

        _authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), FormBuilderPolicies.Section).Returns(AuthorizationResult.Success());
        _migrations.Pending = true;
        var pending = await RunAsync([Folder(FormBuilderPermissions.Design)], ("key", Events));

        Assert.Multiple(async () =>
        {
            Assert.That(outsider.Outcome, Is.EqualTo("403 no_section"));
            Assert.That(askedWhileRefused, Is.Zero, "the migration state is not asked for an outsider");
            Assert.That((pending.Outcome, pending.Http.Response.Headers.RetryAfter.ToString()), Is.EqualTo(("503 migrations_pending", "10")));
            await _folders.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
            Assert.That(pending.NextRan, Is.False);
        });
    }

    [Test]
    public async Task AnUnknownPrincipal_IsChallenged()
    {
        var unknown = await RunAsync([new FormBuilderSectionAttribute()], [], known: false);

        Assert.Multiple(() =>
        {
            Assert.That(unknown.Result, Is.InstanceOf<ChallengeResult>());
            Assert.That(_asked, Is.Empty);
        });
    }

    [Test]
    public async Task ATicketsAction_NeedsNoUser_ButWaitsForTheMigrations_AndLeavesTheTicketToTheAction()
    {
        _migrations.Pending = true;
        var pending = await RunAsync([new FormBuilderTicketAccessAttribute()], [], known: false, signedIn: false);
        _migrations.Pending = false;
        // A plain link carries no sign-in at all: the request is anonymous, not a sign-in without a user.
        var anonymous = await RunAsync([new FormBuilderTicketAccessAttribute()], [], known: false, signedIn: false);
        var withATicketToo = await RunAsync([new FormBuilderTicketAccessAttribute(), new FormBuilderSectionAttribute()], [], known: false);

        Assert.Multiple(() =>
        {
            Assert.That((pending.Outcome, pending.NextRan), Is.EqualTo(("503 migrations_pending", false)));
            Assert.That(anonymous.NextRan, Is.True, "a plain link's request reaches the action, which reads its ticket");
            Assert.That(anonymous.Http.Features.Get<FormBuilderRequestAccess>(), Is.Null, "no user stands behind it");
            Assert.That(withATicketToo.Outcome, Is.EqualTo("403 forbidden"), "still exactly one declaration");
            Assert.That(_asked, Is.Empty, "no policy is evaluated");
        });
    }

    [Test]
    public async Task AKeyThatNamesNothing_IsRefused_AndLogged()
    {
        var answer = await RunAsync([new FormBuilderPermissionAttribute(FormBuilderPermissions.Design) { On = FormBuilderAccessResourceKind.Folder, Key = "folderKey" }], ("key", Events));
        var withoutKey = await RunAsync([new FormBuilderPermissionAttribute(FormBuilderPermissions.Design) { On = FormBuilderAccessResourceKind.Folder }], ("key", Events));

        Assert.Multiple(() =>
        {
            Assert.That((answer.Outcome, withoutKey.Outcome), Is.EqualTo(("403 forbidden", "403 forbidden")));
            Assert.That(_logger.Errors, Is.EqualTo(new[]
            {
                "The access declaration of the management API action Test.Action names the key folderKey, which no parameter or body property holds; it is refused.",
                "The access declaration of the management API action Test.Action names the key (none), which no parameter or body property holds; it is refused.",
            }));
        });
    }

    [Test]
    public async Task AFolderOrFormThatDoesNotExist_IsNotFound_BeforeAnyPermission()
    {
        var form = await RunAsync([new FormBuilderPermissionAttribute(FormBuilderPermissions.Design) { On = FormBuilderAccessResourceKind.Form, Key = "key" }], ("key", Guid.NewGuid()));
        var noForm = await RunAsync([new FormBuilderPermissionAttribute(FormBuilderPermissions.Design) { On = FormBuilderAccessResourceKind.Form, Key = "key" }], ("key", Guid.Empty));
        var folder = await RunAsync([Folder(FormBuilderPermissions.Design)], ("key", Guid.NewGuid()));

        Assert.Multiple(() =>
        {
            Assert.That((form.Outcome, noForm.Outcome, folder.Outcome), Is.EqualTo(("404 form_not_found", "404 form_not_found", "404 folder_not_found")));
            Assert.That(_asked, Is.EqualTo(new[] { "Jiron.FormBuilder.Policy.Section@global", "Jiron.FormBuilder.Policy.Section@global", "Jiron.FormBuilder.Policy.Section@global" }));
        });
    }

    [Test]
    public async Task EachPermission_IsAskedInTheTablesOrder_OnTheResource_AndTheFirstDenialAnswers()
    {
        var allowed = await RunAsync([Folder(FormBuilderPermissions.DeleteEntries | FormBuilderPermissions.Design)], ("key", Events));
        var asked = _asked.ToList();
        Deny(FormBuilderPolicies.DeleteEntries, FormBuilderDenialKind.MissingPermission, FormBuilderPermissions.DeleteEntries);
        var missing = await RunAsync([Folder(FormBuilderPermissions.DeleteEntries | FormBuilderPermissions.Design)], ("key", Events));
        Deny(FormBuilderPolicies.Design, FormBuilderDenialKind.NotFound, FormBuilderPermissions.Design);
        var hidden = await RunAsync([Folder(FormBuilderPermissions.DeleteEntries | FormBuilderPermissions.Design)], ("key", Events));
        var onForm = await RunAsync([new FormBuilderPermissionAttribute(FormBuilderPermissions.Design) { On = FormBuilderAccessResourceKind.Form, Key = "key" }], ("key", Spring.Key));

        Assert.Multiple(() =>
        {
            Assert.That(allowed.NextRan, Is.True);
            Assert.That(asked, Is.EqualTo(new[] { "Jiron.FormBuilder.Policy.Section@global", $"Jiron.FormBuilder.Policy.design@folder {Events}", $"Jiron.FormBuilder.Policy.deleteEntries@folder {Events}" }));
            Assert.That((missing.Outcome, missing.Permission), Is.EqualTo(("403 forbidden", "deleteEntries")));
            Assert.That(hidden.Outcome, Is.EqualTo("404 folder_not_found"));
            Assert.That(onForm.Outcome, Is.EqualTo("404 form_not_found"), "a form the user does not see");
        });
    }

    [TestCase("content")]
    [TestCase("media")]
    [TestCase("member")]
    [TestCase("Settings")]
    public async Task ContentAccess_NeedsOneOfUmbracosSectionsWhereAPickerIsUsed_NotTheFormsSection(string section)
    {
        _authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), FormBuilderPolicies.Section).Returns(AuthorizationResult.Failed());
        var answered = await RunAsync([new FormBuilderContentAccessAttribute()], [], user: User(section));

        Assert.Multiple(() =>
        {
            Assert.That(answered.NextRan, Is.True, "in any letter case, without the Forms section");
            Assert.That(answered.Http.Features.Get<FormBuilderRequestAccess>()?.User.HasSection, Is.False, "the action learns that the user lacks the section");
            Assert.That(_asked, Is.Empty, "no policy of Jiron Form Builder is asked");
        });
    }

    [Test]
    public async Task ContentAccess_RefusesAUserWithNoneOfThoseSections_ThenWaitsForTheMigrations_ThenReadsTheValues()
    {
        FormBuilderAccessAttribute[] declaration = [new FormBuilderContentAccessAttribute()];
        var formsOnly = await RunAsync(declaration, [], user: User(Section, "users", "translation"));
        var askedWhileRefused = _migrations.Asked;
        var invalid = await RunAsync(declaration, [], invalid: true, user: User("content"));
        _migrations.Pending = true;
        var pending = await RunAsync(declaration, [], invalid: true, user: User("content"));
        var anonymous = await RunAsync(declaration, [], known: false);

        Assert.Multiple(() =>
        {
            Assert.That((formsOnly.Outcome, formsOnly.Section), Is.EqualTo(("403 forbidden", "content")));
            Assert.That(askedWhileRefused, Is.Zero, "the migration state is not asked for a refused user");
            Assert.That(invalid.Outcome, Is.EqualTo("400 invalid_request"));
            Assert.That(pending.Outcome, Is.EqualTo("503 migrations_pending"), "before the values");
            Assert.That(anonymous.Result, Is.TypeOf<ChallengeResult>());
        });
    }

    [Test]
    public async Task AnUmbracoSection_IsCheckedAfterThePermissions_AndARefusalNamesIt()
    {
        FormBuilderAccessAttribute[] declaration = [new FormBuilderPermissionAttribute(FormBuilderPermissions.ManageSettings) { UmbracoSection = "settings" }];
        var without = await RunAsync(declaration, [], user: User(Section));
        var with = await RunAsync(declaration, [], user: User(Section, "Settings"));
        Deny(FormBuilderPolicies.ManageSettings, FormBuilderDenialKind.MissingPermission, FormBuilderPermissions.ManageSettings);
        var neither = await RunAsync(declaration, [], user: User(Section));

        Assert.Multiple(() =>
        {
            Assert.That((without.Outcome, without.Section, without.NextRan), Is.EqualTo(("403 forbidden", "settings", false)));
            Assert.That(with.NextRan, Is.True, "in any letter case");
            Assert.That((neither.Outcome, neither.Permission, neither.Section), Is.EqualTo(("403 forbidden", "manageSettings", (string?)null)), "the permission first");
        });
    }

    [Test]
    public async Task ASectionDeclaration_AsksWhatTheUserSees_OnlyWhenItNamesAFolderOrForm()
    {
        var global = await RunAsync([new FormBuilderSectionAttribute()]);
        var globalAsked = _asked.ToList();
        _asked.Clear();
        var onFolder = await RunAsync([new FormBuilderSectionAttribute { On = FormBuilderAccessResourceKind.Folder, Key = "key" }], ("key", Events));
        var atRoot = await RunAsync([new FormBuilderSectionAttribute { On = FormBuilderAccessResourceKind.Folder, Key = "key" }], ("key", null));

        Assert.Multiple(() =>
        {
            Assert.That((global.NextRan, onFolder.NextRan, atRoot.NextRan), Is.EqualTo((true, true, true)));
            Assert.That(globalAsked, Is.EqualTo(new[] { "Jiron.FormBuilder.Policy.Section@global" }));
            Assert.That(_asked, Is.EqualTo(new[]
            {
                "Jiron.FormBuilder.Policy.Section@global",
                $"Jiron.FormBuilder.Policy.Visible@folder {Events}",
                "Jiron.FormBuilder.Policy.Section@global",
                "Jiron.FormBuilder.Policy.Visible@folder root",
            }));
        });
    }

    [Test]
    public async Task ARequestThatCannotBeRead_IsRefused_AfterTheChecks_OrBeforeThemWhenItHoldsTheKey()
    {
        var invalid = await RunAsync([Folder(FormBuilderPermissions.Design)], [("key", Events)], invalid: true);
        var unreadableBody = await RunAsync([new FormBuilderPermissionAttribute(FormBuilderPermissions.Design) { On = FormBuilderAccessResourceKind.Folder, Key = "parentKey" }], [("request", null)], invalid: true, body: true);

        Assert.Multiple(async () =>
        {
            Assert.That((invalid.Outcome, invalid.NextRan), Is.EqualTo(("400 invalid_request", false)));
            Assert.That(unreadableBody.Outcome, Is.EqualTo("400 invalid_request"));
            Assert.That(_asked.Count(asked => asked.Contains("design", StringComparison.Ordinal)), Is.EqualTo(1), "only the readable request's folder was checked");
            await _folders.Received(1).GetAsync(Events, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task AllowedRequests_ReachTheAction_WithTheUserAndWhatWasRead()
    {
        var user = User(Section);
        var onForm = await RunAsync([new FormBuilderPermissionAttribute(FormBuilderPermissions.Design) { On = FormBuilderAccessResourceKind.Form, Key = "key" }], [("key", Spring.Key)], user: user);
        var onFolder = await RunAsync([Folder(FormBuilderPermissions.Design)], [("key", Events)], user: user);
        var access = onForm.Http.Features.Get<FormBuilderRequestAccess>();

        Assert.Multiple(() =>
        {
            Assert.That(onForm.NextRan, Is.True);
            Assert.That((access!.User.Key, access.Form, access.Folder), Is.EqualTo((user.Key, Spring, (FolderInfo?)null)));
            Assert.That(onFolder.Http.Features.Get<FormBuilderRequestAccess>()!.Folder, Is.EqualTo(EventsFolder));
        });
    }

    [Test]
    public async Task TheVisibilityHandler_LetsThroughWhoSeesTheResource_AndRefusesOthersAsNotFound()
    {
        _access.GetForFolderAsync(Arg.Any<AccessUser>(), Events, Arg.Any<CancellationToken>()).Returns(FormBuilderPermissions.None);
        _access.GetFolderVisibilityAsync(Arg.Any<AccessUser>(), Arg.Any<CancellationToken>()).Returns(new FolderVisibility(false, new HashSet<Guid>()));
        var hidden = await VisibleAsync(User(Section), FormBuilderAccessResource.ForFolder(Events));
        _access.GetFolderVisibilityAsync(Arg.Any<AccessUser>(), Arg.Any<CancellationToken>()).Returns(new FolderVisibility(false, new HashSet<Guid> { Events }));
        var onTheWay = await VisibleAsync(User(Section), FormBuilderAccessResource.ForFolder(Events));
        _access.GetForFormAsync(Arg.Any<AccessUser>(), Arg.Any<FormAccessTarget>(), Arg.Any<CancellationToken>()).Returns(FormBuilderPermissions.ViewEntries);
        var form = await VisibleAsync(User(Section), FormBuilderAccessResource.ForForm(FormAccessTarget.Of(Spring)));

        Assert.Multiple(async () =>
        {
            Assert.That(hidden, Is.EqualTo("NotFound:None"));
            Assert.That(onTheWay, Is.EqualTo("succeeded"), "a folder on the way to something the user holds");
            Assert.That(form, Is.EqualTo("succeeded"));
            Assert.That(await VisibleAsync(User(Section), FormBuilderAccessResource.ForFolder(null)), Is.EqualTo("succeeded"), "the root");
            Assert.That(await VisibleAsync(User(Section), null), Is.EqualTo("succeeded"), "globally");
            Assert.That(await VisibleAsync(User("content"), FormBuilderAccessResource.ForFolder(Events)), Is.EqualTo("NoSection:None"));
            Assert.That(await VisibleAsync(null, FormBuilderAccessResource.ForFolder(Events)), Is.EqualTo("undecided"), "not a backoffice user");
        });
    }

    private static FormBuilderPermissionAttribute Folder(FormBuilderPermissions permissions) =>
        new(permissions) { On = FormBuilderAccessResourceKind.Folder, Key = "key" };

    private static string Describe(object? resource) => resource switch
    {
        FormBuilderAccessResource { Kind: FormBuilderAccessResourceKind.Folder, FolderKey: var key } => $"folder {key?.ToString() ?? "root"}",
        FormBuilderAccessResource { Kind: FormBuilderAccessResourceKind.Form, Form: var form } => $"form {form!.Key}",
        _ => "global",
    };

    private static IUser User(params string[] sections)
    {
        var user = Substitute.For<IUser>();
        user.Key.Returns(Guid.NewGuid());
        user.Groups.Returns([]);
        user.AllowedSections.Returns(sections);
        return user;
    }

    private static IAuthorizationHelper Helper(IUser? user)
    {
        var helper = Substitute.For<IAuthorizationHelper>();
        helper.TryGetUmbracoUser(Arg.Any<IPrincipal>(), out Arg.Any<IUser?>()).Returns(call =>
        {
            call[1] = user;
            return user is not null;
        });
        return helper;
    }

    private void Deny(string policy, FormBuilderDenialKind kind, FormBuilderPermissions permission) =>
        _authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), policy)
            .Returns(AuthorizationResult.Failed(AuthorizationFailure.Failed([new FormBuilderAccessDenial(Substitute.For<IAuthorizationHandler>(), kind, permission)])));

    private Task<Run> RunAsync(FormBuilderAccessAttribute[] declarations, params (string Name, Guid? Value)[] arguments) =>
        RunAsync(declarations, arguments, user: User(Section));


    private async Task<Run> RunAsync(FormBuilderAccessAttribute[] declarations, (string Name, Guid? Value)[] arguments, bool invalid = false, bool body = false, IUser? user = null, bool known = true, bool signedIn = true)
    {
        user ??= User(Section);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(signedIn ? new ClaimsIdentity("test") : new ClaimsIdentity()) };
        var action = new ActionDescriptor
        {
            DisplayName = "Test.Action",
            EndpointMetadata = [.. declarations],
            Parameters = body
                ? [new ParameterDescriptor { Name = "request", ParameterType = typeof(CreateFolderRequestModel), BindingInfo = new BindingInfo { BindingSource = BindingSource.Body } }]
                : [.. arguments.Select(argument => new ParameterDescriptor { Name = argument.Name, ParameterType = typeof(Guid?), BindingInfo = new BindingInfo { BindingSource = BindingSource.Path } })],
        };
        var context = new ActionExecutingContext(new ActionContext(http, new RouteData(), action), [], arguments.ToDictionary(argument => argument.Name, argument => (object?)argument.Value), new object());
        if (invalid)
        {
            context.ModelState.AddModelError("name", "The name is required.");
        }

        var ran = false;
        var filter = new FormBuilderAccessFilter(_authorization, Helper(known ? user : null), _migrations, _forms, _folders, _logger);
        await filter.OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(context, [], new object()));
        });
        return new Run(context.Result, ran, http);
    }

    private async Task<string> VisibleAsync(IUser? user, object? resource)
    {
        var context = new AuthorizationHandlerContext([FormBuilderVisibilityRequirement.Instance], new ClaimsPrincipal(new ClaimsIdentity("test")), resource);
        await new FormBuilderVisibilityHandler(Helper(user), _access).HandleAsync(context);
        return context.HasSucceeded ? "succeeded"
            : context.FailureReasons.OfType<FormBuilderAccessDenial>().SingleOrDefault() is { } denial ? $"{denial.Kind}:{denial.Permission}"
            : "undecided";
    }

    private sealed record Run(IActionResult? Result, bool NextRan, HttpContext Http)
    {
        public string Outcome => Result is FormBuilderProblemResult problem ? $"{problem.StatusCode} {problem.Problem.Extensions["code"]}" : NextRan ? "ran" : $"{Result}";

        public string? Permission => (Result as FormBuilderProblemResult)?.Problem.Extensions.TryGetValue("permission", out var name) == true ? (string?)name : null;

        public string? Section => (Result as FormBuilderProblemResult)?.Problem.Extensions.TryGetValue("section", out var name) == true ? (string?)name : null;
    }

    private sealed class Migrations : IFormBuilderMigrationState
    {
        public bool Pending { get; set; }

        public int Asked { get; private set; }

        public ValueTask<bool> IsPendingAsync(CancellationToken cancellationToken = default)
        {
            Asked++;
            return ValueTask.FromResult(Pending);
        }

        public void Invalidate()
        {
        }
    }

    private sealed class RecordingLogger : ILogger<FormBuilderAccessFilter>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }
}
