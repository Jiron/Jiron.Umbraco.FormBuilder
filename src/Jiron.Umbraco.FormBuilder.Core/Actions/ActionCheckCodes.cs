namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>
/// The codes the built-in action types' own checks give their problems (ADR-0192), which the backoffice says in its own
/// words; a site's own type keeps <c>setting_invalid</c> and its sentence, or a code the backoffice shows by its sentence.
/// </summary>
internal static class ActionCheckCodes
{
    /// <summary>A content action without a document type.</summary>
    public const string ContentTypeMissing = "content_type_missing";

    /// <summary>A document type the site lacks, or one no item can have.</summary>
    public const string ContentTypeGone = "content_type_gone";

    /// <summary>A parent from the visitor's page or site root without the item it must stay within.</summary>
    public const string ContentParentMissing = "content_parent_missing";

    /// <summary>An item without a name.</summary>
    public const string ContentNameMissing = "content_name_missing";

    /// <summary>A sensitive field to copy that the form lacks.</summary>
    public const string ContentSensitiveUnknown = "content_sensitive_unknown";

    /// <summary>A mapping that is no list of properties, each with a field or a value.</summary>
    public const string ContentMappingInvalid = "content_mapping_invalid";

    /// <summary>A row's property the document type lacks.</summary>
    public const string ContentPropertyGone = "content_property_gone";

    /// <summary>A property an earlier row sets too, a warning.</summary>
    public const string ContentPropertyTwice = "content_property_twice";

    /// <summary>A row's field the form lacks.</summary>
    public const string ContentFieldGone = "content_field_gone";

    /// <summary>A row's field whose answer the property cannot hold.</summary>
    public const string ContentFieldCannotHold = "content_field_cannot_hold";

    /// <summary>A row's value the property cannot hold.</summary>
    public const string ContentValueCannotHold = "content_value_cannot_hold";

    /// <summary>A row's field in a repeatable group, which has an answer for each row.</summary>
    public const string ContentGroupField = "content_group_field";

    /// <summary>A Set state action to the state its trigger's entries are in already, a warning.</summary>
    public const string StateAlready = "state_already";

    /// <summary>Two Set state actions that would move an entry back and forth.</summary>
    public const string StateLoop = "state_loop";
}
