using System.Text.Encodings.Web;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The view component's result with the assets (ADR-0093): the assets go into the page's writer before the form, as
/// content that decides when the page is written, whether the result runs synchronously or not.
/// </summary>
[TestFixture]
public sealed class FormBuilderFormViewComponentTests
{
    [Test]
    public void Run_WritesTheAssetsBeforeTheForm()
    {
        var (context, writer) = Context();

        new FormBuilderFormViewComponent.AssetsFirst(new HtmlString("<script>assets</script>"), new ContentViewComponentResult("<form>")).Execute(context);

        Assert.That(writer.ToString(), Is.EqualTo("<script>assets</script>&lt;form&gt;"));
    }

    [Test]
    public async Task RunAsynchronously_WritesTheAssetsBeforeTheForm()
    {
        var (context, writer) = Context();

        await new FormBuilderFormViewComponent.AssetsFirst(new HtmlString("<script>assets</script>"), new ContentViewComponentResult("<form>")).ExecuteAsync(context);

        Assert.That(writer.ToString(), Is.EqualTo("<script>assets</script>&lt;form&gt;"));
    }

    [Test]
    public void WithoutAContext_TheResultIsRefused()
    {
        var result = new FormBuilderFormViewComponent.AssetsFirst(HtmlString.Empty, new ContentViewComponentResult(string.Empty));

        Assert.Multiple(() =>
        {
            Assert.That(() => result.Execute(null!), Throws.ArgumentNullException);
            Assert.That(() => result.ExecuteAsync(null!), Throws.ArgumentNullException);
        });
    }

    private static (ViewComponentContext Context, StringWriter Writer) Context()
    {
        var writer = new StringWriter();
        return (new ViewComponentContext { ViewContext = new ViewContext { Writer = writer }, HtmlEncoder = HtmlEncoder.Default }, writer);
    }
}
