using System.Text;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Picker;

/// <summary>
/// The form picker's markers in the Content Delivery API's answers (ADR-0127): found by their exact bytes in the item,
/// items and query answers, each with the page it is on (the nearest item with a route, never a block's element), and
/// nothing that only looks like one; a marker's theme and page after a send from the picker's settings (ADR-0129), in
/// their places after the first two; an answer without a marker, and any other path, pass byte for byte.
/// </summary>
public sealed class FormBuilderDeliveryApiMiddlewareTests
{
    private const string Form = "6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084";
    private const string Page = "7a2d4b63-42a5-4f68-8e3f-72c1a7b2d195";
    private const string Other = "8b3e5c74-53b6-4a79-9f40-83d2b8c3e2a6";
    private const string Element = "9c4f6d85-64c7-4b8a-8a51-94e3c9d4f3b7";

    private static string Marker(bool expand) => $$$"""{"jfbForm":"{{{Form}}}","expand":{{{(expand ? "true" : "false")}}}}""";

    private static string Item(string id, string form) =>
        "{\"contentType\":\"jfbPickerPage\",\"name\":\"Picks\",\"route\":{\"path\":\"/picks/\",\"startItem\":{\"id\":\"" + Element + "\",\"path\":\"home\"}},"
        + "\"id\":\"" + id + "\",\"properties\":{\"form\":" + form + ",\"blocks\":{\"items\":[{\"content\":{\"id\":\"" + Element + "\","
        + "\"contentType\":\"formBlock\",\"properties\":{\"form\":" + form + "}}}]}},\"cultures\":{}}";

    [Test]
    public void AnItemsMarkers_AreFound_ByTheirBytes_EachOnThePageItIsOn_ABlocksToo()
    {
        var json = Item(Page, Marker(expand: true));
        var found = FormBuilderDeliveryApiMiddleware.Find(Encoding.UTF8.GetBytes(json));

        Assert.Multiple(() =>
        {
            Assert.That(found, Has.Count.EqualTo(2));
            Assert.That(found.Select(marker => json[marker.Start..marker.End]), Is.All.EqualTo(Marker(expand: true)), "their exact bytes");
            Assert.That(found.Select(marker => (marker.Form, marker.Expand, marker.Page)), Is.All.EqualTo((Guid.Parse(Form), true, (Guid?)Guid.Parse(Page))), "a block's element has no route, so its marker is on the page");
        });
    }

    [Test]
    public void TheItemsAndQueryAnswers_HoldEachItemsMarkers_OnTheirOwnPages()
    {
        var json = $$$"""{"total":2,"items":[{{{Item(Page, Marker(expand: false))}}},{{{Item(Other, "null")}}},{{{Item(Other, Marker(expand: false))}}}]}""";
        var found = FormBuilderDeliveryApiMiddleware.Find(Encoding.UTF8.GetBytes(json));

        Assert.That(found.Select(marker => marker.Page), Is.EqualTo(new Guid?[] { Guid.Parse(Page), Guid.Parse(Page), Guid.Parse(Other), Guid.Parse(Other) }));
    }

    [TestCase($$$"""{"jfbForm":"{{{Form}}}","expand":true,"more":1}""")]
    [TestCase($$$"""{"jfbForm":"{{{Form}}}","expand":true,"theme":"bootstrap5","redirect":null,"more":1}""")]
    [TestCase($$$"""{"jfbForm":"{{{Form}}}","expand":true,"redirect":null,"theme":"bootstrap5"}""")]
    [TestCase($$$"""{"jfbForm":"{{{Form}}}","expand":true,"theme":1}""")]
    [TestCase($$$"""{"expand":true,"jfbForm":"{{{Form}}}"}""")]
    [TestCase("""{"jfbForm":"not a key","expand":true}""")]
    [TestCase($$$"""{"jfbForm":"{{{Form}}}","expand":"true"}""")]
    [TestCase($$$"""{"jfbForm":"{{{Form}}}"}""")]
    [TestCase($$$"""{"text":"{\"jfbForm\":\"{{{Form}}}\",\"expand\":true}"}""")]
    [TestCase($$$"""["jfbForm","{{{Form}}}","expand",true]""")]
    public void WhatOnlyLooksLikeAMarker_IsNone(string json) =>
        Assert.That(FormBuilderDeliveryApiMiddleware.Find(Encoding.UTF8.GetBytes(json)), Is.Empty);

    [TestCase($$$"""{"jfbForm":"{{{Form}}}","expand":false,"theme":"bootstrap5","redirect":"{{{Other}}}"}""", "bootstrap5", Other)]
    [TestCase($$$"""{"jfbForm":"{{{Form}}}","expand":false,"theme":null,"redirect":null}""", null, null)]
    [TestCase($$$"""{"jfbForm":"{{{Form}}}","expand":false,"theme":"unstyled"}""", "unstyled", null)]
    [TestCase($$$"""{"jfbForm":"{{{Form}}}","expand":false,"redirect":"{{{Other}}}"}""", null, Other)]
    public void AMarkersThemeAndPage_AreFoundWithIt(string marker, string? theme, string? redirect)
    {
        var found = FormBuilderDeliveryApiMiddleware.Find(Encoding.UTF8.GetBytes(Item(Page, marker)));

        Assert.Multiple(() =>
        {
            Assert.That(found, Has.Count.EqualTo(2));
            Assert.That(found.Select(each => (each.Theme, each.Redirect)), Is.All.EqualTo((theme, redirect is null ? (Guid?)null : Guid.Parse(redirect))));
        });
    }

    [Test]
    public void AMultiplePickersMarkers_AreEachFound_InOrder()
    {
        var json = $$$"""{"route":{"path":"/p/"},"id":"{{{Page}}}","properties":{"forms":[{"jfbForm":"{{{Other}}}","expand":false,"theme":null,"redirect":null},{"jfbForm":"{{{Form}}}","expand":false,"theme":null,"redirect":null}]}}""";

        Assert.That(FormBuilderDeliveryApiMiddleware.Find(Encoding.UTF8.GetBytes(json)).Select(each => each.Form), Is.EqualTo(new[] { Guid.Parse(Other), Guid.Parse(Form) }));
    }

    [TestCase("/umbraco/delivery/api/v2/content/item/x", "application/json; charset=utf-8", """{"name":"No marker","properties":{"form":null}}""")]
    [TestCase("/umbraco/delivery/api/v2/content", "text/plain", "jfbForm in plain text")]
    [TestCase("/somewhere/else", "application/json", """{"jfbForm":"6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084","expand":true}""")]
    public async Task AnAnswerWithoutAMarkerToResolve_PassesByteForByte(string path, string contentType, string body)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        using var sink = new MemoryStream();
        context.Response.Body = sink;
        var bytes = Encoding.UTF8.GetBytes(body);

        await new FormBuilderDeliveryApiMiddleware().InvokeAsync(context, async http =>
        {
            http.Response.ContentType = contentType;
            await http.Response.Body.WriteAsync(bytes);
        });

        Assert.That(sink.ToArray(), Is.EqualTo(bytes));
    }

    [Test]
    public async Task AnAnswerOutsideASCII_WithoutAMarker_PassesByteForByte()
    {
        var name = string.Concat("Caf", (char)0xE9, " ", (char)0x2013, " ", char.ConvertFromUtf32(0x1F4D6));
        var bytes = Encoding.UTF8.GetBytes("{\"name\":\"" + name + "\",\"properties\":{\"form\":null}}");
        var context = new DefaultHttpContext();
        context.Request.Path = "/umbraco/delivery/api/v2/content/item/x";
        using var sink = new MemoryStream();
        context.Response.Body = sink;

        await new FormBuilderDeliveryApiMiddleware().InvokeAsync(context, async http =>
        {
            http.Response.ContentType = "application/json";
            await http.Response.Body.WriteAsync(bytes);
        });

        Assert.That(sink.ToArray(), Is.EqualTo(bytes));
    }
}
