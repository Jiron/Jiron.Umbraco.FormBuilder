using System.Text;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// Which view writes each part of a form (ADR-0015, ADR-0093, ADR-0105), over a view engine that knows the paths the
/// test gives: the form's override in its theme's folder first, then the theme's folder, then the default view; a part
/// without a view names the paths looked at; what renders without a form, the default folder's view only (ADR-0095);
/// and in Development, a site's own view whose header names an older contract version, or none, is logged once.
/// </summary>
[TestFixture]
public sealed class FormViewsTests
{
    private const string Folder = "~/Views/JironFormBuilder/default/";

    private const string Themed = "~/Views/JironFormBuilder/bootstrap5/";

    private static readonly string[] Parts = ["Form", "Page", "Group", "Field", "Navigation", "Progress", "ErrorSummary", "Outcome", "Notice", "Summary"];

    [Test]
    public void TheCandidates_AreTheFormsOverride_ThenTheDefaultView() =>
        Assert.That(FormViews.Candidates("default", "contact", "fields/email"), Is.EqualTo(new[]
        {
            Folder + "_forms/contact/fields/email.cshtml",
            Folder + "fields/email.cshtml",
        }));

    [Test]
    public void ForAnotherTheme_TheCandidates_AreTheFormsOverrideInItsFolder_ThenItsFolder_ThenTheDefaultView() =>
        Assert.That(FormViews.Candidates("bootstrap5", "contact", "Field"), Is.EqualTo(new[]
        {
            Themed + "_forms/contact/Field.cshtml",
            Themed + "Field.cshtml",
            Folder + "Field.cshtml",
        }));

    [Test]
    public async Task ForAnotherTheme_EachPart_IsTheFormsOverride_ElseTheThemes_ElseTheDefault()
    {
        var views = Views(Defaults()
            .Append(Themed + "_forms/contact/Field.cshtml")
            .Append(Themed + "Field.cshtml")
            .Append(Themed + "Page.cshtml")
            .Append(Themed + "fields/email.cshtml")
            .Append(Folder + "_forms/contact/Group.cshtml")
            .Append(Folder + "fields/email.cshtml")
            .Append(Folder + "fields/text.cshtml"));

        var (form, parts) = await views.PartsAsync("bootstrap5", "contact", CancellationToken.None);
        var (_, otherParts) = await views.PartsAsync("bootstrap5", "other", CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(form, Is.EqualTo(Folder + "Form.cshtml"));
            Assert.That(parts.Field, Is.EqualTo(Themed + "_forms/contact/Field.cshtml"), "the form's override in the theme's folder");
            Assert.That(otherParts.Field, Is.EqualTo(Themed + "Field.cshtml"), "the theme's view for another form");
            Assert.That(parts.Page, Is.EqualTo(Themed + "Page.cshtml"));
            Assert.That(parts.Group, Is.EqualTo(Folder + "Group.cshtml"), "an override in the default folder belongs to the default theme");
            Assert.That(await views.ControlAsync("bootstrap5", "contact", "email", CancellationToken.None), Is.EqualTo(Themed + "fields/email.cshtml"));
            Assert.That(await views.ControlAsync("bootstrap5", "contact", "text", CancellationToken.None), Is.EqualTo(Folder + "fields/text.cshtml"));
        });
    }

    [Test]
    public async Task EachPart_IsTheDefaultView_UnlessTheFormHasItsOwn()
    {
        var views = Views(Defaults().Append(Folder + "_forms/contact/Field.cshtml").Append(Folder + "_forms/contact/Form.cshtml"));

        var (form, parts) = await views.PartsAsync("default", "contact", CancellationToken.None);
        var (otherForm, otherParts) = await views.PartsAsync("default", "other", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(form, Is.EqualTo(Folder + "_forms/contact/Form.cshtml"));
            Assert.That(parts.Field, Is.EqualTo(Folder + "_forms/contact/Field.cshtml"));
            Assert.That(new[] { parts.Page, parts.Group, parts.Navigation, parts.Progress, parts.ErrorSummary, parts.Outcome, parts.Notice, parts.Summary }, Is.EqualTo(new[]
            {
                Folder + "Page.cshtml", Folder + "Group.cshtml", Folder + "Navigation.cshtml", Folder + "Progress.cshtml",
                Folder + "ErrorSummary.cshtml", Folder + "Outcome.cshtml", Folder + "Notice.cshtml", Folder + "Summary.cshtml",
            }));
            Assert.That(otherForm, Is.EqualTo(Folder + "Form.cshtml"), "another form's override is not this form's");
            Assert.That(otherParts.Field, Is.EqualTo(Folder + "Field.cshtml"));
        });
    }

    [Test]
    public async Task AControl_IsTheViewOfItsType_UnderFields()
    {
        var views = Views([Folder + "fields/email.cshtml", Folder + "_forms/contact/fields/text.cshtml", Folder + "fields/text.cshtml"]);

        Assert.Multiple(async () =>
        {
            Assert.That(await views.ControlAsync("default", "contact", "email", CancellationToken.None), Is.EqualTo(Folder + "fields/email.cshtml"));
            Assert.That(await views.ControlAsync("default", "contact", "text", CancellationToken.None), Is.EqualTo(Folder + "_forms/contact/fields/text.cshtml"));
        });
    }

    [Test]
    public void APartWithoutAView_FailsTheRender_NamingThePathsLookedAt()
    {
        var views = Views(Defaults());

        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => views.ControlAsync("default", "contact", "rating", CancellationToken.None));

        Assert.That(failure!.Message, Does.Contain("'fields/rating'").And.Contain("'contact'").And.Contain(Folder + "_forms/contact/fields/rating.cshtml").And.Contain(Folder + "fields/rating.cshtml"));
    }

    [Test]
    public async Task WithoutAForm_AViewIsTheDefaultFolders_AndOneItLacks_NamesThePathLookedAt()
    {
        var views = Views(Defaults().Append(Folder + "_forms/contact/Notice.cshtml"));

        var notice = await views.DefaultAsync("Notice", CancellationToken.None);
        var failure = Assert.ThrowsAsync<InvalidOperationException>(() => views.DefaultAsync("Note", CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(notice, Is.EqualTo(Folder + "Notice.cshtml"), "no form's override");
            Assert.That(failure!.Message, Does.Contain("'Note'").And.Contain(Folder + "Note.cshtml"));
        });
    }

    [TestCase("@* jfb-view: Field 0 *@", "0")]
    [TestCase("<div>a view without a header</div>", "none")]
    [TestCase("", "none")]
    public async Task InDevelopment_ASitesViewWithAnOlderHeaderOrNone_IsLoggedOnce(string firstLine, string version)
    {
        var path = Folder + "_forms/contact/Field.cshtml";
        var files = new Files { [path[1..]] = firstLine + "\n<div>the rest</div>" };
        var logger = new RecordingLogger<FormViews>();
        var views = Views(Defaults().Append(path), files, logger);

        _ = await views.PartsAsync("default", "contact", CancellationToken.None);
        _ = await views.PartsAsync("default", "contact", CancellationToken.None);

        Assert.That(logger.Entries.Select(entry => (entry.Level, entry.Message)), Is.EqualTo(new[]
        {
            (LogLevel.Warning, $"The view {path} declares contract version {version}, and the built-in view is version 1: compare it with the built-in view and the upgrade notes, then raise its header."),
        }));
    }

    [TestCase("@* jfb-view: Field 1 *@")]
    [TestCase("  @*jfb-view: fields/text 12 *@ <div>")]
    [TestCase("@* jfb-view: fields/acme.mood-2_b 1 *@")]
    public async Task InDevelopment_ASitesViewWithTheCurrentHeaderOrNewer_IsNotLogged(string firstLine)
    {
        var path = Folder + "_forms/contact/Field.cshtml";
        var logger = new RecordingLogger<FormViews>();
        var views = Views(Defaults().Append(path), new Files { [path[1..]] = firstLine }, logger);

        _ = await views.PartsAsync("default", "contact", CancellationToken.None);

        Assert.That(logger.Entries, Is.Empty);
    }

    [Test]
    public async Task TheBuiltInViews_WhichAreNoFilesOfTheSite_AreNotRead()
    {
        var files = new Files();
        var logger = new RecordingLogger<FormViews>();
        var views = Views(Defaults(), files, logger);

        _ = await views.PartsAsync("default", "contact", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(files.Asked, Is.EquivalentTo(Parts.Select(part => "/Views/JironFormBuilder/default/" + part + ".cshtml")), "each looked for once");
            Assert.That(logger.Entries, Is.Empty);
        });
    }

    [Test]
    public async Task OutsideDevelopment_NoViewIsRead()
    {
        var path = Folder + "Field.cshtml";
        var files = new Files { [path[1..]] = "@* jfb-view: Field 0 *@" };
        var logger = new RecordingLogger<FormViews>();
        var views = Views(Defaults(), files, logger, Environments.Production);

        _ = await views.PartsAsync("default", "contact", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(files.Asked, Is.Empty);
            Assert.That(logger.Entries, Is.Empty);
        });
    }

    private static IEnumerable<string> Defaults() => Parts.Select(part => Folder + part + ".cshtml");

    // The views over an engine that finds the given paths, a content root with the given files, in Development
    // unless told otherwise.
    private static FormViews Views(IEnumerable<string> found, Files? files = null, ILogger<FormViews>? logger = null, string environmentName = "Development")
    {
        var paths = found.ToHashSet(StringComparer.Ordinal);
        var engine = Substitute.For<ICompositeViewEngine>();
        engine.GetView(Arg.Is<string?>(path => path == null), Arg.Any<string>(), Arg.Is(false)).Returns(call =>
        {
            var path = call.ArgAt<string>(1);
            return paths.Contains(path) ? ViewEngineResult.Found(path, Substitute.For<IView>()) : ViewEngineResult.NotFound(path, [path]);
        });
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        environment.ContentRootFileProvider.Returns(files ?? new Files());
        return new FormViews(engine, environment, logger ?? new RecordingLogger<FormViews>());
    }

    // A content root of files held in memory, which records the paths it was asked for.
    private sealed class Files : Dictionary<string, string>, IFileProvider
    {
        public List<string> Asked { get; } = [];

        public IFileInfo GetFileInfo(string subpath)
        {
            Asked.Add(subpath);
            return TryGetValue(subpath, out var text) ? new File(subpath, text) : new NotFoundFileInfo(subpath);
        }

        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }

    private sealed class File(string path, string text) : IFileInfo
    {
        public bool Exists => true;

        public long Length => Encoding.UTF8.GetByteCount(text);

        public string? PhysicalPath => null;

        public string Name => Path.GetFileName(path);

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public bool IsDirectory => false;

        public Stream CreateReadStream() => new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
