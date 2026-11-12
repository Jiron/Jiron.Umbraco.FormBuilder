using System.Reflection;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// Logs hold exception types and where they were thrown, never messages (ADR-0009, section 4.10, 16.1's review): every log
/// message of Core and Web that takes an exception takes a <see cref="LoggedException"/> (ADR-0226), which keeps the types,
/// a database's codes and the stack trace and drops every message, so a caught exception reaches no log as it is.
/// </summary>
[TestFixture]
public sealed class LoggedExceptionTests
{
    private static readonly Assembly[] Assemblies = [typeof(FormDefinition).Assembly, typeof(FormBuilderProblems).Assembly];

    [Test]
    public void EveryLogMessageThatTakesAnException_TakesALoggedException()
    {
        var methods = Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<LoggerMessageAttribute>() is not null)
            .ToList();
        var raw = methods
            .Where(method => method.GetParameters().Any(parameter => typeof(Exception).IsAssignableFrom(parameter.ParameterType) && parameter.ParameterType != typeof(LoggedException)))
            .Select(method => $"{method.DeclaringType!.FullName}.{method.Name}")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(methods, Has.Count.GreaterThan(100), "the log messages were found");
            Assert.That(methods.Count(method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(LoggedException))), Is.GreaterThan(30), "and those that take an exception");
            Assert.That(raw, Is.Empty, string.Join(Environment.NewLine, raw));
        });
    }

    [Test]
    public void EveryLogMessage_TakesOnlyScalars_SoNoRecordOrModelReachesALog()
    {
        // A record's generated ToString, or a structured log's destructuring, writes every property, answers and addresses
        // among them; a log message names each value it writes instead (16.1's review).
        var allowed = new HashSet<Type>
        {
            typeof(string), typeof(bool), typeof(char), typeof(byte), typeof(short), typeof(int), typeof(long), typeof(double), typeof(decimal),
            typeof(Guid), typeof(DateTimeOffset), typeof(DateTime), typeof(TimeSpan), typeof(LoggedException), typeof(ILogger),
        };
        bool Scalar(Type type) => allowed.Contains(Nullable.GetUnderlyingType(type) ?? type) || (Nullable.GetUnderlyingType(type) ?? type).IsEnum || typeof(ILogger).IsAssignableFrom(type);
        var wide = Assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<LoggerMessageAttribute>() is not null)
            .SelectMany(method => method.GetParameters().Where(parameter => !Scalar(parameter.ParameterType)).Select(parameter => $"{method.DeclaringType!.FullName}.{method.Name}({parameter.ParameterType.Name} {parameter.Name})"))
            .ToList();

        Assert.That(wide, Is.Empty, string.Join(Environment.NewLine, wide));
    }

    [TestCase(typeof(Core.Submissions.SubmissionRequest))]
    [TestCase(typeof(Core.Submissions.NewDraft))]
    [TestCase(typeof(Core.Persistence.Repositories.NewEmail))]
    [TestCase(typeof(Core.Persistence.Repositories.StoredEmail))]
    [TestCase(typeof(Core.Email.OutgoingEmail))]
    public void ARecordThatHoldsAnswersOrAddresses_PrintsOnlyItsName(Type record)
    {
        var toString = record.GetMethod(nameof(ToString), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes)!;

        Assert.Multiple(() =>
        {
            Assert.That(toString.DeclaringType, Is.EqualTo(record), "it writes its own");
            Assert.That(toString.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>(), Is.Null, "not the compiler's, which prints every property");
        });
    }
}
