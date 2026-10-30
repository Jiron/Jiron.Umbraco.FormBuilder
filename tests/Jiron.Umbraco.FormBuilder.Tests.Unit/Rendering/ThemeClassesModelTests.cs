using System.Reflection;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The classes a form's views write (ADR-0015, ADR-0105): ours on every slot that has some, a theme's after them and
/// never in their place, and a slot that has neither none at all.
/// </summary>
[TestFixture]
public sealed class ThemeClassesModelTests
{
    // Each property with our classes alone, null where we have none.
    private static readonly Dictionary<string, string?> Ours = new()
    {
        [nameof(ThemeClassesModel.Page)] = "jfb-page",
        [nameof(ThemeClassesModel.Label)] = "jfb-label",
        [nameof(ThemeClassesModel.PartLabel)] = null,
        [nameof(ThemeClassesModel.Help)] = "jfb-help",
        [nameof(ThemeClassesModel.Error)] = "jfb-error",
        [nameof(ThemeClassesModel.Control)] = "jfb-control",
        [nameof(ThemeClassesModel.Select)] = "jfb-control",
        [nameof(ThemeClassesModel.Choices)] = "jfb-choices",
        [nameof(ThemeClassesModel.Choice)] = "jfb-choice",
        [nameof(ThemeClassesModel.ChoiceInput)] = null,
        [nameof(ThemeClassesModel.ChoiceLabel)] = null,
        [nameof(ThemeClassesModel.SwitchChoice)] = "jfb-choice",
        [nameof(ThemeClassesModel.SwitchInput)] = "jfb-switch",
        [nameof(ThemeClassesModel.Range)] = null,
        [nameof(ThemeClassesModel.Affix)] = "jfb-affix",
        [nameof(ThemeClassesModel.AffixText)] = null,
        [nameof(ThemeClassesModel.Counter)] = "jfb-counter",
        [nameof(ThemeClassesModel.Navigation)] = "jfb-navigation",
        [nameof(ThemeClassesModel.Button)] = "jfb-button",
        [nameof(ThemeClassesModel.ButtonPrimary)] = "jfb-button jfb-button-primary",
        [nameof(ThemeClassesModel.ButtonLink)] = "jfb-button jfb-button-link",
        [nameof(ThemeClassesModel.ButtonIcon)] = "jfb-button jfb-button-icon",
        [nameof(ThemeClassesModel.ProgressBar)] = "jfb-progress jfb-progress-bar",
        [nameof(ThemeClassesModel.ProgressSteps)] = "jfb-progress jfb-progress-steps",
        [nameof(ThemeClassesModel.ErrorSummary)] = "jfb-error-summary",
        [nameof(ThemeClassesModel.Outcome)] = "jfb-outcome",
    };

    [Test]
    public void TheTestNamesEveryProperty() =>
        Assert.That(Properties().Select(property => property.Name), Is.EquivalentTo(Ours.Keys));

    [Test]
    public void WithoutATheme_EachSlotIsOursAlone_AndOneWithoutOursIsNone() =>
        Assert.That(Values(ThemeClassesModel.None), Is.EquivalentTo(Ours));

    [Test]
    public void AnEmptyClassMap_IsNoTheme() =>
        Assert.That(Values(new ThemeClassesModel(new Dictionary<string, string>())), Is.EquivalentTo(Ours));

    [Test]
    public void ATheme_AddsItsClassesAfterOurs_SlotBySlot()
    {
        var every = FormBuilderThemeSlots.All.ToDictionary(slot => slot, slot => "t-" + slot);

        Assert.That(Values(new ThemeClassesModel(every)), Is.EquivalentTo(new Dictionary<string, string?>
        {
            [nameof(ThemeClassesModel.Page)] = "jfb-page t-page",
            [nameof(ThemeClassesModel.Label)] = "jfb-label t-label",
            [nameof(ThemeClassesModel.PartLabel)] = "t-label",
            [nameof(ThemeClassesModel.Help)] = "jfb-help t-help",
            [nameof(ThemeClassesModel.Error)] = "jfb-error t-error",
            [nameof(ThemeClassesModel.Control)] = "jfb-control t-control",
            [nameof(ThemeClassesModel.Select)] = "jfb-control t-select",
            [nameof(ThemeClassesModel.Choices)] = "jfb-choices t-choices",
            [nameof(ThemeClassesModel.Choice)] = "jfb-choice t-choice",
            [nameof(ThemeClassesModel.ChoiceInput)] = "t-choice-input",
            [nameof(ThemeClassesModel.ChoiceLabel)] = "t-choice-label",
            [nameof(ThemeClassesModel.SwitchChoice)] = "jfb-choice t-choice t-switch",
            [nameof(ThemeClassesModel.SwitchInput)] = "jfb-switch t-choice-input",
            [nameof(ThemeClassesModel.Range)] = "t-range",
            [nameof(ThemeClassesModel.Affix)] = "jfb-affix t-affix",
            [nameof(ThemeClassesModel.AffixText)] = "t-affix-text",
            [nameof(ThemeClassesModel.Counter)] = "jfb-counter t-counter",
            [nameof(ThemeClassesModel.Navigation)] = "jfb-navigation t-navigation",
            [nameof(ThemeClassesModel.Button)] = "jfb-button t-button",
            [nameof(ThemeClassesModel.ButtonPrimary)] = "jfb-button jfb-button-primary t-button-primary",
            [nameof(ThemeClassesModel.ButtonLink)] = "jfb-button jfb-button-link t-button-link",
            [nameof(ThemeClassesModel.ButtonIcon)] = "jfb-button jfb-button-icon t-button",
            [nameof(ThemeClassesModel.ProgressBar)] = "jfb-progress jfb-progress-bar t-progress",
            [nameof(ThemeClassesModel.ProgressSteps)] = "jfb-progress jfb-progress-steps t-progress",
            [nameof(ThemeClassesModel.ErrorSummary)] = "jfb-error-summary t-error-summary",
            [nameof(ThemeClassesModel.Outcome)] = "jfb-outcome t-outcome",
        }));
    }

    [Test]
    public void ASlotTheModelsCompose_JoinsTheThemesClasses_AndOneTheThemeLeavesEmpty_IsOursAlone()
    {
        var classes = new ThemeClassesModel(new Dictionary<string, string> { [FormBuilderThemeSlots.Form] = "needs-validation", [FormBuilderThemeSlots.Group] = string.Empty, [FormBuilderThemeSlots.Range] = string.Empty });

        Assert.Multiple(() =>
        {
            Assert.That(classes.Join("jfb-form", FormBuilderThemeSlots.Form), Is.EqualTo("jfb-form needs-validation"));
            Assert.That(classes.Join("jfb-group", FormBuilderThemeSlots.Group), Is.EqualTo("jfb-group"));
            Assert.That(classes.Join("jfb-field", FormBuilderThemeSlots.Field), Is.EqualTo("jfb-field"));
            Assert.That(classes.Range, Is.Null, "an empty value is none");
        });
    }

    [Test]
    public void WithoutAClassMap_TheModelIsRefused() =>
        Assert.That(() => new ThemeClassesModel(null!), Throws.ArgumentNullException);

    private static IEnumerable<PropertyInfo> Properties() =>
        typeof(ThemeClassesModel).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.PropertyType == typeof(string));

    private static Dictionary<string, string?> Values(ThemeClassesModel classes) =>
        Properties().ToDictionary(property => property.Name, property => (string?)property.GetValue(classes));
}
