using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>A Razor view written to text with its model: a mail's body (ADR-0160) and an outcome's preview (ADR-0187).</summary>
internal static class RazorViewText
{
    /// <summary>The text the view writes for the model.</summary>
    /// <typeparam name="TModel">The view's model.</typeparam>
    /// <param name="view">The view the site's view engine found.</param>
    /// <param name="action">The request the view writes for.</param>
    /// <param name="model">The model.</param>
    /// <param name="tempData">The site's temp data, which a view may read.</param>
    /// <param name="culture">The culture the view writes in, the request's own given back after; null to leave the request's.</param>
    public static async Task<string> WriteAsync<TModel>(IView view, ActionContext action, TModel model, ITempDataProvider tempData, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(action);

        // The culture set here stays in this method: an async method's caller gets its own back when the method returns,
        // or throws, as the culture flows with the execution context into a call and never out of it.
        if (culture is not null)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        return await WriteInAsync(view, action, model, tempData);
    }

    private static async Task<string> WriteInAsync<TModel>(IView view, ActionContext action, TModel model, ITempDataProvider tempData)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var data = new ViewDataDictionary<TModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        var temp = new TempDataDictionary(action.HttpContext, tempData);
        await view.RenderAsync(new ViewContext(action, view, data, temp, writer, new HtmlHelperOptions()));
        return writer.ToString();
    }
}
