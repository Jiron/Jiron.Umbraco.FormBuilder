using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// Numbers as visitors post them (ADR-0058): the wire form and what a browser's number input posts, converted exactly to
/// the wire form of ADR-0005, and refused when they are no number or longer than the 60 digits a calculation reads;
/// whether a stored number keeps their digits is the check's (ADR-0069).
/// </summary>
public sealed class PostedNumbersTests
{
    [TestCase("0", "0")]
    [TestCase("-0", "0")]
    [TestCase("-0.000", "0")]
    [TestCase("0e999999999999", "0")]
    [TestCase("7", "7")]
    [TestCase("007", "7")]
    [TestCase("-12.5", "-12.5")]
    [TestCase("1.50", "1.5")]
    [TestCase("000.0010", "0.001")]
    [TestCase(".5", "0.5")]
    [TestCase("-.5", "-0.5")]
    [TestCase("1.5e3", "1500")]
    [TestCase("1.5E+3", "1500")]
    [TestCase("15e-1", "1.5")]
    [TestCase("-12.34e-2", "-0.1234")]
    [TestCase("100e-2", "1")]
    [TestCase("1e14", "100000000000000")]
    [TestCase("1e-15", "0.000000000000001")]
    [TestCase("123456789012345", "123456789012345")]
    [TestCase("0.123456789012345", "0.123456789012345")]
    [TestCase("12345678901234.5", "12345678901234.5")]
    [TestCase("1234567890123450000e-4", "123456789012345")]
    [TestCase("00000000000000000000000000001", "1")]
    public void ToWire_ANumber_IsItsWireForm(string posted, string wire)
    {
        var converted = PostedNumbers.ToWire(posted);

        Assert.Multiple(() =>
        {
            Assert.That(converted, Is.EqualTo(wire));
            Assert.That(ExactDecimal.Parse(converted!)?.ToString(), Is.EqualTo(wire), "the canonical wire form");
            Assert.That(ExactDecimal.Fits(ExactDecimal.Parse(converted!)!.Value), Is.True, "which a stored number holds");
        });
    }

    [TestCase("")]
    [TestCase("-")]
    [TestCase(".")]
    [TestCase("-.")]
    [TestCase("5.")]
    [TestCase("1e")]
    [TestCase("1e+")]
    [TestCase("e5")]
    [TestCase("+1")]
    [TestCase("--1")]
    [TestCase(" 1")]
    [TestCase("1 ")]
    [TestCase("1,5")]
    [TestCase("1_000")]
    [TestCase("0x10")]
    [TestCase("1.2.3")]
    [TestCase("1e1.5")]
    [TestCase("1e2e3")]
    [TestCase("Infinity")]
    [TestCase("NaN")]
    public void ToWire_NoNumber_IsNull(string posted) => Assert.That(PostedNumbers.ToWire(posted), Is.Null);

    [Test]
    public void ToWire_DigitsOtherThanAscii_AreNoNumber() =>
        Assert.That(PostedNumbers.ToWire(new string((char)0x0661, 2)), Is.Null);

    [TestCase("1e15", "1000000000000000")]
    [TestCase("1234567890123456", "1234567890123456")]
    [TestCase("0.0000000000000001", "0.0000000000000001")]
    [TestCase("1e59", "100000000000000000000000000000000000000000000000000000000000")]
    [TestCase("1e-60", "0.000000000000000000000000000000000000000000000000000000000001")]
    public void ToWire_ANumberPastTheStoredDigits_IsRead_ForTheCheckToRefuse(string posted, string wire)
    {
        var converted = PostedNumbers.ToWire(posted);

        Assert.Multiple(() =>
        {
            Assert.That(converted, Is.EqualTo(wire));
            Assert.That(ExactDecimal.Fits(ExactDecimal.Parse(converted!)!.Value), Is.False, "which no stored number holds");
        });
    }

    [TestCase("1e60")]
    [TestCase("1234567890123456789012345678901234567890123456789012345678901")]
    [TestCase("123456789012345678901234567890.1234567890123456789012345678901")]
    [TestCase("1e-61")]
    [TestCase("0.1234567890123456789012345678901234567890123456789012345678901")]
    [TestCase("1e9999999999")]
    [TestCase("1e-9999999999")]
    public void ToWire_ANumberTooLongToRead_IsNull(string posted) => Assert.That(PostedNumbers.ToWire(posted), Is.Null);

    [Test]
    public void ToWire_RefusesNull() => Assert.That(() => PostedNumbers.ToWire(null!), Throws.ArgumentNullException);
}
