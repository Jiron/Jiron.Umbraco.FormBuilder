using Jiron.Umbraco.FormBuilder.Core.Persistence;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// A statement's argument as it goes out (ROADMAP 26.1): a time outside what SQL Server's DATETIME holds goes at that
/// edge, with its kind and without a conversion between zones; every other argument goes as it is.
/// </summary>
public sealed class FormBuilderDbParameterTests
{
    private static readonly DateTime First = new(1753, 1, 1, 0, 0, 0);
    private static readonly DateTime Last = new(9999, 12, 31, 23, 59, 59, 997);

    [TestCase(DateTimeKind.Utc)]
    [TestCase(DateTimeKind.Local)]
    [TestCase(DateTimeKind.Unspecified)]
    public void ATimeBeyondEitherEdge_GoesAtThatEdge_WithItsKind(DateTimeKind kind)
    {
        var early = (DateTime)FormBuilderDbExtensions.Parameter(DateTime.SpecifyKind(DateTime.MinValue, kind))!;
        var before = (DateTime)FormBuilderDbExtensions.Parameter(DateTime.SpecifyKind(new DateTime(1752, 12, 31, 23, 59, 59), kind))!;
        var late = (DateTime)FormBuilderDbExtensions.Parameter(DateTime.SpecifyKind(DateTime.MaxValue, kind))!;

        Assert.Multiple(() =>
        {
            Assert.That((early, early.Kind), Is.EqualTo((First, kind)));
            Assert.That((before, before.Kind), Is.EqualTo((First, kind)));
            Assert.That((late, late.Kind), Is.EqualTo((Last, kind)));
        });
    }

    [Test]
    public void ATimeInside_GoesAsItIs_EdgesIncluded_AndLocalTimesAreNotConverted()
    {
        var local = new DateTime(1753, 1, 1, 0, 0, 0, DateTimeKind.Local);

        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderDbExtensions.Parameter(First), Is.EqualTo(First));
            Assert.That(FormBuilderDbExtensions.Parameter(Last), Is.EqualTo(Last));
            Assert.That(FormBuilderDbExtensions.Parameter(new DateTime(2026, 11, 23, 9, 30, 0, DateTimeKind.Utc)), Is.EqualTo(new DateTime(2026, 11, 23, 9, 30, 0, DateTimeKind.Utc)));
            Assert.That(FormBuilderDbExtensions.Parameter(local), Is.EqualTo(local).And.Property(nameof(DateTime.Kind)).EqualTo(DateTimeKind.Local), "read as written, whatever the server's zone");
        });
    }

    [Test]
    public void EveryOtherArgument_IsTheSameObject()
    {
        object[] others = ["text", 42, 4.5, true, Guid.NewGuid(), new DateTimeOffset(1, 1, 1, 0, 0, 0, TimeSpan.Zero), new List<int> { 1 }];

        Assert.Multiple(() =>
        {
            foreach (var other in others)
            {
                Assert.That(FormBuilderDbExtensions.Parameter(other), Is.SameAs(other), other.GetType().Name);
            }

            Assert.That(FormBuilderDbExtensions.Parameter(null), Is.Null);
        });
    }
}
