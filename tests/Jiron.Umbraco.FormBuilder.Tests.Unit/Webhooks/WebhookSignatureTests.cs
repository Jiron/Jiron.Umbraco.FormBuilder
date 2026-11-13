using System.Diagnostics;
using System.Globalization;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using WebhookSignatureSample;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Webhooks;

/// <summary>
/// The webhook's signature and the samples that verify it (ROADMAP 8.4, ADR-0161): each sample reads what the action signs, and
/// refuses what was changed, signed with another secret, too old or written wrong. PHP runs where it is installed, as on CI's
/// runner, which fails the test when it has none; elsewhere the test says it could not run.
/// </summary>
[TestFixture]
public sealed class WebhookSignatureTests
{
    private const string Secret = "JFB_FAKE-sample-secret";
    private const string Body = """{"event":"submitted"}""";

    private static readonly DateTimeOffset Time = DateTimeOffset.FromUnixTimeSeconds(1793620800);

    private static readonly string Samples = FindSamples();

    /// <summary>What each sample is asked: a case's name, the secret, the header, the body, the time of the check, and whether it is genuine.</summary>
    public static IEnumerable<TestCaseData> Cases()
    {
        var good = WebhookSignature.Of(Secret, Time, Encoding.UTF8.GetBytes(Body));
        var seconds = Time.ToUnixTimeSeconds();
        var hex = good[(good.IndexOf("v1=", StringComparison.Ordinal) + 3)..];
        yield return new TestCaseData(Secret, good, Body, seconds, true).SetName("{m} genuine");
        yield return new TestCaseData(Secret, good, Body, seconds + 299, true).SetName("{m} a little later");
        yield return new TestCaseData(Secret, good, Body, seconds - 299, true).SetName("{m} with a clock a little behind");
        yield return new TestCaseData(Secret, good, Body + " ", seconds, false).SetName("{m} another body");
        yield return new TestCaseData("JFB_FAKE-other-secret", good, Body, seconds, false).SetName("{m} another secret");
        yield return new TestCaseData(Secret, good, Body, seconds + 301, false).SetName("{m} too old");
        yield return new TestCaseData(Secret, good, Body, seconds - 301, false).SetName("{m} from the future");
        yield return new TestCaseData(Secret, $"t={seconds + 1}, v1={hex}", Body, seconds, false).SetName("{m} another time");
        yield return new TestCaseData(Secret, $"t={seconds}", Body, seconds, false).SetName("{m} no signature");
        yield return new TestCaseData(Secret, $"v1={hex}", Body, seconds, false).SetName("{m} no time");
        yield return new TestCaseData(Secret, $"t=soon, v1={hex}", Body, seconds, false).SetName("{m} a time that is no number");
        yield return new TestCaseData(Secret, "nonsense", Body, seconds, false).SetName("{m} nonsense");
        yield return new TestCaseData(Secret, $"{good}, t={seconds + 1}", Body, seconds, false).SetName("{m} a time said twice");
        yield return new TestCaseData(Secret, $"{good}, v1={hex}", Body, seconds, false).SetName("{m} a signature said twice");
        yield return new TestCaseData(Secret, $"t={seconds}, v1={hex.ToUpperInvariant()}", Body, seconds, false).SetName("{m} in uppercase");
    }

    [Test]
    public void TheSignature_IsTheHmacOfTheTimeADotAndTheBody_InLowercaseHex()
    {
        // Worked out apart from this code, with Node's crypto module.
        Assert.That(
            WebhookSignature.Of(Secret, Time, Encoding.UTF8.GetBytes(Body)),
            Is.EqualTo("t=1793620800, v1=0c4dd8ef53dd6e6472f0f5167c3d77331ac37d8a0ff6f25bc2833bb60715787a"));
    }

    [TestCaseSource(nameof(Cases))]
    public void TheCSharpSample(string secret, string header, string body, long now, bool genuine)
    {
        Assert.That(SignatureCheck.IsGenuine(secret, header, Encoding.UTF8.GetBytes(body), DateTimeOffset.FromUnixTimeSeconds(now)), Is.EqualTo(genuine));
    }

    [TestCaseSource(nameof(Cases))]
    public async Task TheNodeSample(string secret, string header, string body, long now, bool genuine)
    {
        var code = await RunAsync("node", Path.Combine(Samples, "verify.mjs"), secret, header, body, now);

        Assert.That(code, Is.EqualTo(genuine ? 0 : 1));
    }

    [Test]
    public async Task TheNodeSample_WithoutATime_ChecksTheHeaderAgainstTheClock()
    {
        var fresh = WebhookSignature.Of(Secret, DateTimeOffset.UtcNow, Encoding.UTF8.GetBytes(Body));
        var old = WebhookSignature.Of(Secret, Time, Encoding.UTF8.GetBytes(Body));

        var freshCode = await RunAsync("node", Path.Combine(Samples, "verify.mjs"), Secret, fresh, Body, now: null);
        var oldCode = await RunAsync("node", Path.Combine(Samples, "verify.mjs"), Secret, old, Body, now: null);

        Assert.That((freshCode, oldCode), Is.EqualTo((0, 1)));
    }

    [Test]
    public async Task ThePhpSample_WithoutATime_ChecksTheHeaderAgainstTheClock()
    {
        if (!IsOnThePath("php"))
        {
            if (Environment.GetEnvironmentVariable("CI") is not null)
            {
                Assert.Fail("CI's runner has PHP, so this sample is run there.");
            }

            Assert.Ignore("PHP is not installed here; this sample runs on CI's runner.");
        }

        var fresh = WebhookSignature.Of(Secret, DateTimeOffset.UtcNow, Encoding.UTF8.GetBytes(Body));
        var old = WebhookSignature.Of(Secret, Time, Encoding.UTF8.GetBytes(Body));

        var freshCode = await RunAsync("php", Path.Combine(Samples, "verify.php"), Secret, fresh, Body, now: null);
        var oldCode = await RunAsync("php", Path.Combine(Samples, "verify.php"), Secret, old, Body, now: null);

        Assert.That((freshCode, oldCode), Is.EqualTo((0, 1)));
    }

    [TestCaseSource(nameof(Cases))]
    public async Task ThePhpSample(string secret, string header, string body, long now, bool genuine)
    {
        if (!IsOnThePath("php"))
        {
            if (Environment.GetEnvironmentVariable("CI") is not null)
            {
                Assert.Fail("CI's runner has PHP, so this sample is run there.");
            }

            Assert.Ignore("PHP is not installed here; this sample runs on CI's runner.");
        }

        var code = await RunAsync("php", Path.Combine(Samples, "verify.php"), secret, header, body, now);

        Assert.That(code, Is.EqualTo(genuine ? 0 : 1));
    }

    [Test]
    public void ThePhpSample_SaysWhatTheOthersSay_WhereItCannotRun()
    {
        var sample = File.ReadAllText(Path.Combine(Samples, "verify.php"));

        Assert.Multiple(() =>
        {
            Assert.That(sample, Does.Contain("hash_hmac('sha256', $time . '.' . $body, $secret)"));
            Assert.That(sample, Does.Contain("hash_equals($expected, $signature)"));
            Assert.That(sample, Does.Contain($"JFB_TOLERANCE_SECONDS = {SignatureCheck.ToleranceSeconds}"));
        });
    }

    [Test]
    public void TheSamplesAgreeOnTheTolerance()
    {
        Assert.That(File.ReadAllText(Path.Combine(Samples, "verify.mjs")), Does.Contain($"TOLERANCE_SECONDS = {SignatureCheck.ToleranceSeconds}"));
    }

    private static async Task<int> RunAsync(string program, string script, string secret, string header, string body, long? now)
    {
        var file = Path.Combine(Path.GetTempPath(), "jfb-signature-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(file, body, new UTF8Encoding(false));
        try
        {
            var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { script, secret, header, file })
            {
                start.ArgumentList.Add(argument);
            }

            if (now is { } seconds)
            {
                start.ArgumentList.Add(seconds.ToString(CultureInfo.InvariantCulture));
            }

            using var process = Process.Start(start)!;
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var errors = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            Assert.That(await errors, Is.Empty, await output);
            return process.ExitCode;
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static bool IsOnThePath(string program)
    {
        var endings = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat" } : [string.Empty];
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(folder => endings.Any(ending => File.Exists(Path.Combine(folder, program + ending))));
    }

    private static string FindSamples()
    {
        for (var folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); folder is not null; folder = folder.Parent)
        {
            var samples = Path.Combine(folder.FullName, "docs", "samples", "webhook-signature");
            if (Directory.Exists(samples))
            {
                return samples;
            }
        }

        throw new InvalidOperationException("The samples are in docs/samples/webhook-signature of the repository.");
    }
}
