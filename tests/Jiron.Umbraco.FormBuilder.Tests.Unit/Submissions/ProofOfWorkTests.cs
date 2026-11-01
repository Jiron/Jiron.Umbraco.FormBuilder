using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Submissions;

/// <summary>The puzzle (ROADMAP 7.2a, ADR-0141): the hash's leading zero bits, and what counts as an answer.</summary>
[TestFixture]
public sealed class ProofOfWorkTests
{
    private static readonly ChallengeTicket Ticket = new(Guid.Parse("71e00000-0000-4000-8000-000000000001"), DateTimeOffset.UnixEpoch, null, 8);

    [TestCase("a", "0")]
    [TestCase("a", "1")]
    [TestCase("nonce", "123")]
    public void TheCount_IsOfTheHashesLeadingZeroBits(string nonce, string answer)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes($"{nonce}:{answer}"));
        var expected = 0;
        foreach (var part in hash)
        {
            if (part == 0)
            {
                expected += 8;
                continue;
            }

            expected += System.Numerics.BitOperations.LeadingZeroCount((uint)part) - 24;
            break;
        }

        Assert.That(ProofOfWork.LeadingZeroBits(nonce, answer), Is.EqualTo(expected));
    }

    [Test]
    public void TheNonce_IsTheChallengesId() => Assert.That(ProofOfWork.NonceOf(Ticket), Is.EqualTo("71e00000000040008000000000000001"));

    [Test]
    public void TheRightAnswer_Solves_AndANumberThatFallsShort_DoesNot()
    {
        var nonce = ProofOfWork.NonceOf(Ticket);
        var numbers = Enumerable.Range(0, 5_000).Select(number => number.ToString(CultureInfo.InvariantCulture)).ToList();
        var right = numbers.First(answer => ProofOfWork.LeadingZeroBits(nonce, answer) >= 8);
        var wrong = numbers.First(answer => ProofOfWork.LeadingZeroBits(nonce, answer) == 7);

        Assert.Multiple(() =>
        {
            Assert.That(ProofOfWork.IsSolved(Ticket, right), Is.True);
            Assert.That(ProofOfWork.IsSolved(Ticket, wrong), Is.False);
        });
    }

    [TestCase(null, TestName = "No answer")]
    [TestCase("", TestName = "An empty answer")]
    [TestCase("12a", TestName = "An answer with a letter")]
    [TestCase("-1", TestName = "An answer with a sign")]
    [TestCase("123456789012345678901", TestName = "An answer longer than a number can be")]
    public void WhatIsNoNumber_SolvesNothing(string? answer)
    {
        // A puzzle that asks for nothing would pass any number, so only the answer's form can refuse here.
        var easy = Ticket with { ProofBits = 1 };

        Assert.That(ProofOfWork.IsSolved(easy, answer), Is.False);
    }

    [TestCase("x{0}", TestName = "A text whose hash would solve it")]
    [TestCase("-{0}", TestName = "A signed number whose hash would solve it")]
    public void OnlyDigits_CanSolveIt(string form)
    {
        var nonce = ProofOfWork.NonceOf(Ticket);
        var would = Enumerable.Range(0, 5_000).Select(number => string.Format(CultureInfo.InvariantCulture, form, number)).First(answer => ProofOfWork.LeadingZeroBits(nonce, answer) >= Ticket.ProofBits);

        Assert.That(ProofOfWork.IsSolved(Ticket, would), Is.False);
    }

    [Test]
    public void AChallengeWithoutAPuzzle_IsSolvedByNothing() => Assert.That(ProofOfWork.IsSolved(Ticket with { ProofBits = 0 }, "1"), Is.False);
}
