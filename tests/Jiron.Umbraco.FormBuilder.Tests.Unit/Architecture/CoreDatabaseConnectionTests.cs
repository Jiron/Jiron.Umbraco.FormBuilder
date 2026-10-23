using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using IScopeProvider = Umbraco.Cms.Infrastructure.Scoping.IScopeProvider;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// One way to the database (ADR-0007, ADR-0036): Core reaches the database only through Umbraco's scopes and never
/// opens a connection of its own, because on SQLite a connection outside Umbraco's shared cache can hang Umbraco's own
/// transactions (1.6). The test reads the IL of every method in Core, the bodies of async methods and lambdas included,
/// and fails on any call that creates a connection or a database: so does the gate, and with it the build.
/// </summary>
[TestFixture]
public sealed class CoreDatabaseConnectionTests
{
    [Test]
    public void Core_OpensNoDatabaseConnectionOfItsOwn()
    {
        var scan = IlScan.Of(typeof(FormBuilderCoreComposer).Assembly.GetTypes());

        Assert.Multiple(() =>
        {
            Assert.That(scan.Unreadable, Is.Empty, "every call in Core was read");
            Assert.That(scan.Calls, Has.Count.GreaterThan(1000), "the scan read Core's methods");
            Assert.That(
                scan.Calls.Any(call => call.Callee.Name == nameof(IScopeProvider.CreateScope) && call.Callee.DeclaringType == typeof(IScopeProvider)),
                Is.True,
                "the scan reads the bodies of async lambdas, where Core opens its scopes");
            Assert.That(scan.Calls.Where(call => ConnectionBan.Reason(call.Callee) is not null).Select(call => call.ToString()), Is.Empty);
        });
    }

    [Test]
    public void TheScan_FindsEveryWayToAConnection_AlsoInAsyncMethodsAndLambdas()
    {
        var scan = IlScan.Of(Nested(typeof(Offender)));
        var found = scan.Calls.Select(call => ConnectionBan.Reason(call.Callee)).OfType<string>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(scan.Unreadable, Is.Empty);
            Assert.That(found.Distinct(), Is.EquivalentTo(ConnectionBan.Reasons));
            Assert.That(found.Count(reason => reason == ConnectionBan.NewConnection), Is.EqualTo(3), "in a method, an async method and a lambda");
        });
    }

    [Test]
    public void TheScan_LetsUmbracosScopesAndTheDatabaseFactorysSqlContextThrough()
    {
        var scan = IlScan.Of(Nested(typeof(Allowed)));

        Assert.Multiple(() =>
        {
            Assert.That(scan.Calls, Is.Not.Empty);
            Assert.That(scan.Calls.Where(call => ConnectionBan.Reason(call.Callee) is not null).Select(call => call.ToString()), Is.Empty);
        });
    }

    private static IEnumerable<Type> Nested(Type type) =>
        new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(Nested));

    /// <summary>The calls that open a connection or create a database, each with the reason a test names.</summary>
    private static class ConnectionBan
    {
        public const string NewConnection = "constructs a DbConnection";
        public const string FactoryConnection = "DbProviderFactory.CreateConnection";
        public const string DataSourceConnection = "a DbDataSource's connection or command";
        public const string UmbracoDatabase = "IUmbracoDatabaseFactory.CreateDatabase";
        public const string NPocoDatabase = "constructs an NPoco database";
        public const string ProviderFactory = "IDbProviderFactoryCreator.CreateFactory";

        public static readonly IReadOnlyList<string> Reasons = [NewConnection, FactoryConnection, DataSourceConnection, UmbracoDatabase, NPocoDatabase, ProviderFactory];

        private static readonly string[] DataSourceMembers = ["CreateConnection", "OpenConnection", "OpenConnectionAsync", "CreateCommand", "CreateBatch"];

        public static string? Reason(MethodBase callee)
        {
            var type = callee.DeclaringType;
            if (type is null)
            {
                return null;
            }

            if (callee is ConstructorInfo)
            {
                return typeof(DbConnection).IsAssignableFrom(type) || typeof(IDbConnection).IsAssignableFrom(type) ? NewConnection
                    : typeof(IDatabase).IsAssignableFrom(type) ? NPocoDatabase
                    : null;
            }

            return callee.Name switch
            {
                "CreateConnection" when typeof(DbProviderFactory).IsAssignableFrom(type) => FactoryConnection,
                _ when typeof(DbDataSource).IsAssignableFrom(type) && DataSourceMembers.Contains(callee.Name) => DataSourceConnection,
                "CreateDatabase" when typeof(IUmbracoDatabaseFactory).IsAssignableFrom(type) => UmbracoDatabase,
                "CreateFactory" when typeof(IDbProviderFactoryCreator).IsAssignableFrom(type) => ProviderFactory,
                _ => null,
            };
        }
    }

    /// <summary>Every method call, constructor call and method pointer in the IL of some types.</summary>
    private sealed class IlScan
    {
        private static readonly OpCode[] OneByte = new OpCode[256];
        private static readonly OpCode[] TwoByte = new OpCode[256];

        static IlScan()
        {
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var code = (OpCode)field.GetValue(null)!;
                var value = (ushort)code.Value;
                if (code.Size == 1)
                {
                    OneByte[value] = code;
                }
                else
                {
                    TwoByte[value & 0xFF] = code;
                }
            }
        }

        public List<Call> Calls { get; } = [];

        public List<string> Unreadable { get; } = [];

        public static IlScan Of(IEnumerable<Type> types)
        {
            var scan = new IlScan();
            const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                {
                    scan.Read(method);
                }
            }

            return scan;
        }

        private void Read(MethodBase method)
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il is null)
            {
                return;
            }

            var typeArguments = method.DeclaringType is { IsGenericType: true } type ? type.GetGenericArguments() : null;
            var methodArguments = method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null;
            var at = 0;
            while (at < il.Length)
            {
                var code = il[at] == 0xFE ? TwoByte[il[++at]] : OneByte[il[at]];
                at++;
                if (code.Size == 0)
                {
                    Unreadable.Add($"{Name(method)}: unknown opcode at {at - 1}");
                    return;
                }

                if (code.OperandType == OperandType.InlineMethod)
                {
                    var token = BitConverter.ToInt32(il, at);
                    try
                    {
                        Calls.Add(new Call(method, method.Module.ResolveMethod(token, typeArguments, methodArguments)!));
                    }
                    catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or TypeLoadException or FileNotFoundException)
                    {
                        Unreadable.Add($"{Name(method)}: {exception.GetType().Name} for token {token:X8}");
                    }
                }

                at += OperandSize(code, il, at);
            }
        }

        private static int OperandSize(OpCode code, byte[] il, int at) => code.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, at)),
            _ => 4,
        };

        private static string Name(MethodBase method) => $"{method.DeclaringType?.FullName}.{method.Name}";

        public sealed record Call(MethodBase Caller, MethodBase Callee)
        {
            public override string ToString() => $"{Name(Caller)} calls {Name(Callee)}";
        }
    }

    // Never called: the scan reads their IL to show that it finds each way to a connection.
    private static class Offender
    {
        public static StandInConnection NewConnection() => new StandInConnection();

        public static DbConnection? FactoryConnection(DbProviderFactory factory) => factory.CreateConnection();

        public static DbConnection DataSourceConnection(DbDataSource source) => source.OpenConnection();

        public static IUmbracoDatabase UmbracoDatabase(IUmbracoDatabaseFactory factory) => factory.CreateDatabase();

        public static Database NPocoDatabase(DbConnection connection) => new Database(connection);

        public static DbProviderFactory? ProviderFactory(IDbProviderFactoryCreator creator) => creator.CreateFactory("JFB_TEST");

        public static async Task<object> InAnAsyncMethod()
        {
            await Task.Yield();
            return new StandInConnection();
        }

        public static Func<object> InALambda() => () => new StandInConnection();
    }

    // Never called: what Core does, which the scan lets through.
    private static class Allowed
    {
        public static async Task<int> ThroughAScope(IScopeProvider scopes)
        {
            using var scope = scopes.CreateScope(autoComplete: true);
            await Task.Yield();
            return scope.Database.ExecuteScalar<int>("SELECT 1");
        }

        public static string? TheProvider(IUmbracoDatabaseFactory factory) => factory.SqlContext?.SqlSyntax?.ProviderName;
    }

    private sealed class StandInConnection : DbConnection
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;

        public override string Database => string.Empty;

        public override string DataSource => string.Empty;

        public override string ServerVersion => string.Empty;

        public override ConnectionState State => ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();

        public override void Close()
        {
        }

        public override void Open() => throw new NotSupportedException();

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }
}
