using System.Data.Common;
using Jiron.Umbraco.FormBuilder.Core.Privacy;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>An exception as a background loop logs it: its type, codes and stack, never its message (ADR-0226).</summary>
public sealed class LoggedExceptionTests
{
    private const string Value = "ada@visitor.jfb.test";

    [Test]
    public void ItsMessage_StaysOut_AndItsTypeAndStackStay_InnerExceptionsToo()
    {
        var thrown = Thrown(() => throw new InvalidOperationException($"Could not store {Value}", new FormatException($"Bad {Value}")));

        var logged = LoggedException.Of(thrown);

        Assert.Multiple(() =>
        {
            Assert.That(logged.ToString(), Does.Not.Contain(Value), "nowhere in what a log writes");
            Assert.That(logged.Message, Is.EqualTo("System.InvalidOperationException; its message is not logged"));
            Assert.That(logged.StackTrace, Is.EqualTo(thrown.StackTrace).And.Contain(nameof(Thrown)), "where it was thrown");
            Assert.That(logged.InnerException?.Message, Is.EqualTo("System.FormatException; its message is not logged"));
            Assert.That(logged.HResult, Is.EqualTo(thrown.HResult));
            Assert.That(LoggedException.Of(logged), Is.SameAs(logged), "once is enough");
        });
    }

    [Test]
    public void ADatabasesError_KeepsItsCodes()
    {
        var logged = LoggedException.Of(new TruncatedValue());

        Assert.Multiple(() =>
        {
            Assert.That(logged.Message, Does.Contain("(error 2628, SQL state 22001)"));
            Assert.That(logged.ToString(), Does.Not.Contain(Value));
        });
    }

    private static Exception Thrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new AssertionException("It threw nothing.");
    }

    // A database's error that quotes the value it could not store, as SQL Server's truncation error does.
    private sealed class TruncatedValue : DbException
    {
        public TruncatedValue()
            : base($"String or binary data would be truncated. Truncated value: '{Value}'.", 2628)
        {
        }

        public override string SqlState => "22001";
    }
}
