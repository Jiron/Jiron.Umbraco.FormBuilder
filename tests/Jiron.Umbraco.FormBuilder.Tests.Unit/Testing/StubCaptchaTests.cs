using Jiron.Umbraco.FormBuilder.Testing.Stub;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>The stub's captcha verification: the token alone decides the answer (D22).</summary>
[TestFixture]
public sealed class StubCaptchaTests
{
    private const string Form = "application/x-www-form-urlencoded";
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    [TestCase("/captcha/siteverify", true)]
    [TestCase("/captcha/provider-a/siteverify", true)]
    [TestCase("/captcha/siteverify/", true)]
    [TestCase("/captcha", false)]
    [TestCase("/captcha/a/b/siteverify", false)]
    [TestCase("/Captcha/siteverify", false)]
    [TestCase("/webhooks/siteverify", false)]
    public void IsVerifyPath_Paths_AreSortedAsExpected(string path, bool verify)
    {
        Assert.That(StubCaptcha.IsVerifyPath(path), Is.EqualTo(verify));
    }

    [Test]
    public void Verify_PassToken_Succeeds()
    {
        var answer = StubCaptcha.Verify($"secret=JFB_FAKE-s&response={StubCaptcha.Pass}&remoteip=127.0.0.1", Form, Now);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Success, Is.True);
            Assert.That(answer.ChallengeTimestamp, Is.EqualTo("2026-03-01T12:00:00Z"));
            Assert.That(answer.Hostname, Is.EqualTo("localhost"));
            Assert.That(answer.Score, Is.Null);
            Assert.That(answer.ErrorCodes, Is.Empty);
        });
    }

    [TestCase("0.3", 0.3)]
    [TestCase("0", 0.0)]
    [TestCase("1.0", 1.0)]
    public void Verify_ScoreToken_SucceedsWithThatScore(string score, double expected)
    {
        var answer = StubCaptcha.Verify($"secret=JFB_FAKE-s&response={StubCaptcha.ScorePrefix}{score}", Form, Now);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Success, Is.True);
            Assert.That(answer.Score, Is.EqualTo((decimal)expected));
            Assert.That(answer.Action, Is.EqualTo("submit"));
        });
    }

    [TestCase("secret=JFB_FAKE-s&response=JFB_FAKE-captcha-fail", "invalid-input-response")]
    [TestCase("secret=JFB_FAKE-s&response=JFB_FAKE-captcha-expired", "timeout-or-duplicate")]
    [TestCase("secret=JFB_FAKE-s&response=anything-else", "invalid-input-response")]
    [TestCase("secret=JFB_FAKE-s&response=JFB_FAKE-captcha-score-1.5", "invalid-input-response")]
    [TestCase("secret=JFB_FAKE-s&response=JFB_FAKE-captcha-score-x", "invalid-input-response")]
    [TestCase("response=JFB_FAKE-captcha-pass", "missing-input-secret")]
    [TestCase("secret=JFB_FAKE-s", "missing-input-response")]
    [TestCase("", "missing-input-secret")]
    public void Verify_OtherForms_AreRefusedWithTheirCode(string body, string code)
    {
        var answer = StubCaptcha.Verify(body, Form, Now);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Success, Is.False);
            Assert.That(answer.ErrorCodes, Is.EqualTo(new[] { code }));
            Assert.That(answer.ChallengeTimestamp, Is.Null);
        });
    }

    [Test]
    public void Verify_NotAForm_IsRefused()
    {
        var answer = StubCaptcha.Verify($$"""{"secret":"JFB_FAKE-s","response":"{{StubCaptcha.Pass}}"}""", "application/json", Now);

        Assert.That(answer.ErrorCodes, Is.EqualTo(new[] { "missing-input-secret" }));
    }
}
