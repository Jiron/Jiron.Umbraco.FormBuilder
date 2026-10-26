using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.ManagementApi;

/// <summary>
/// The models of the field types' endpoint (ADR-0057): each type with its availability in the registry's order, names in
/// camel case as definition documents write them, and the groups of the palette and the inspector in their order.
/// </summary>
public sealed class FieldTypeModelsTests
{
    private static readonly FieldTypeRegistry Registry = TestFieldTypes.Registry;

    private static readonly FieldSettingLocks NoLocks = new(Options.Create(new FormBuilderOptions()), Registry);

    [Test]
    public void EachType_KeepsItsAvailability_AndTheCountsMustAgree()
    {
        var availability = Registry.Types.Select((type, i) => i == 1 ? FieldTypeAvailability.NotConfigured("#acme_noKeys") : FieldTypeAvailability.Available).ToList();

        var model = FieldTypesResponseModel.Of(Registry.Types, availability, NoLocks);

        Assert.Multiple(() =>
        {
            Assert.That(model.Types.Select(type => type.Alias), Is.EqualTo(Registry.Types.Select(type => type.Declaration.Alias)));
            Assert.That(model.Types[1].Availability, Is.EqualTo(new FieldTypeAvailabilityResponseModel(false, "#acme_noKeys")));
            Assert.That(model.Types[0].Availability, Is.EqualTo(new FieldTypeAvailabilityResponseModel(true, null)));
            Assert.Throws<ArgumentException>(() => FieldTypesResponseModel.Of(Registry.Types, availability.Take(3).ToList(), NoLocks), "an availability for every type");
        });
    }

    [Test]
    public void TheGroups_AreEveryValueOfTheirEnums_InOrder_EachWithItsKey()
    {
        var model = FieldTypesResponseModel.Of([], [], NoLocks);

        Assert.Multiple(() =>
        {
            Assert.That(model.PaletteGroups.Select(group => group.Alias), Is.EqualTo(Enum.GetNames<FieldPaletteGroup>().Select(Camel)));
            Assert.That(model.PaletteGroups.Select(group => group.Name), Is.EqualTo(model.PaletteGroups.Select(group => $"#jfbFieldPalette_{group.Alias}")));
            Assert.That(model.SettingGroups.Select(group => group.Alias), Is.EqualTo(new[] { "essentials", "validation", "appearance", "advanced" }));
            Assert.That(model.SettingGroups.Select(group => group.Name), Is.EqualTo(model.SettingGroups.Select(group => $"#jfbFieldSettingGroups_{group.Alias}")));
        });
    }

    [Test]
    public void EveryCommonProperty_AndEveryErrorKind_HasItsName()
    {
        var everything = Enum.GetValues<FieldFeatures>().Aggregate(FieldFeatures.None, (all, feature) => all | feature);

        Assert.Multiple(() =>
        {
            Assert.That(FieldTypeNames.OfFeatures(everything), Is.EqualTo(Enum.GetNames<FieldFeatures>().Skip(1).Select(Camel)), "every flag but None, in the enum's order");
            Assert.That(FieldTypeNames.OfFeatures(FieldFeatures.None), Is.Empty);
            Assert.That(FieldTypeNames.OfFeatures(FieldFeatures.Label | FieldFeatures.Places), Is.EqualTo(new[] { "label", "places" }));
            foreach (var type in Registry.Types)
            {
                var messages = FieldTypeResponseModel.Of(type, FieldTypeAvailability.Available, []).Messages;
                Assert.That(messages, Is.EqualTo(FieldMessageProperties.All.Where(message => type.Declaration.Messages.HasFlag(message.Kind)).Select(message => message.Name)), type.Declaration.Alias);
            }
        });
    }

    [Test]
    public void ASetting_CarriesItsKindChoicesNestedSettingsDefaultAndRules()
    {
        var email = FieldTypeResponseModel.Of(Registry.FindType("email")!, FieldTypeAvailability.Available, []);
        var domains = email.Settings.Single(setting => setting.Name == "domains");
        var mode = domains.Settings.Single(setting => setting.Name == "mode");
        var list = domains.Settings.Single(setting => setting.Name == "list");

        Assert.Multiple(() =>
        {
            Assert.That((domains.Kind, domains.Group, domains.IsPublic, domains.Default), Is.EqualTo(("nested", "validation", false, (System.Text.Json.JsonElement?)null)));
            Assert.That(mode.Choices, Is.EqualTo(new[]
            {
                new FieldSettingChoiceResponseModel("any", "#jfbFieldChoices_emailDomainModeAny"),
                new FieldSettingChoiceResponseModel("allow", "#jfbFieldChoices_emailDomainModeAllow"),
                new FieldSettingChoiceResponseModel("block", "#jfbFieldChoices_emailDomainModeBlock"),
            }));
            Assert.That(mode.Default?.GetString(), Is.EqualTo("any"));
            Assert.That((list.Kind, list.ItemKind, list.Rules.MaximumLength, list.Rules.MaximumCount), Is.EqualTo(("list", "text", 253, FieldSettingRules.ListCount)));
            Assert.That(email.RuleKind, Is.EqualTo("text"));
            Assert.That(FieldTypeResponseModel.Of(Registry.FindType("captcha")!, FieldTypeAvailability.Available, []).RuleKind, Is.Null);
        });
    }

    [Test]
    public void ALockedSetting_CarriesItsLock_ReadOnlyWithItsValue_OrHiddenWithout()
    {
        var options = new FormBuilderOptions();
        options.Designer.SettingLocks.Add(new DesignerSettingLock { Kind = "field", Type = "text", Setting = "maxLength", Value = "100" });
        options.Designer.SettingLocks.Add(new DesignerSettingLock { Kind = "field", Type = "text", Setting = "prefix", Value = "EUR", Hidden = true });
        options.Designer.SettingLocks.Add(new DesignerSettingLock { Kind = "field", Type = "text", Setting = "counter" });
        options.Designer.SettingLocks.Add(new DesignerSettingLock { Kind = "field", Type = "email", Setting = "domains", Value = """{"mode":"block","list":["example.com"]}""" });
        var locks = new FieldSettingLocks(Options.Create(options), Registry);

        var model = FieldTypesResponseModel.Of(Registry.Types, [.. Registry.Types.Select(_ => FieldTypeAvailability.Available)], locks);
        var text = model.Types.Single(type => type.Alias == "text").Settings.ToDictionary(setting => setting.Name, setting => setting.Lock);
        var domains = model.Types.Single(type => type.Alias == "email").Settings.Single(setting => setting.Name == "domains");

        Assert.Multiple(() =>
        {
            Assert.That(text["maxLength"]! with { Value = null }, Is.EqualTo(new FieldSettingLockResponseModel(false, "JironFormBuilder:Designer:SettingLocks:0", null)));
            Assert.That(text["maxLength"]!.Value?.GetRawText(), Is.EqualTo("100"), "the value every field has");
            Assert.That(text["prefix"], Is.EqualTo(new FieldSettingLockResponseModel(true, "JironFormBuilder:Designer:SettingLocks:1", null)), "a hidden lock keeps its value on the server");
            Assert.That(text["counter"], Is.EqualTo(new FieldSettingLockResponseModel(false, "JironFormBuilder:Designer:SettingLocks:2", null)), "a lock that keeps the default");
            Assert.That((text["minLength"], text["suffix"]), Is.EqualTo(((FieldSettingLockResponseModel?)null, (FieldSettingLockResponseModel?)null)));
            Assert.That(domains.Lock?.Value?.GetRawText(), Is.EqualTo("""{"list":["example.com"],"mode":"block"}"""), "an object, as settings store it");
            Assert.That(domains.Settings.Select(setting => setting.Lock), Is.All.Null, "a lock fixes the whole object");
            Assert.That(model.Types.Where(type => type.Alias is not ("text" or "email")).SelectMany(type => type.Settings).Select(setting => setting.Lock), Is.All.Null);
            Assert.That(() => FieldTypesResponseModel.Of(Registry.Types, [], null!), Throws.ArgumentNullException);
            Assert.That(() => FieldTypeResponseModel.Of(Registry.FindType("text")!, FieldTypeAvailability.Available, null!), Throws.ArgumentNullException);
        });
    }

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
