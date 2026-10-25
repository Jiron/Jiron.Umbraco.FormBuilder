using Jiron.Umbraco.FormBuilder.Core.Persistence;
using NUnit.Framework.Constraints;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The one check of the trash (ADR-0047, ADR-0053), which the services and the repositories share: a change to a form or
/// folder in the trash is refused with <c>trashed</c>, a permanent delete of one outside it with <c>not_trashed</c>.
/// </summary>
[TestFixture]
public sealed class LibraryTrashTests
{
    private static readonly DateTimeOffset Trashed = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [TestCase("form")]
    [TestCase("folder")]
    public void AChangeInTheTrash_IsRefused_WithTrashed(string item)
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => LibraryTrash.CheckOutside(Trashed, item), Refused(FormBuilderConflict.Trashed, $"The {item} is in the trash; restore it first."));
            Assert.That(() => LibraryTrash.CheckOutside(null, item), Throws.Nothing);
            Assert.That(LibraryTrash.InTrash(item), Has.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.Trashed));
        });
    }

    [TestCase("form")]
    [TestCase("folder")]
    public void ADeleteOutsideTheTrash_IsRefused_WithNotTrashed(string item)
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => LibraryTrash.CheckInside(null, item), Refused(FormBuilderConflict.NotTrashed, $"Only a {item} in the trash is deleted; put it in the trash first."));
            Assert.That(() => LibraryTrash.CheckInside(Trashed, item), Throws.Nothing);
            Assert.That(LibraryTrash.OutsideTrash(item), Has.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.NotTrashed));
        });
    }

    [Test]
    public void AnItemWithoutAName_IsAMistake()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => LibraryTrash.CheckOutside(Trashed, ""), Throws.ArgumentException);
            Assert.That(() => LibraryTrash.CheckInside(null, null!), Throws.ArgumentNullException);
        });
    }

    private static EqualConstraint Refused(FormBuilderConflict conflict, string message) =>
        Throws.TypeOf<FormBuilderConflictException>()
            .With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(conflict)
            .And.Message.EqualTo(message);
}
