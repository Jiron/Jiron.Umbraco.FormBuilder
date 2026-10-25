using System.Reflection;
using System.Security.Claims;
using System.Security.Principal;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using NSubstitute;
using OpenIddict.Validation.AspNetCore;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Security.Authorization;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.ManagementApi;

/// <summary>
/// The management API's policies and handlers without a host (ADR-0049): their names, their scheme and requirements, the
/// resources, and each way a handler lets a user through or refuses with its denial.
/// </summary>
[TestFixture]
public sealed class FormBuilderAuthorizationTests
{
    private const string Section = "Jiron.FormBuilder.Section";
    private static readonly Guid Events = Guid.Parse("6d1f3b2a-4c5e-4f60-8a71-9b0c1d2e3f01");
    private static readonly FormAccessTarget Spring = new(Guid.Parse("6d1f3b2a-4c5e-4f60-8a71-9b0c1d2e3f02"), Events, false);

    private IFormBuilderAccessService _access = null!;

    [SetUp]
    public void CreateTheAccessService() => _access = Substitute.For<IFormBuilderAccessService>();

    [Test]
    public void EachPermissionsPolicy_IsNamedAfterItsStableName_AndHasItsConstant()
    {
        var constants = typeof(FormBuilderPolicies).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.Name is not nameof(FormBuilderPolicies.Prefix) and not nameof(FormBuilderPolicies.Section) and not nameof(FormBuilderPolicies.Visible))
            .Select(field => (string)field.GetRawConstantValue()!);

        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderPolicies.Section, Is.EqualTo("Jiron.FormBuilder.Policy.Section"));
            Assert.That(FormBuilderPolicies.Visible, Is.EqualTo("Jiron.FormBuilder.Policy.Visible"));
            Assert.That(FormBuilderPolicies.For(FormBuilderPermissions.ViewEntries), Is.EqualTo("Jiron.FormBuilder.Policy.viewEntries"));
            Assert.That(constants, Is.EquivalentTo(PermissionRules.Each.Select(FormBuilderPolicies.For)));
            Assert.That(() => FormBuilderPolicies.For(FormBuilderPermissions.Design | FormBuilderPermissions.Export), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void EveryPolicy_TakesTheBackofficesToken_AndASignedInUser_AndItsOneRequirement()
    {
        var options = new AuthorizationOptions();

        FormBuilderPolicies.AddTo(options);

        Assert.Multiple(() =>
        {
            AssertPolicy(options.GetPolicy(FormBuilderPolicies.Section), FormBuilderSectionRequirement.Instance);
            AssertPolicy(options.GetPolicy(FormBuilderPolicies.Visible), FormBuilderVisibilityRequirement.Instance);
            foreach (var permission in PermissionRules.Each)
            {
                AssertPolicy(options.GetPolicy(FormBuilderPolicies.For(permission)), new FormBuilderPermissionRequirement(permission));
            }

            Assert.That(() => FormBuilderPolicies.AddTo(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void AResource_IsGlobal_AFolderOrTheRoot_OrAForm_WithItsFolder()
    {
        var root = FormBuilderAccessResource.ForFolder(null);
        var events = FormBuilderAccessResource.ForFolder(Events);
        var form = FormBuilderAccessResource.ForForm(Spring);

        Assert.Multiple(() =>
        {
            Assert.That((FormBuilderAccessResource.Global.Kind, FormBuilderAccessResource.Global.FolderKey, FormBuilderAccessResource.Global.Form), Is.EqualTo((FormBuilderAccessResourceKind.Global, (Guid?)null, (FormAccessTarget?)null)));
            Assert.That((root.Kind, root.FolderKey), Is.EqualTo((FormBuilderAccessResourceKind.Folder, (Guid?)null)));
            Assert.That((events.Kind, events.FolderKey), Is.EqualTo((FormBuilderAccessResourceKind.Folder, (Guid?)Events)));
            Assert.That((form.Kind, form.FolderKey, form.Form), Is.EqualTo((FormBuilderAccessResourceKind.Form, (Guid?)Events, Spring)));
            Assert.That(() => FormBuilderAccessResource.ForForm(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void ADenial_SaysWhy_AndIsFoundInTheResultOfItsCheck()
    {
        var handler = Substitute.For<IAuthorizationHandler>();
        var notFound = new FormBuilderAccessDenial(handler, FormBuilderDenialKind.NotFound, FormBuilderPermissions.Design);

        Assert.Multiple(() =>
        {
            Assert.That(new FormBuilderAccessDenial(handler, FormBuilderDenialKind.NoSection, FormBuilderPermissions.None).Message, Is.EqualTo("The user has no access to the Forms section."));
            Assert.That(notFound.Message, Is.EqualTo("The user cannot see the folder or form."));
            Assert.That(new FormBuilderAccessDenial(handler, FormBuilderDenialKind.MissingPermission, FormBuilderPermissions.Export).Message, Is.EqualTo("The user lacks the permission 'export'."));
            Assert.That((notFound.Kind, notFound.Permission, notFound.Handler), Is.EqualTo((FormBuilderDenialKind.NotFound, FormBuilderPermissions.Design, handler)));
            Assert.That(FormBuilderAccessDenial.Of(AuthorizationResult.Failed(AuthorizationFailure.Failed([notFound]))), Is.SameAs(notFound));
            Assert.That(FormBuilderAccessDenial.Of(AuthorizationResult.Failed(AuthorizationFailure.Failed([new AuthorizationFailureReason(handler, "other")]))), Is.Null);
            Assert.That(FormBuilderAccessDenial.Of(AuthorizationResult.Failed()), Is.Null);
            Assert.That(FormBuilderAccessDenial.Of(AuthorizationResult.Success()), Is.Null);
        });
    }

    [Test]
    public async Task TheSectionHandler_LetsAUserOfTheSectionThrough_RefusesOthers_AndLeavesAnUnknownPrincipal()
    {
        Assert.Multiple(async () =>
        {
            Assert.That(await SectionAsync(User(Section)), Is.EqualTo("succeeded"));
            Assert.That(await SectionAsync(User("content", Section.ToUpperInvariant())), Is.EqualTo("NoSection:None"));
            Assert.That(await SectionAsync(null), Is.EqualTo("undecided"), "not a backoffice user");
        });
    }

    [Test]
    public async Task ThePermissionHandler_RefusesWithoutTheSection_BeforeItEvaluatesAnything()
    {
        var outcome = await PermissionAsync(User("content"), FormBuilderPermissions.Design, FormBuilderAccessResource.ForFolder(Events));

        Assert.Multiple(() =>
        {
            Assert.That(outcome, Is.EqualTo("NoSection:Design"));
            Assert.That(_access.ReceivedCalls(), Is.Empty);
        });
    }

    [Test]
    public async Task ThePermissionHandler_ChecksGlobally_WithoutOneOfItsResources()
    {
        _access.GetGlobalAsync(Arg.Any<AccessUser>(), Arg.Any<CancellationToken>()).Returns(FormBuilderPermissions.ManageLibrary);

        Assert.Multiple(async () =>
        {
            Assert.That(await PermissionAsync(User(Section), FormBuilderPermissions.ManageLibrary, null), Is.EqualTo("succeeded"));
            Assert.That(await PermissionAsync(User(Section), FormBuilderPermissions.ManageLibrary, new object()), Is.EqualTo("succeeded"), "an endpoint's own resource");
            Assert.That(await PermissionAsync(User(Section), FormBuilderPermissions.ManageSettings, FormBuilderAccessResource.Global), Is.EqualTo("MissingPermission:ManageSettings"), "the global checks are always seen");
            Assert.That(await PermissionAsync(null, FormBuilderPermissions.ManageLibrary, null), Is.EqualTo("undecided"), "not a backoffice user");
        });
    }

    [Test]
    public async Task OnAFolder_AUserWhoSeesIt_LacksThePermission_AndOneWhoDoesNot_FindsNothing()
    {
        var user = User(Section);
        _access.GetForFolderAsync(Arg.Any<AccessUser>(), Events, Arg.Any<CancellationToken>()).Returns(FormBuilderPermissions.ViewEntries);
        var seeing = await PermissionAsync(user, FormBuilderPermissions.Design, FormBuilderAccessResource.ForFolder(Events));
        var holding = await PermissionAsync(user, FormBuilderPermissions.ViewEntries, FormBuilderAccessResource.ForFolder(Events));
        _access.GetForFolderAsync(Arg.Any<AccessUser>(), Events, Arg.Any<CancellationToken>()).Returns(FormBuilderPermissions.None);
        _access.GetFolderVisibilityAsync(Arg.Any<AccessUser>(), Arg.Any<CancellationToken>()).Returns(new FolderVisibility(false, new HashSet<Guid> { Events }));
        var onTheWay = await PermissionAsync(user, FormBuilderPermissions.Design, FormBuilderAccessResource.ForFolder(Events));
        _access.GetFolderVisibilityAsync(Arg.Any<AccessUser>(), Arg.Any<CancellationToken>()).Returns(new FolderVisibility(false, new HashSet<Guid>()));
        var hidden = await PermissionAsync(user, FormBuilderPermissions.Design, FormBuilderAccessResource.ForFolder(Events));
        var root = await PermissionAsync(user, FormBuilderPermissions.Design, FormBuilderAccessResource.ForFolder(null));

        Assert.Multiple(() =>
        {
            Assert.That(seeing, Is.EqualTo("MissingPermission:Design"), "it holds something there");
            Assert.That(holding, Is.EqualTo("succeeded"));
            Assert.That(onTheWay, Is.EqualTo("MissingPermission:Design"), "on the way to a form granted on its own");
            Assert.That(hidden, Is.EqualTo("NotFound:Design"));
            Assert.That(root, Is.EqualTo("MissingPermission:Design"), "the root is always seen");
        });
    }

    [Test]
    public async Task OnAForm_AUserWhoHoldsNothingThere_FindsNothing()
    {
        var user = User(Section);
        _access.GetForFormAsync(Arg.Any<AccessUser>(), Spring, Arg.Any<CancellationToken>()).Returns(FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Export);
        var both = await PermissionAsync(user, FormBuilderPermissions.Export, FormBuilderAccessResource.ForForm(Spring));
        var lacking = await PermissionAsync(user, FormBuilderPermissions.EditEntries, FormBuilderAccessResource.ForForm(Spring));
        _access.GetForFormAsync(Arg.Any<AccessUser>(), Spring, Arg.Any<CancellationToken>()).Returns(FormBuilderPermissions.None);
        var nothing = await PermissionAsync(user, FormBuilderPermissions.EditEntries, FormBuilderAccessResource.ForForm(Spring));

        Assert.Multiple(async () =>
        {
            Assert.That(both, Is.EqualTo("succeeded"));
            Assert.That(lacking, Is.EqualTo("MissingPermission:EditEntries"));
            Assert.That(nothing, Is.EqualTo("NotFound:EditEntries"));
            await _access.DidNotReceive().GetFolderVisibilityAsync(Arg.Any<AccessUser>(), Arg.Any<CancellationToken>());
        });
    }

    private static void AssertPolicy(AuthorizationPolicy? policy, IAuthorizationRequirement requirement)
    {
        Assert.That(policy, Is.Not.Null);
        Assert.That(policy!.AuthenticationSchemes, Is.EqualTo(new[] { OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme }));
        Assert.That(policy.Requirements.OfType<DenyAnonymousAuthorizationRequirement>().Count(), Is.EqualTo(1));
        Assert.That(policy.Requirements.Where(each => each is not DenyAnonymousAuthorizationRequirement), Is.EqualTo(new[] { requirement }));
    }

    private static IUser User(params string[] sections)
    {
        var user = Substitute.For<IUser>();
        user.Key.Returns(Guid.NewGuid());
        user.Groups.Returns([]);
        user.AllowedSections.Returns(sections);
        return user;
    }

    // The helper Umbraco gives, knowing the principal as the user, or not at all for null.
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

    private static string Decision(AuthorizationHandlerContext context) =>
        context.HasSucceeded ? "succeeded"
        : context.FailureReasons.OfType<FormBuilderAccessDenial>().SingleOrDefault() is { } denial ? $"{denial.Kind}:{denial.Permission}"
        : context.HasFailed ? "failed"
        : "undecided";

    private static async Task<string> SectionAsync(IUser? user)
    {
        var context = new AuthorizationHandlerContext([FormBuilderSectionRequirement.Instance], new ClaimsPrincipal(new ClaimsIdentity("test")), null);
        await new FormBuilderSectionHandler(Helper(user)).HandleAsync(context);
        return Decision(context);
    }

    private async Task<string> PermissionAsync(IUser? user, FormBuilderPermissions permission, object? resource)
    {
        var context = new AuthorizationHandlerContext([new FormBuilderPermissionRequirement(permission)], new ClaimsPrincipal(new ClaimsIdentity("test")), resource);
        await new FormBuilderPermissionHandler(Helper(user), _access).HandleAsync(context);
        return Decision(context);
    }
}
