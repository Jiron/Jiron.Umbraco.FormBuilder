using System.Data;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Infrastructure.Scoping;
using IScopeProvider = Umbraco.Cms.Infrastructure.Scoping.IScopeProvider;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The entry repositories refuse what their columns cannot hold, or would hold with another meaning, before they reach
/// the database (ADR-0036, ADR-0038): an entry's hashes and ids, its page without a query, its context as a JSON object,
/// one typed column per value in UTC, files and events in the shape the later steps rely on.
/// </summary>
[TestFixture]
public sealed class EntryRepositoryArgumentTests
{
    private static readonly string Hash = new('a', 64);
    private IScopeProvider _scopeProvider = null!;
    private IFormBuilderDatabaseGate _gate = null!;
    private FormBuilderDatabase _database = null!;

    [SetUp]
    public void SetUp()
    {
        _scopeProvider = Substitute.For<IScopeProvider>();
        _gate = Substitute.For<IFormBuilderDatabaseGate>();
        _database = new FormBuilderDatabase(_scopeProvider, Substitute.For<IScopeAccessor>(), _gate, TimeProvider.System, NullLogger<FormBuilderDatabase>.Instance);
    }

    // Every unit of work passes the gate first, and the substitute gate never runs one: a call that reached it would have
    // reached the database.
    [TearDown]
    public void NothingReachedTheDatabase()
    {
        Assert.That(_gate.ReceivedCalls(), Is.Empty, "a unit of work was started");
        _scopeProvider.DidNotReceiveWithAnyArgs().CreateScope(Arg.Any<IsolationLevel>(), Arg.Any<RepositoryCacheMode>(), Arg.Any<IEventDispatcher?>(), Arg.Any<IScopedNotificationPublisher?>(), Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

    [Test]
    public void AnEntry_IsRefused_WhenAColumnCannotHoldIt()
    {
        var repository = new EntryRepository(_database);

        Assert.Multiple(() =>
        {
            Refused(repository, Entry() with { State = (EntryState)99 }, "state");
            Refused(repository, Entry() with { SubmissionHash = Hash.ToUpperInvariant() }, "submission hash in uppercase");
            Refused(repository, Entry() with { SubmissionHash = Hash[1..] }, "submission hash of 63");
            Refused(repository, Entry() with { SubmissionHash = null! }, "no submission hash");
            Refused(repository, Entry() with { Culture = new string('c', EntryRepository.MaxCultureLength + 1) }, "culture");
            Refused(repository, Entry() with { Culture = null! }, "no culture");
            Refused(repository, Entry() with { PageUrl = string.Empty }, "empty page");
            Refused(repository, Entry() with { PageUrl = "/" + new string('p', EntryRepository.MaxPageUrlLength) }, "long page");
            Refused(repository, Entry() with { PageUrl = "/contact?email=anna@jfb.test" }, "a query");
            Refused(repository, Entry() with { PageUrl = "/contact#form" }, "a fragment");
            Refused(repository, Entry() with { PageUrl = "contact" }, "not a path from the root");
            Refused(repository, Entry() with { PageUrl = "//jfb.test/contact" }, "another host");
            Refused(repository, Entry() with { PageUrl = "/" + (char)92 + "jfb.test/contact" }, "another host, as browsers read it");
            Refused(repository, Entry() with { PageUrl = "https://jfb.test/contact" }, "a whole address");
            Refused(repository, Entry() with { IpAddress = string.Empty }, "empty address");
            Refused(repository, Entry() with { IpAddress = new string('1', EntryRepository.MaxIpAddressLength + 1) }, "long address");
            Refused(repository, Entry() with { SpamScore = -1 }, "spam score");
            Refused(repository, Entry() with { SpamReasons = new string('r', EntryRepository.MaxSpamReasonsLength + 1) }, "spam reasons");
            Refused(repository, Entry() with { SpamReasons = null! }, "no spam reasons");
            Refused(repository, Entry() with { SearchText = null! }, "no search text");
            Refused(repository, Entry() with { Context = "[]" }, "context as an array");
            Refused(repository, Entry() with { Context = "{" }, "context that is not JSON");
            Refused(repository, Entry() with { Context = string.Empty }, "empty context");
            Refused(repository, Entry() with { DurationSeconds = -1 }, "duration");
            Refused(repository, Entry() with { ResumeTokenHash = Hash[1..] }, "resume token hash");
            Refused(repository, Entry() with { ChallengeId = Hash }, "a challenge id of 64");
            Refused(repository, Entry() with { ChallengeId = new string('A', 32) }, "a challenge id in uppercase");
            Refused(repository, Entry() with { FileIds = [3, 3] }, "a file twice");
            Refused(repository, Entry() with { FileIds = [0] }, "file id 0");
            Refused(repository, Entry() with { FileIds = null! }, "no files");
            Refused(repository, Entry() with { Values = null! }, "no values");
            Assert.That(() => repository.CreateAsync(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void AValue_IsRefused_WhenItsColumnsWouldNotMeanWhatItSays()
    {
        var repository = new EntryRepository(_database);
        var local = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Local);
        var unspecified = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Unspecified);

        Assert.Multiple(() =>
        {
            Refused(repository, Entry(new NewEntryValue(0, "a")), "field id 0");
            Refused(repository, Entry(new NewEntryValue(1, "a", RowIndex: -1)), "a negative row");
            Refused(repository, Entry(new NewEntryValue(1, "a", ValueIndex: -1)), "a negative item");
            Refused(repository, Entry(new NewEntryValue(1, "1", Number: 1, Bool: true)), "two typed columns");
            Refused(repository, Entry(new NewEntryValue(1, null)), "nothing at all");
            Refused(repository, Entry(new NewEntryValue(1, "NaN", Number: double.NaN)), "not a number");
            Refused(repository, Entry(new NewEntryValue(1, "huge", Number: double.PositiveInfinity)), "infinite");
            Refused(repository, Entry(new NewEntryValue(1, "noon", Date: local)), "a local date");
            Refused(repository, Entry(new NewEntryValue(1, "noon", Date: unspecified)), "a date of no kind");
            Refused(repository, Entry(new NewEntryValue(1, "cv.pdf", FileId: 0)) with { FileIds = [] }, "file id 0");
            Refused(repository, Entry(new NewEntryValue(1, "cv.pdf", FileId: 7)) with { FileIds = [8] }, "a file the entry does not take");
            Refused(repository, Entry(new NewEntryValue(1, "a"), new NewEntryValue(1, "b")), "two values in one place");
            Refused(repository, Entry(new NewEntryValue[] { null! }), "a null value");
        });
    }

    [Test]
    public void AReplace_IsRefused_ForValuesOutsideItsFields()
    {
        var repository = new EntryRepository(_database);

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.ReplaceValuesAsync(1, [0], [], string.Empty), Throws.ArgumentException, "field id 0");
            Assert.That(() => repository.ReplaceValuesAsync(1, [1], [new NewEntryValue(2, "b")], string.Empty), Throws.ArgumentException, "a value of another field");
            Assert.That(() => repository.ReplaceValuesAsync(1, [1], [new NewEntryValue(1, "a"), new NewEntryValue(1, "b")], string.Empty), Throws.ArgumentException, "two values in one place");
            Assert.That(() => repository.ReplaceValuesAsync(1, [1], [new NewEntryValue(1, null)], string.Empty), Throws.ArgumentException, "nothing at all");
            Assert.That(() => repository.ReplaceValuesAsync(1, [1], [], null!), Throws.ArgumentNullException);
            Assert.That(() => repository.ReplaceValuesAsync(1, null!, [], string.Empty), Throws.ArgumentNullException);
            Assert.That(() => repository.ReplaceValuesAsync(1, [1], null!, string.Empty), Throws.ArgumentNullException);
            Assert.That(() => repository.ListValuesAsync(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void AStateChange_IsRefused_ForAStateThatIsNone()
    {
        var repository = new EntryRepository(_database);

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.ChangeStateAsync(1, (EntryState)99, EntryState.Approved, null), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => repository.ChangeStateAsync(1, EntryState.Submitted, (EntryState)(-1), null), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void AFile_IsRefused_WhenAColumnCannotHoldIt()
    {
        var repository = new EntryRepository(_database);
        var file = new NewEntryFile(Guid.NewGuid(), 1, Guid.NewGuid(), "cv.pdf", new string('b', 32), "application/pdf", 1, Hash, "Pending");

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.AddFileAsync(file with { OriginalName = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.AddFileAsync(file with { OriginalName = new string('n', EntryRepository.MaxOriginalNameLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.AddFileAsync(file with { StoredName = "cv.pdf" }), Throws.ArgumentException, "never derived from the name");
            Assert.That(() => repository.AddFileAsync(file with { StoredName = new string('B', 32) }), Throws.ArgumentException);
            Assert.That(() => repository.AddFileAsync(file with { ContentType = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.AddFileAsync(file with { ContentType = new string('t', EntryRepository.MaxContentTypeLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.AddFileAsync(file with { SizeBytes = -1 }), Throws.ArgumentException);
            Assert.That(() => repository.AddFileAsync(file with { FileHash = Hash[1..] }), Throws.ArgumentException);
            Assert.That(() => repository.AddFileAsync(file with { ScanState = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.AddFileAsync(file with { ScanState = new string('S', EntryRepository.MaxScanStateLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.AddFileAsync(file with { ScanState = "Pending1" }), Throws.ArgumentException);
            Assert.That(() => repository.AddFileAsync(null!), Throws.ArgumentNullException);
            Assert.That(() => repository.ChangeScanStateAsync(1, "Pending", "In progress"), Throws.ArgumentException);
            Assert.That(() => repository.ChangeScanStateAsync(1, string.Empty, "Clean"), Throws.ArgumentException);
            Assert.That(() => repository.ListOrphanFilesAsync(DateTimeOffset.UnixEpoch, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => repository.ListOrphanFilesAsync(DateTimeOffset.UnixEpoch, EntryRepository.MaxOrphansPerList + 1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void AConsentWording_IsRefused_WhenItsHashCouldNotNameItAlone()
    {
        var repository = new EntryRepository(_database);

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.AddConsentTextAsync(new string('c', ConsentTextHash.MaxCultureLength + 1), "JFB_TEST"), Throws.ArgumentException);
            Assert.That(() => repository.AddConsentTextAsync("en", string.Empty), Throws.ArgumentException);
            Assert.That(() => repository.AddConsentTextAsync("en", "a" + (char)0xDC00), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => repository.GetConsentTextAsync(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void AnEvent_IsRefused_WhenItsKindOrDetailsAreNotInShape()
    {
        var repository = new EntryEventRepository(_database);

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.AddAsync(new NewEntryEvent(1, string.Empty)), Throws.ArgumentException);
            Assert.That(() => repository.AddAsync(new NewEntryEvent(1, new string('k', EntryEventRepository.MaxKindLength + 1))), Throws.ArgumentException);
            Assert.That(() => repository.AddAsync(new NewEntryEvent(1, "1st")), Throws.ArgumentException, "starts with a letter");
            Assert.That(() => repository.AddAsync(new NewEntryEvent(1, "state changed")), Throws.ArgumentException);
            Assert.That(() => repository.AddAsync(new NewEntryEvent(1, null!)), Throws.ArgumentException);
            Assert.That(() => repository.AddAsync(new NewEntryEvent(1, "submitted", Details: "[]")), Throws.ArgumentException);
            Assert.That(() => repository.AddAsync(new NewEntryEvent(1, "submitted", Details: "{")), Throws.ArgumentException);
            Assert.That(() => repository.AddAsync(new NewEntryEvent(1, "submitted", Details: null!)), Throws.ArgumentException);
            Assert.That(() => repository.AddAsync(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void ANote_IsRefused_WhenItIsEmptyOrTooLong()
    {
        var repository = new EntryNoteRepository(_database);
        var tooLong = new string('n', EntryNoteRepository.MaxBodyLength + 1);

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.AddAsync(1, Guid.NewGuid(), string.Empty), Throws.ArgumentException);
            Assert.That(() => repository.AddAsync(1, Guid.NewGuid(), " " + (char)9), Throws.ArgumentException, "white space only");
            Assert.That(() => repository.AddAsync(1, Guid.NewGuid(), tooLong), Throws.ArgumentException);
            Assert.That(() => repository.UpdateAsync(1, string.Empty), Throws.ArgumentException);
            Assert.That(() => repository.UpdateAsync(1, tooLong), Throws.ArgumentException);
            Assert.That(() => repository.AddAsync(1, Guid.NewGuid(), null!), Throws.ArgumentNullException);
        });
    }

    private static NewEntry Entry(params NewEntryValue[] values) => new()
    {
        Key = Guid.NewGuid(),
        FormId = 1,
        FormVersionId = 1,
        SubmissionId = Guid.NewGuid(),
        SubmissionHash = Hash,
        Values = values,
    };

    private static void Refused(EntryRepository repository, NewEntry entry, string why) =>
        Assert.That(() => repository.CreateAsync(entry), Throws.InstanceOf<ArgumentException>(), why);
}
