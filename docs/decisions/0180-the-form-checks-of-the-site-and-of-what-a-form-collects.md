# 0180. The form check's checks of the site and of what a form collects

- Status: accepted; builds on [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (dictionary
  texts, the last outcome, what an export names by key), [ADR-0008](0008-settings-model.md) (a restriction that arrives
  later), [ADR-0015](0015-themes-and-the-markup-contract.md) (a form whose theme is gone),
  [ADR-0046](0046-copies-templates-and-import.md) (content, media and member groups an import keeps),
  [ADR-0158](0158-the-action-framework.md) (limits on what a form does not keep) and
  [ADR-0177](0177-the-form-check-on-the-server.md) (the form check)
- Date: 2026-11-04
- Roadmap step: 9.5e (9.5f shows the check in the designer)

## Context

The form check of 9.5b and 9.5d weighs what the field types, the merge tags and the form's rules know. ROADMAP 9.5e adds
what only the site and the form's purpose show: personal data collected without a consent field (a hint), an outcome
that redirects to an unpublished page, a file field without limits, labels or patterns that ask for card numbers or
passwords (a warning, section 1.4), and a required choice whose options can all become full. The decision records
promise more: a dictionary key without a text (a warning, ADR-0002, ADR-0067), a last outcome with a rule (a warning,
ADR-0002), a missing theme (ADR-0015, ADR-0037), a host lock that replaces a stored value (ADR-0008), the content, media
and member groups a form names that the site lacks (ADR-0002, ADR-0046), place or unique limits on a form that keeps no
entries (ADR-0158), and a merge tag in a label from a source labels refuse (ADR-0118).

Open were what each of them means where the words leave room, what each weighs, and how the check finds what it needs
without guessing.

## Options

1. **Personal data.** A fixed list of the built-in types that ask for it; or a trait each type declares, so a site's own
   types take part, together with the autocomplete tokens that name a person.
2. **Card numbers and passwords.** Words only; or the autocomplete tokens, the words of the label and placeholder in the
   visitor languages, and what a pattern takes, tried with the form's own matcher.
3. **Host locks.** A check of its own over the locked definition, which cannot see what was stored; or the form check
   itself, which holds both.

## Decision

Option 2, option 2 and option 3, with these meanings and weights:

- **Personal data without a consent field** (`consent_missing`): a hint, since consent is one lawful basis of several
  and a contact form may rely on another. A field asks for personal data when its type declares
  `FieldTypeDeclaration.PersonalData` (the built-in email, phone, name, address and signature; a site's own type may
  declare it, and one that holds no value is refused at start-up) or its autocomplete token names, reaches or locates a
  person (`name` and its parts, `nickname`, `username`, `email`, `impp`, `tel` and its parts, the street, address lines
  and postcode, `bday` and its parts, `sex`, `photo`), whatever section, address or contact kind comes before it. A
  file may or may not be about a person, a country or a city alone is none. The hint is said at the first such field;
  a consent field anywhere in the form, in a repeatable group too, answers it.
- **A card number or a password asked for** (`card_number_asked`, `password_asked`): warnings, said where the field asks,
  for fields a visitor types into (those that take a placeholder, an autocomplete token or a pattern). A field asks for
  a card number when its token starts with `cc-`, when its label or placeholder in any culture names a payment card or
  its code (credit card, debit card, CVV, CVC, and their German, French, Italian, Dutch, Spanish and Danish words,
  compounds included), or when its pattern takes a card's sixteen digits, plain or in groups of four, and refuses a
  twelve-digit number. "Card number" named without the card, in each language, counts only as the whole text, since a
  membership or a library card has a number too. A field asks for a password when its token is `current-password` or
  `new-password`, or its label or placeholder names one. Texts are compared folded: lowercase, without accents, in
  words. Help texts are left out, since they often say what not to type. A bank account number is not card data: a
  direct debit mandate is a lawful thing for a form to take, and the check names only what ROADMAP 9.5e names.
- **A file field without a size of its own** (`file_size_unset`): a hint at `settings.maxBytes`, naming the site's
  limit (`Files:MaxFileBytes`), which is set for the largest file any form takes. A file field always has limits on its
  types (images and PDFs by default) and on its number of files (one by default); its size is the one it may leave to
  the site.
- **A required choice whose options can all fill up** (`options_can_fill`): a warning at the field's options. Required
  means always or under a rule that is switched on. The options are the field's own, or a library list's that holds its
  options; options a source resolves have no places the form can know, and a choice with "Other" takes any text. A
  cascading choice counts all its options.
- **A limit that counts entries the form does not keep** (`limit_without_entries`): a warning. On a form that keeps no
  entries (`store: never`), its entry limit, one entry per member, its options' places and its unique answers each
  count only the entries whose actions still run. A field whose answers are kept only until the actions have run gives
  its places and unique answers back then (ADR-0090, ADR-0091). A form that keeps entries for days holds its limits
  while it keeps them.
- **A last outcome with a rule** (`last_outcome_has_rule`): a warning at that rule; a rule switched off is none.
- **A theme the site lacks** (`theme_missing`): a warning. The site has a theme in code (built in, or a site's own,
  ADR-0105) or in the library (`jfbTheme`), one lookup (`FormThemes`) that the import uses too, so an import no longer
  clears a built-in theme, which has no row.
- **Content, media and member groups the site lacks** (`content_missing`, `media_missing`, `member_group_missing`):
  warnings, since the form goes live and that part of it does nothing. An item the site lacks is gone or in the recycle
  bin. The form names content in its outcomes' redirects and in the settings of its fields' and actions' types whose
  editor picks content (`documentPicker`, `contentPicker`), media in its options' images and in the settings whose
  editor picks media (`mediaPicker`), lists and nested settings included, and member groups by key in who may fill it in
  and by name, in any letter case, in its rules' conditions. An option source's configuration is its kind's to check
  when the form is published, with errors (ADR-0075).
- **A redirect to a page that is not published** (`redirect_unpublished`): a warning, for a page in the tree that the
  published content does not hold in any culture. Visitors then get the outcome's message or the built-in thank-you
  message. A page published in some cultures is published; its address in each is Umbraco's to give.
- **A dictionary item without a text** (`dictionary_text_missing`): a warning. Every localised text of the form that is
  exactly `#key` is asked in each of the site's languages as its visitors see the form: the text of their culture,
  falling back as a request does, and the item's text through Umbraco's languages (ADR-0067). The warning is said at
  the culture's text and names the languages in which the item has none. The keys are read once per language. A merge
  tag of the dictionary renders empty and warns when it renders (ADR-0067), as an unknown tag does.
- **A host lock that replaces a stored value** (`lock_replaces_value`): a warning from the form check itself, which
  compares the stored definition with the host's designer locks of field and action settings (ADR-0060) before it
  applies them, at each setting whose stored value is neither the lock's nor, for a lock without a value, the setting's
  default. A hidden lock's setting is none, since the designer never shows it. The form's own settings take no host
  rule yet; the first that will, `ip: full` where the host does not allow it (13.3), joins this warning there.
- **A merge tag in a label from a source labels refuse** stays the merge-tag check's error (`source_not_for_place`,
  ADR-0118); 9.5e adds its test and the refused publish through the API.

## Consequences

- Every issue keeps the path of the element and setting it is about, so 9.5f can give each a "fix it".
- The checks that read the site run on every draft check and publish: the dictionary is read once per language, and
  only for a form with a dictionary text; the member groups once, and only for a form that names one; the tree and the
  published content per key, from Umbraco's in-memory navigation and cache.
- The word lists are heuristics and warn rather than refuse. A site's own field type that is personal by nature
  declares it once, and its forms get the hint.
- ROADMAP 13.3 gains the lock warning for a form that stores `ip: full` on a host that does not allow it.
