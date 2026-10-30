using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The marks that let the copy of the answers show once (ADR-0123): the call that adds an entry's mark takes the copy,
/// marks older than a sent token's lifetime go first, and an entry as old as that lifetime has no copy to take.
/// </summary>
public sealed class SentCopiesTests
{
    private static readonly Guid Form = Guid.Parse("f0000000-0000-4000-8000-000000000123");
    private static readonly Guid Submission = Guid.Parse("50000000-0000-4000-8000-000000000123");

    private TestTimeProvider _clock = null!;
    private IKeyValueRepository _keyValues = null!;
    private SentCopies _copies = null!;

    [SetUp]
    public void CreateTheMarks()
    {
        _clock = new TestTimeProvider();
        _keyValues = Substitute.For<IKeyValueRepository>();
        _copies = new SentCopies(_keyValues, _clock);
    }

    [Test]
    public async Task AYoungEntry_TakesItsCopy_WhenItsMarkIsNew_AfterTheOldMarksAreGone()
    {
        _keyValues.TryAddAsync(SentCopies.KeyOf(Form, Submission), null, Arg.Any<CancellationToken>()).Returns(true, false);

        var first = await _copies.TakeAsync(Form, Submission, _clock.GetUtcNow().AddMinutes(-29), CancellationToken.None);

        // The old marks go first, then the entry's is added.
        Received.InOrder(() =>
        {
            _keyValues.DeleteOlderAsync(SentCopies.Prefix, (_clock.GetUtcNow() - SentTokens.Lifetime).UtcDateTime, Arg.Any<CancellationToken>());
            _keyValues.TryAddAsync(SentCopies.KeyOf(Form, Submission), null, Arg.Any<CancellationToken>());
        });

        var second = await _copies.TakeAsync(Form, Submission, _clock.GetUtcNow().AddMinutes(-29), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True, "the first call adds the mark");
            Assert.That(second, Is.False, "a later one finds it there");
            Assert.That(SentCopies.KeyOf(Form, Submission), Is.EqualTo("sent-copy:" + Form.ToString("N") + ":" + Submission.ToString("N")), "the form's key and the submission id, each without hyphens");
            Assert.That(KeyValueRepository.IsValidKey(SentCopies.KeyOf(Form, Submission)), Is.True, "a key the store takes");
        });
    }

    [Test]
    public async Task AnEntryAsOldAsATokensLifetime_TakesNoCopy_AndWritesNothing()
    {
        var taken = await _copies.TakeAsync(Form, Submission, _clock.GetUtcNow() - SentTokens.Lifetime, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(taken, Is.False);
            await _keyValues.DidNotReceiveWithAnyArgs().TryAddAsync(default!, default, default);
            await _keyValues.DidNotReceiveWithAnyArgs().DeleteOlderAsync(default!, default, default);
        });
    }
}
