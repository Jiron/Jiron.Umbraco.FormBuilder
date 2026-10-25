using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using NSubstitute;
using NUnit.Framework.Constraints;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// Where forms and folders go in the library (ADR-0047, ADR-0053), worded once for the form and folder services: into a
/// folder outside the trash or to the root, and from the trash back into the folder they were in while that is outside
/// the trash, otherwise to the root.
/// </summary>
[TestFixture]
public sealed class LibraryFoldersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private static readonly StoredFolder Live = Folder(7, trashed: false);
    private static readonly StoredFolder Trashed = Folder(8, trashed: true);

    [Test]
    public void AFormOrFolderGoes_IntoAFolderOutsideTheTrash_OrToTheRoot_WithoutReadingForTheRoot()
    {
        var folders = Substitute.For<IFolderRepository>();
        folders.GetAsync(Live.Key, Arg.Any<CancellationToken>()).Returns(Live);
        folders.GetAsync(Trashed.Key, Arg.Any<CancellationToken>()).Returns(Trashed);

        Assert.Multiple(async () =>
        {
            Assert.That(await LibraryFolders.LiveAsync(folders, null, CancellationToken.None), Is.Null, "the root");
            Assert.That(folders.ReceivedCalls(), Is.Empty, "nothing read for the root");
            Assert.That(await LibraryFolders.LiveAsync(folders, Live.Key, CancellationToken.None), Is.SameAs(Live));
            Assert.That(() => LibraryFolders.LiveAsync(folders, Trashed.Key, CancellationToken.None), MissingFolder(), "in the trash");
            Assert.That(() => LibraryFolders.LiveAsync(folders, Guid.NewGuid(), CancellationToken.None), MissingFolder(), "missing");
        });
    }

    [Test]
    public void ARestore_LandsInTheFolderItWasIn_WhileThatIsOutsideTheTrash_OtherwiseAtTheRoot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LibraryFolders.Landing(Live), Is.EqualTo(new RestoreLanding(Live.Key)));
            Assert.That(LibraryFolders.Landing(Trashed), Is.EqualTo(new RestoreLanding(null)), "its folder is in the trash");
            Assert.That(LibraryFolders.Landing(null), Is.EqualTo(new RestoreLanding(null)), "it was at the root");
        });
    }

    [TestCase("form")]
    [TestCase("folder")]
    public void TheWrite_LandsWhereTheCallerChecked_AndALandingInAnotherFolderIsRefused(string item)
    {
        Assert.Multiple(() =>
        {
            Assert.That(LibraryFolders.LandingId(new RestoreLanding(null), Live, item), Is.Null, "the root, which the write checks");
            Assert.That(LibraryFolders.LandingId(new RestoreLanding(Live.Key), Live, item), Is.EqualTo(Live.Id));
            Assert.That(() => LibraryFolders.LandingId(new RestoreLanding(Guid.NewGuid()), Live, item), LandingChanged(item), "another folder");
            Assert.That(() => LibraryFolders.LandingId(new RestoreLanding(Live.Key), null, item), LandingChanged(item), "it was at the root");
        });
    }

    private static StoredFolder Folder(int id, bool trashed) =>
        new(id, Guid.NewGuid(), null, "Folder " + id, 0, false, trashed ? Now : null, trashed ? Guid.NewGuid() : null, Now, Now, null, null);

    private static EqualConstraint MissingFolder() =>
        Throws.TypeOf<FormBuilderConflictException>().With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.MissingFolder);

    private static StartsWithConstraint LandingChanged(string item) =>
        Throws.TypeOf<FormBuilderConflictException>()
            .With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.LandingChanged)
            .And.Message.StartsWith($"Where the {item} lands changed");
}
