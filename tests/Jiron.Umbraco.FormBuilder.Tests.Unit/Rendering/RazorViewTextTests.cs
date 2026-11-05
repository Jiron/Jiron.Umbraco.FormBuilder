using System.Globalization;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// A Razor view written to text (ADR-0160, ADR-0187): with its model, in the culture asked for, the request's own culture
/// given back afterwards, also when the view fails; without a culture, in the request's.
/// </summary>
[TestFixture]
public sealed class RazorViewTextTests
{
    [Test]
    public async Task AView_WritesItsModel_InTheCultureAskedFor_AndTheRequestsCultureComesBack()
    {
        var view = new Recording();
        var (culture, ui) = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);

        var german = await RazorViewText.WriteAsync(view, Action(), "the model", Substitute.For<ITempDataProvider>(), CultureInfo.GetCultureInfo("de-DE"));
        var asIs = await RazorViewText.WriteAsync(view, Action(), "the model", Substitute.For<ITempDataProvider>());

        Assert.Multiple(() =>
        {
            Assert.That(german, Is.EqualTo("the model in de-DE/de-DE"));
            Assert.That(asIs, Is.EqualTo($"the model in {culture.Name}/{ui.Name}"), "the request's own culture without one");
            Assert.That((CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture), Is.EqualTo((culture, ui)), "given back");
        });
    }

    [Test]
    public void AViewThatFails_StillGivesTheRequestsCultureBack()
    {
        var culture = CultureInfo.CurrentCulture;

        Assert.ThrowsAsync<InvalidOperationException>(() =>
            RazorViewText.WriteAsync(new Recording { Fails = true }, Action(), "the model", Substitute.For<ITempDataProvider>(), CultureInfo.GetCultureInfo("de-DE")));
        Assert.That(CultureInfo.CurrentCulture, Is.EqualTo(culture));
    }

    private static ActionContext Action() => new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());

    // A view that writes its model and the cultures it was written in.
    private sealed class Recording : IView
    {
        public bool Fails { get; init; }

        public string Path => "recording";

        public async Task RenderAsync(ViewContext context)
        {
            if (Fails)
            {
                throw new InvalidOperationException("The view failed.");
            }

            await context.Writer.WriteAsync($"{context.ViewData.Model} in {CultureInfo.CurrentCulture.Name}/{CultureInfo.CurrentUICulture.Name}");
        }
    }
}
