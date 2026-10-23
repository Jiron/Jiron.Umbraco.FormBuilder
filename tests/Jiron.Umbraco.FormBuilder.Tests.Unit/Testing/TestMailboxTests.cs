using Jiron.Umbraco.FormBuilder.Testing.Mail;
using MimeKit;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>The mail pickup reader, on .eml files written the way Umbraco writes them (MimeKit).</summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class TestMailboxTests
{
    // Non-ASCII text in mails, built from code points: code files stay ASCII (check-text).
    private static readonly string Name = "Z" + char.ConvertFromUtf32(0xE4) + "hnler";
    private static readonly string Greeting = "Gr" + char.ConvertFromUtf32(0xFC) + "ezi";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jfb-mail-" + Guid.NewGuid().ToString("N"));

    [SetUp]
    public void SetUp() => Directory.CreateDirectory(_folder);

    [TearDown]
    public void TearDown() => Directory.Delete(_folder, recursive: true);

    [Test]
    public async Task ReadAllAsync_AMail_DecodesItsAddressesSubjectAndBodies()
    {
        await WriteAsync("one.eml", "Thank you, " + Name, html: "<p>" + Greeting + "</p>", text: Greeting);

        var mail = (await new TestMailbox(_folder).ReadAllAsync()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(mail.Subject, Is.EqualTo("Thank you, " + Name));
            Assert.That(mail.From, Is.EqualTo(new[] { "library@lindenhall.example" }));
            Assert.That(mail.To, Is.EqualTo(new[] { "visitor@lindenhall.example" }));
            Assert.That(mail.Cc, Is.EqualTo(new[] { "desk@lindenhall.example" }));
            Assert.That(mail.ReplyTo, Is.EqualTo(new[] { "help@lindenhall.example" }));
            Assert.That(mail.Html, Does.Contain("<p>" + Greeting + "</p>"));
            Assert.That(mail.Text, Does.Contain(Greeting));
            Assert.That(mail.Date, Is.EqualTo(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero)));
            Assert.That(mail.Headers.Select(header => header.Key), Does.Contain("X-Jfb-Test"));
            Assert.That(mail.File, Does.EndWith("one.eml"));
        });
    }

    [Test]
    public async Task ReadAllAsync_SeveralMails_AreOldestFirst()
    {
        await WriteAsync("b.eml", "first");
        File.SetLastWriteTimeUtc(Path.Combine(_folder, "b.eml"), DateTime.UtcNow.AddMinutes(-2));
        await WriteAsync("a.eml", "second");

        var subjects = (await new TestMailbox(_folder).ReadAllAsync()).Select(mail => mail.Subject);

        Assert.That(subjects, Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public async Task ReadAllAsync_MissingFolder_IsEmpty()
    {
        var mails = await new TestMailbox(Path.Combine(_folder, "none")).ReadAllAsync();

        Assert.That(mails, Is.Empty);
    }

    [Test]
    public async Task ReadAllAsync_FileStillBeingWritten_WaitsUntilItIsDone()
    {
        await WriteAsync("busy.eml", "busy");
        var path = Path.Combine(_folder, "busy.eml");
        var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var reading = new TestMailbox(_folder).ReadAllAsync();
        var finishedWhileLocked = reading.IsCompleted;
        await Task.Delay(300);
        await writer.DisposeAsync();
        var mails = await reading;

        Assert.Multiple(() =>
        {
            Assert.That(finishedWhileLocked, Is.False);
            Assert.That(mails.Single().Subject, Is.EqualTo("busy"));
        });
    }

    [Test]
    public async Task WaitForAsync_AMailThatArrivesLater_IsFound()
    {
        await WriteAsync("other.eml", "Something else");
        var mailbox = new TestMailbox(_folder);

        var waiting = mailbox.WaitForAsync(mail => mail.Subject.StartsWith("Your entry", StringComparison.Ordinal), TimeSpan.FromSeconds(10));
        await WriteAsync("entry.eml", "Your entry E-7");
        var found = await waiting;

        Assert.That(found.Subject, Is.EqualTo("Your entry E-7"));
    }

    [Test]
    public async Task WaitForAsync_NoMatch_TimesOutAndSaysHowManyMailsThereWere()
    {
        await WriteAsync("other.eml", "Something else");
        var mailbox = new TestMailbox(_folder);

        var exception = Assert.ThrowsAsync<TimeoutException>(() =>
            mailbox.WaitForAsync(mail => mail.Subject == "never", TimeSpan.FromMilliseconds(300)));

        Assert.That(exception!.Message, Does.Contain("1 mails there"));
    }

    [Test]
    public async Task Clear_DeletesEveryMail()
    {
        await WriteAsync("one.eml", "one");
        var mailbox = new TestMailbox(_folder);

        mailbox.Clear();

        Assert.That(await mailbox.ReadAllAsync(), Is.Empty);
    }

    private async Task WriteAsync(string name, string subject, string html = "<p>Hello</p>", string text = "Hello")
    {
        var message = new MimeMessage
        {
            Subject = subject,
            Date = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero),
            Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody(),
        };
        message.From.Add(MailboxAddress.Parse("library@lindenhall.example"));
        message.To.Add(MailboxAddress.Parse("visitor@lindenhall.example"));
        message.Cc.Add(MailboxAddress.Parse("desk@lindenhall.example"));
        message.ReplyTo.Add(MailboxAddress.Parse("help@lindenhall.example"));
        message.Headers.Add("X-Jfb-Test", "yes");
        await message.WriteToAsync(Path.Combine(_folder, name));
    }
}
