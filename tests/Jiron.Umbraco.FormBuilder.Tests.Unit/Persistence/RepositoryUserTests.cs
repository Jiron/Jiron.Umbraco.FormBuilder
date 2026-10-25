using System.Reflection;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Dtos = Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// Every change a repository writes to a row that keeps who changed it last names that user (ROADMAP 4.8g): the
/// record an update of such a row takes has a user key the caller must give, null only when Jiron Form Builder makes
/// the change, and no repository method takes a user key its caller may leave out. Rows that keep no user (option lists,
/// themes, email layouts) leave who changed them to the audit log (ADR-0041).
/// </summary>
[TestFixture]
public sealed class RepositoryUserTests
{
    private static readonly Assembly Core = typeof(FormBuilderCoreComposer).Assembly;

    // Each row that keeps who changed it last, and the record its update takes; a folder changes through methods.
    private static readonly Dictionary<Type, Type?> Rows = new()
    {
        [typeof(Dtos.FolderDto)] = null,
        [typeof(Dtos.FormDto)] = typeof(FormUpdate),
        [typeof(Dtos.FormTemplateDto)] = typeof(FormTemplateUpdate),
        [typeof(Dtos.SettingDto)] = typeof(SettingsSave),
    };

    [Test]
    public void EveryRowThatKeepsItsUser_IsListed()
    {
        var keeping = Core.GetTypes().Where(type => type.Namespace == typeof(Dtos.FormDto).Namespace && type.GetProperty("UpdatedByUserKey") is not null);

        Assert.That(keeping, Is.EquivalentTo(Rows.Keys));
    }

    [Test]
    public void AnUpdateOfARowThatKeepsItsUser_RequiresTheUser()
    {
        Assert.Multiple(() =>
        {
            foreach (var record in Rows.Values.OfType<Type>())
            {
                var user = UserKey(record);
                Assert.That(user?.ParameterType, Is.EqualTo(typeof(Guid?)), $"{record.Name} names its user");
                Assert.That(user?.HasDefaultValue, Is.False, $"{record.Name} requires its user");
            }
        });
    }

    [Test]
    public void NoUpdateRecord_NorRepositoryMethod_LetsItsCallerLeaveOutTheUser()
    {
        var records = Core.GetTypes()
            .Where(type => type.Namespace == typeof(FormUpdate).Namespace && type.Name.EndsWith("Update", StringComparison.Ordinal) && UserKey(type) is not null)
            .ToList();
        var methods = Core.GetTypes()
            .Where(type => type.Namespace == typeof(FormUpdate).Namespace && type.IsInterface)
            .SelectMany(type => type.GetMethods().Select(method => (type, method)))
            .SelectMany(pair => pair.method.GetParameters().Where(parameter => parameter.Name == "userKey").Select(parameter => (pair.type, pair.method, parameter)))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(records, Is.SupersetOf(new[] { typeof(FormUpdate), typeof(FormTemplateUpdate) }), "the update records with a user");
            Assert.That(records.Where(record => UserKey(record)!.HasDefaultValue).Select(record => record.Name), Is.Empty, "update records that may leave out the user");
            Assert.That(methods.Select(found => found.type).Distinct(), Has.Member(typeof(IFolderRepository)), "the folder's changes take the user");
            Assert.That(methods.Where(found => found.parameter.HasDefaultValue).Select(found => $"{found.type.Name}.{found.method.Name}"), Is.Empty, "methods that may leave out the user");
        });
    }

    private static ParameterInfo? UserKey(Type record) =>
        record.GetConstructors().SelectMany(constructor => constructor.GetParameters()).FirstOrDefault(parameter => parameter.Name == "UserKey");
}
