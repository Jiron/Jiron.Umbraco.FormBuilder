using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>One of Umbraco's types the block needs: its key, its alias or name, and whether this call created it.</summary>
/// <param name="Key">The type's key.</param>
/// <param name="Name">The element type's alias, or the data type's name.</param>
/// <param name="Created">Whether this call created it; false for one that was there.</param>
internal sealed record FormBlockPart(Guid Key, string Name, bool Created);

/// <summary>What "create a form block type for me" left: the element type and the data type its property uses.</summary>
/// <param name="ElementType">The element type <c>Form</c>.</param>
/// <param name="DataType">The form picker's data type.</param>
internal sealed record FormBlockTypesResult(FormBlockPart ElementType, FormBlockPart DataType);

/// <summary>
/// "Create a form block type for me" (ROADMAP 6.7b, section 4.7): the element type <c>Form</c> (<see cref="ElementTypeAlias"/>)
/// with one property, <c>form</c>, of the data type "Jiron Form Builder form picker" (<see cref="DataTypeKey"/>), which an
/// editor then allows in a block list or grid. It creates what is missing and never changes a type that is there: an
/// element type or any other content type with the alias, and the data type with the key, stay as they are, however an
/// editor changed them, so a second call changes nothing.
/// </summary>
/// <param name="dataTypes">Umbraco's data types.</param>
/// <param name="contentTypes">Umbraco's content types.</param>
/// <param name="editors">Umbraco's property editors, the form picker among them.</param>
/// <param name="serializer">The serializer of data types' settings.</param>
/// <param name="shortStrings">Umbraco's string helper, which the types take.</param>
internal sealed class FormBlockTypes(
    IDataTypeService dataTypes,
    IContentTypeService contentTypes,
    PropertyEditorCollection editors,
    IConfigurationEditorJsonSerializer serializer,
    IShortStringHelper shortStrings)
{
    /// <summary>The element type's alias.</summary>
    public const string ElementTypeAlias = "jfbFormBlock";

    /// <summary>The element type's name, which editors see when they allow the block.</summary>
    public const string ElementTypeName = "Form";

    /// <summary>The element type's property, which holds the picked form.</summary>
    public const string PropertyAlias = "form";

    /// <summary>The data type's name.</summary>
    public const string DataTypeName = "Jiron Form Builder form picker";

    /// <summary>The data type's key, fixed for good, by which a second call finds it.</summary>
    public static readonly Guid DataTypeKey = new("c4f1a9e2-6b3d-4e58-9a07-2d8c5f1e3b64");

    /// <summary>Creates the data type and the element type where they are missing, as the given user.</summary>
    /// <param name="userKey">The user who asked, whom Umbraco records as the types' creator.</param>
    /// <exception cref="InvalidOperationException">Umbraco refused to create a type; the message says which and why.</exception>
    public async Task<FormBlockTypesResult> CreateAsync(Guid userKey)
    {
        var dataType = await dataTypes.GetAsync(DataTypeKey);
        var dataTypeCreated = dataType is null;
        if (dataType is null)
        {
            var created = await dataTypes.CreateAsync(
                new DataType(editors[FormPickerPropertyEditor.EditorAlias]!, serializer)
                {
                    Key = DataTypeKey,
                    Name = DataTypeName,
                    DatabaseType = ValueStorageType.Nvarchar,
                    ConfigurationData = new Dictionary<string, object> { ["multiple"] = false },
                },
                userKey);
            dataType = created.Success ? created.Result : throw new InvalidOperationException($"Umbraco refused the data type '{DataTypeName}': {created.Status}.");
        }

        if (contentTypes.Get(ElementTypeAlias) is { } existing)
        {
            return new FormBlockTypesResult(new FormBlockPart(existing.Key, existing.Alias, Created: false), new FormBlockPart(dataType.Key, dataType.Name ?? DataTypeName, dataTypeCreated));
        }

        var type = new ContentType(shortStrings, -1)
        {
            Alias = ElementTypeAlias,
            Name = ElementTypeName,
            Description = "A form of Jiron Form Builder, picked from the Forms section.",
            Icon = "icon-autofill",
            IsElement = true,
        };
        type.AddPropertyType(new PropertyType(shortStrings, dataType, PropertyAlias) { Name = "Form", Mandatory = true });
        var saved = await contentTypes.CreateAsync(type, userKey);
        return saved.Success
            ? new FormBlockTypesResult(new FormBlockPart(type.Key, type.Alias, Created: true), new FormBlockPart(dataType.Key, dataType.Name ?? DataTypeName, dataTypeCreated))
            : throw new InvalidOperationException($"Umbraco refused the element type '{ElementTypeAlias}': {saved.Result}.");
    }
}
