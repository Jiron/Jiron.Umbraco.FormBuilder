using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// Publishing a form protected by a captcha (ADR-0035, ADR-0184): the site must have the provider, and the form's settings
/// for it must be those the provider describes, each within its bounds.
/// </summary>
public sealed class CaptchaPublishCheckTests
{
    private const string Scored = RecaptchaV3Provider.ChosenAs;
    private const string Plain = "plain";

    private static readonly CaptchaPublishCheck Check = new(new FormBuilderCaptchaProviderCollection(() =>
        [new Described(Scored, typeof(RecaptchaV3Settings)), new Described(Plain, null)]));

    [TestCase(Scored, null)]
    [TestCase(Scored, """{"threshold":"0.7"}""")]
    [TestCase(Scored, """{"threshold":"1"}""")]
    [TestCase(Plain, null)]
    public async Task SettingsTheProviderDescribes_WithinTheirBounds_Pass(string provider, string? settings) =>
        Assert.That(await ProblemsAsync(provider, settings), Is.Empty);

    [TestCase("""{"threshold":"2"}""")]
    [TestCase("""{"threshold":"-0.1"}""")]
    [TestCase("""{"threshold":true}""")]
    public async Task ASettingOutOfItsBounds_OrOfAnotherKind_IsRefusedAtItsPath(string settings) =>
        Assert.That(await ProblemsAsync(Scored, settings), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.settings.protection.captcha.settings.threshold") }));

    [TestCase(Scored, """{"score":"0.7"}""", "$.settings.protection.captcha.settings.score")]
    [TestCase(Plain, """{"threshold":"0.7"}""", "$.settings.protection.captcha.settings.threshold")]
    public async Task ASettingTheProviderDoesNotDescribe_IsRefusedAtItsPath(string provider, string settings, string path) =>
        Assert.That(await ProblemsAsync(provider, settings), Is.EqualTo(new[] { (DefinitionErrors.SettingUnknown, path) }));

    [Test]
    public async Task AProviderTheSiteLacks_IsRefusedByName() =>
        Assert.That(await ProblemsAsync("gone", """{"threshold":"0.7"}"""), Is.EqualTo(new[] { (DefinitionErrors.UnknownType, "$.settings.protection.captcha.provider") }));

    [Test]
    public async Task AFormWithoutACaptcha_HasNothingToCheck() =>
        Assert.That(await Check.CheckAsync(new FormDefinition { Key = Guid.NewGuid() }, CancellationToken.None), Is.Empty);

    private static async Task<IEnumerable<(string Code, string Path)>> ProblemsAsync(string provider, string? settings)
    {
        var definition = new FormDefinition
        {
            Key = Guid.NewGuid(),
            Settings = new FormSettings
            {
                Protection = new FormProtectionSettings
                {
                    Level = ProtectionLevel.Captcha,
                    Captcha = new CaptchaChoice { Provider = provider, Settings = settings is null ? null : SettingsObject.Parse(settings) },
                },
            },
        };
        return (await Check.CheckAsync(definition, CancellationToken.None)).Select(problem => (problem.Code, problem.Path));
    }

    private sealed class Described(string name, Type? settings) : IFormBuilderCaptchaProvider
    {
        public string Name => name;

        public Type? SettingsType => settings;

        public bool IsConfigured => true;

        public FormBuilderCaptchaClient? Client => null;

        public Task<FormBuilderCaptchaOutcome> VerifyAsync(FormBuilderCaptchaCheck check, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
