// The form check's texts in English (ROADMAP 9.5f, ADR-0181): its sidebar, its result in the publish dialog, and a
// sentence for each problem the server's check finds, by its code. A sentence names the text it is about where the
// problem's path says which; the element it is on is the row's name. The codes whose words only the server has (a
// type's own check, a value's bounds) show the server's sentence instead, and have none here.

/** " in "Label"", where a sentence names the text it is about. */
const within = (place: string) => (place ? ` in "${place}"` : '');

const count = (number: number, one: string, many: string) => (number === 1 ? `1 ${one}` : `${number} ${many}`);

export default {
  jfbCheck: {
    headline: 'Form check',
    errors: (number: number) => count(number, 'error', 'errors'),
    warnings: (number: number) => count(number, 'warning', 'warnings'),
    hints: (number: number) => count(number, 'suggestion', 'suggestions'),
    blocks: 'Blocks publishing',
    fix: 'Fix it',
    fixNamed: (name: string) => `Fix it: ${name}`,
    none: 'No problems. The form can be published.',
    checking: 'Checking the form…',
    checkingAgain: 'Checking the latest changes…',
    failed: "The form check didn't answer. Try again in a moment.",
    retry: 'Try again',

    publishClear: 'The form check found no problems.',
    publishErrors: (number: number) =>
      number === 1
        ? "This form can't be published yet: 1 error needs fixing."
        : `This form can't be published yet: ${number} errors need fixing.`,
    publishWarnings: (number: number) =>
      number === 1
        ? "The form check found 1 warning. It doesn't block publishing."
        : `The form check found ${number} warnings. They don't block publishing.`,
    publishAlsoWarnings: (number: number) =>
      number === 1
        ? "It also has 1 warning, which doesn't block publishing."
        : `It also has ${number} warnings, which don't block publishing.`,
    publishFailed: "The form check didn't answer. Publishing checks the form again.",
    publishMore: (number: number) =>
      number === 1 ? 'And 1 more, which the form check lists.' : `And ${number} more, which the form check lists.`,

    name_form: 'The form',
    name_settings: 'Form settings',
    name_outcomes: 'After sending',
    name_outcome: (number: number) => `After sending: outcome ${number}`,
    name_action: (number: number) => `Action ${number}`,
    name_fieldsMissing: 'Add at least one field',

    place_option: (number: number) => `Option ${number}`,
    place_row: (number: number) => `Row ${number}`,
    place_message: 'Message',
    place_description: 'Description',
    place_redirect: 'Redirect address',
    place_entryTitle: 'Entry title',
    place_button: 'Button text',

    code_fields_missing: 'The form has no field to fill in yet, so visitors would have nothing to send.',
    code_label_missing: 'This field has no label, which screen readers and the entries name it by.',
    code_lock_replaces_value: (setting: string) =>
      `The site locks "${setting}", so the form runs with the site's value instead of the one it holds.`,
    code_unknown_type: (kind: string, alias: string) =>
      kind === 'action'
        ? `The site has no action type "${alias}".`
        : kind === 'captcha'
          ? `The site has no captcha provider "${alias}".`
          : `The site has no field type "${alias}".`,
    code_setting_unknown: (name: string) => `It holds the setting "${name}", which its type doesn't have.`,
    code_setting_missing: (setting: string) => `"${setting}" needs a value.`,

    code_unknown_path: (place: string) => `A merge tag${within(place)} names nothing, so it shows nothing.`,
    code_unknown_filter: (place: string) => `A merge tag${within(place)} uses a filter the site doesn't have.`,
    code_field_after_text: (place: string) =>
      `A merge tag${within(place)} recalls the answer of a field that comes after this text, so visitors haven't given it yet.`,
    code_source_not_for_place: (place: string) =>
      `A merge tag${within(place)} takes its value from a source this text can't use.`,
    code_filter_not_for_place: (place: string) =>
      `A merge tag${within(place)} uses a filter of the site's own that this text can't use.`,
    code_too_many_tags: (place: string) => `There are more merge tags${within(place)} than a text may hold.`,
    code_too_many_filters: (place: string) => `A merge tag${within(place)} has more filters than a tag may have.`,
    code_too_long: (place: string) => `A merge tag${within(place)} makes the text longer than a text may be.`,
    code_malformed_tag: (place: string) => `A merge tag${within(place)} isn't written as merge tags are.`,
    code_unclosed_tag: (place: string) => `A merge tag${within(place)} isn't closed with }}.`,
    code_tag_in_markup: (place: string) =>
      `A merge tag${within(place)} sits inside the text's markup, where it can't be shown.`,
    code_tag_before_path: 'A merge tag comes before the path of the address, so it could lead anywhere.',
    code_invalid_argument: (place: string) => `A filter of a merge tag${within(place)} is given a value it can't take.`,
    code_filter_not_for_context: (place: string) =>
      `A merge tag${within(place)} uses a filter that doesn't work in this text.`,
    code_filter_after_encoding: (place: string) =>
      `A merge tag${within(place)} uses a filter after the one that encodes its value.`,
    code_answers_in_url: 'The redirect puts answers into its address, which ends up in logs and browser history.',

    code_captcha_not_configured: (provider: string) =>
      `The captcha provider "${provider}" has no keys on this site yet, so the form's entries go to Spam until it has them.`,
    code_no_reply_field: 'The form has no email field, so a reply to this mail reaches no one who filled in the form.',
    code_reply_field_unset:
      'Choose the email field to reply to, so a reply to this mail reaches the person who filled in the form.',
    code_required_can_hide:
      'This rule makes the field required for answers for which a rule hides it. A hidden field is never required, so visitors can send without it.',
    code_page_unreachable: (number: number) =>
      `No answers a visitor can give before page ${number} make it show, so a visitor going forward never reaches it.`,

    code_consent_missing:
      'The form asks for personal data, such as this answer, but has no consent field. Add one if consent is the basis on which the entries are kept and used.',
    code_card_number_asked:
      "This field asks for a card number. Take payments through a payment provider: card data never belongs in a form's entries, mail or exports.",
    code_password_asked:
      "This field asks for a password, which would end up in the form's entries, mail and exports. Let members sign in instead.",
    code_file_size_unset:
      "This field takes files up to the site's limit for each file: give it the largest size its answers need.",
    code_options_can_fill:
      'Every option of this required field has a number of places, so once all are taken nobody can send the form: add an option without a limit, or let the field be left empty.',
    code_limit_without_entries: (form: boolean) =>
      form
        ? 'The form keeps no entries, so this limit forgets each entry once its actions have run.'
        : "This field's answers are kept only until the actions have run, so this limit gives them back then.",
    code_last_outcome_has_rule:
      "Visitors whose answers no outcome's rule matches get the built-in thank-you message: leave the last outcome without a rule, so it is there for every other answer.",

    code_theme_missing: (theme: string) =>
      `The site has no theme "${theme}", so the form shows with the default theme.`,
    code_content_missing: (redirect: boolean) =>
      redirect
        ? "The site has no such page any more, so visitors get the outcome's message or the built-in thank-you message instead."
        : 'The site has no such content item any more.',
    code_redirect_unpublished:
      "This page isn't published, so visitors get the outcome's message or the built-in thank-you message until it is.",
    code_media_missing: 'The site has no such media item any more.',
    code_content_type_missing: 'Choose the document type each entry becomes.',
    code_content_type_gone: 'This site no longer has that document type. Choose another.',
    code_content_parent_missing: 'Choose the item it must stay within.',
    code_content_name_missing: 'The item needs a name.',
    code_content_sensitive_unknown: 'A field chosen here is no longer on this form.',
    code_content_mapping_invalid: 'Each property needs a field or a value.',
    code_content_property_gone: 'The document type has no such property. Remove the row.',
    code_content_property_twice: "An earlier row sets this property too; this row's value wins.",
    code_content_field_gone: 'That field is no longer on this form. Choose another.',
    code_content_field_cannot_hold: "The property can't hold that field's answer. Choose another.",
    code_content_value_cannot_hold: "The property can't hold this value. Write it as the box asks.",
    code_content_group_field:
      'That field is in a repeatable group, with an answer for each row, and a property holds one. Choose another.',
    code_state_already:
      "Entries are in that state already when this trigger's actions run, so this action changes nothing.",
    code_state_loop: (other: string) =>
      `${other ? `"${other}"` : 'Another action'} moves the entry back, so the two would move it back and forth for ever. Change one of them.`,
    code_member_group_missing: (group: string) =>
      group
        ? `The site has no member group named "${group}", so no member is in it. Names are compared exactly.`
        : 'The site has no such member group any more, so no member is in it.',
    code_dictionary_text_missing: (key: string, place: string) =>
      `Umbraco's dictionary has no text for "${key}"${within(place)} in some of the site's languages, so visitors there see the text as it is written.`,

    code_options_invalid: "Its options aren't set up as its type needs them.",
    code_property_not_supported: (place: string) =>
      place ? `Its type doesn't take "${place}".` : "Its type doesn't take this setting.",
    code_part_missing: (part: string) =>
      ({
        pages: 'The form has no page.',
        groups: 'This page has no group.',
        rows: 'This grid has no rows.',
        fields: 'This repeatable group has no fields.',
        options: 'This field has no options.',
        list: 'This field takes its options from an option list, but names none.',
        memberGroups: 'The form is for some member groups, but names none.',
        captcha: 'The form is protected by a captcha, but names no provider.',
        redirect: "The redirect doesn't say where it leads.",
      })[part] ?? 'Something this element needs is missing.',
    code_field_not_allowed: "A field of this type can't be here.",
    code_alias_invalid: (alias: string) =>
      alias
        ? `"${alias}" can't be an alias: an alias starts with a lowercase letter and holds letters and digits only.`
        : 'This field has no alias, which the entries keep its answer under.',
    code_alias_reserved: (alias: string) => `"${alias}" can't be an alias: the form keeps it for its own use.`,
    code_alias_duplicate: (alias: string) => `Another field has the alias "${alias}" already.`,
    code_name_invalid: "This name starts the way only the site's own names may.",
    code_name_duplicate: (name: string) => `"${name}" is in the list twice.`,
    code_option_value_invalid: "An option has no value, or one an option can't have.",
    code_option_value_duplicate: 'Two options have the same value.',
    code_unknown_field: "It names a field the form doesn't have.",
    code_invalid_reference: "It refers to a field or an option that doesn't fit here.",
    code_invalid_condition: "A condition of its rule isn't complete.",
    code_unknown_operator: "A condition compares in a way the form doesn't know.",
    code_operator_not_for_kind: "A condition compares in a way this field's answers can't be compared.",
    code_invalid_operand: "A condition compares with a value that doesn't fit.",
    code_unsafe_pattern: "The site's matcher can't check this pattern safely.",
    code_too_deep: 'Its rule nests conditions deeper than a rule may.',
    code_constant_formula: 'Its formula names no field, so its result never changes.',
    code_rule_effect_invalid: "A rule of it does something this element can't do.",
    code_rule_duplicate: 'It has two rules that do the same.',
    code_cycle: 'Its rules or calculations depend on each other in a circle.',
    code_duplicate_key: 'Two elements of the form have the same key.',
  },
};
