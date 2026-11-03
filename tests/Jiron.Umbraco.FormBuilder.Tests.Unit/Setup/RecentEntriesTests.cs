using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Setup;

/// <summary>What the Overview does with an entry it cannot read (ADR-0165): the entry arrived, so it stays in the list, named by its number.</summary>
public sealed class RecentEntriesTests
{
    private static readonly AccessUser Someone = new(Guid.NewGuid(), new HashSet<Guid>(), true, true);

    [Test]
    public async Task AnEntryThatCannotBeRead_KeepsItsNumber_AndLeavesTheOthersStanding()
    {
        var access = Substitute.For<IFormBuilderAccessService>();
        access.GetFormVisibilityAsync(Someone, FormBuilderPermissions.ViewEntries, Arg.Any<CancellationToken>()).Returns(FormVisibility.Everything);
        var form = SetupTestForms.Form();
        var forms = Substitute.For<IFormRepository>();
        forms.GetByIdAsync(form.Id, Arg.Any<CancellationToken>()).Returns(form);
        forms.GetVersionByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<StoredFormVersion?>(_ => throw new InvalidOperationException("broken row"));
        var entries = Substitute.For<IEntryRepository>();
        entries.ListRecentAsync(FormVisibility.Everything, RecentEntries.Count, Arg.Any<CancellationToken>()).Returns(
        [
            EntryRepository.FromDto(new EntryDto { Key = Guid.NewGuid(), FormId = form.Id, FormVersionId = 7, Number = 42, State = "submitted", SubmissionId = Guid.NewGuid(), SubmissionHash = "a", Culture = "en", SubmittedUtc = DateTime.UnixEpoch, UpdatedUtc = DateTime.UnixEpoch }),
        ]);

        var recent = await new RecentEntries(access, entries, forms, null!, null!, null!, NullLogger<RecentEntries>.Instance).ListAsync(Someone);

        Assert.Multiple(() =>
        {
            Assert.That(recent.Single().Number, Is.EqualTo(42));
            Assert.That(recent.Single().Title, Is.Null);
            Assert.That(recent.Single().FormName, Is.EqualTo(form.Name));
        });
    }
}
