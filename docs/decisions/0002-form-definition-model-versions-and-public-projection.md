# 0002. The form definition model, versions and the public projection

- Status: accepted; theme alias, calculation decimals, consent and card defaults, unsafe patterns and verification
  amended in 1.8; the save codes, unsupported properties and the outcome limit amended by
  [ADR-0034](0034-definition-model-reading-and-writing.md) in 4.2a; the publish codes and what the projection and its
  hash hold amended by [ADR-0035](0035-structural-validation-and-public-projection.md) in 4.2b; saves and publishes
  that change nothing amended by [ADR-0044](0044-form-service.md) in 4.5a; restore, pins, comments, the diff and the
  draft cleanup amended by [ADR-0045](0045-versions-comparison-and-draft-cleanup.md) in 4.5b; how copies find
  references, the envelope's limits and layout, `unsupported_format` and when an import creates option lists amended
  by [ADR-0046](0046-copies-templates-and-import.md) in 4.5c; the "Other" option, the empty lists of required parts
  and file types, and the check of every field's settings amended by [ADR-0056](0056-field-settings-checks.md) in 5.1b;
  the message kind `confirmation` and the effective public settings in the projection amended by
  [ADR-0062](0062-the-built-in-types-checks.md) in 5.2b
- Date: 2026-10-23
- Roadmap step: 1.7a (decided); built in 4.2a (the model, the schema, upgraders), 4.2b (structural validation, the
  projection), 4.5 (the form service), 5.x (the field types), 6.6 (the headless definition) and 12.5 (the mirror)

## Context

D5 makes a form definition a versioned, schema-versioned JSON document with one draft and many published versions,
and makes entries rows that point at the version they were submitted with. §4.3 sketches the document and asks
Phase 1 to fix its exact shape in an ADR; §4.3.1 sets the conventions (GUID keys, aliases, localised texts, limits,
the public projection and its revision); §4.3.2 lists the field types and their settings; §4.3.3 the rules and
validation; §4.3.4 the form settings. §4.5 stores versions in `jfbFormVersion` and the field catalogue in
`jfbFormField` (ADR-0003). ADR-0004 lets the runtime refresh a form after `form_changed`, and ADR-0005 fixes the
wire forms and the rule semantics both runtimes share.

Questions the sketch leaves open:

- **Identity:** the sketch puts `alias` and `name` inside the document, but `jfbForm` holds them too. A restored
  version would then rename the form, and a unique alias would have to be checked across drafts.
- **Rules per element:** each element has one `rule` with one effect, but a field often needs two ("shown when the
  contact method is phone or callback, required when it is phone").
- **Where limits live:** length and range appear both as type settings (§4.3.2) and as validation kinds (§4.3.3).
- **Option lists:** a list in the library, or options from content, change without the form being published. If
  they were part of the version, the public revision would change without a publish, and a list edit would need a
  publish of every form that uses it.
- **When to refuse:** §4.3.3 says saving refuses cycles and rules that point at missing fields, while §1.1's form
  check flags "a rule that points at a deleted field". Step 4.5 runs the structural validation at publish.
- **Revisions:** what "older but compatible" (§4.4, step 4) means, and how a sanitiser or dictionary change affects
  caches.

Ligata.Forms (§3.1) kept the editor's concurrency version apart from a visitor-facing revision that changed only
when the public content changed. That idea carries over.

## Options

**Identity:**

1. **Name and alias in the document.** Exports are self-contained, but restoring renames, and uniqueness spans
   drafts.
2. **Name and alias on the form row only.** The document is the form's content; the row is its identity in the
   library. Exports wrap the document in an envelope that carries the name and the alias.

**Rules per element:**

1. **One rule per element** (the sketch). Cannot express "shown when A, required when B".
2. **Named rule properties per effect** (a visibility rule, a required rule, a run rule). Explicit, but every new
   effect is a new property, and the rules overview has to know each one.
3. **A list of rules per element, each with its effect**, with a limit per effect and element.

**When to refuse:**

1. **Every save refuses every problem.** A draft can never be inconsistent, but an editor who deletes a field that
   a rule uses cannot save their work until they fix the rule.
2. **Saves refuse only what makes the document unreadable; publishing refuses every structural problem,** and the
   form check shows them in the meantime.

## Decision

Option 2 for identity, option 3 for rules, option 2 for refusing. The rest follows.

### The document

A definition is one JSON object. Property names are camelCase; enums are camelCase strings; keys are GUIDs in the
lowercase `d` format; Umbraco items (content, media, member groups) are referred to by their GUID keys.

| Property | Type | Notes |
| --- | --- | --- |
| `schemaVersion` | integer | `1`. Required. |
| `key` | GUID | The form's key, equal to `jfbForm.Key`. Required. |
| `settings` | object | The form settings below. Missing groups take their defaults. |
| `pages` | array | At least one page. |
| `validationRules` | array | Form-level checks across fields. |
| `actions` | array | In run order. |
| `outcomes` | array | In order; the first whose rule matches is used. |

**The form's name, alias and folder live on `jfbForm`, not in the document.** Renaming and moving act at once and
are audited; restoring a version never renames the form. The form alias follows the field alias pattern below and
is unique among all forms, trashed ones included, so a restore from the trash cannot collide.

**Localised text** (`LocalizedText`): an object from culture to text, or `null` for no text. The key `""` is the
default and is required whenever the object has any entry; other keys are culture names (`de`, `de-CH`,
`sr-Latn-RS`), matched case-insensitively. A request resolves its culture, then the neutral culture, then `""`.
A value that is exactly `#` followed by a dictionary key (letters, digits, `.`, `_` and `-`) resolves through
Umbraco's dictionary; when no such item exists, the text shows as written and the form check warns. `LocalizedHtml`
has the same shape and holds rich text, stored as the editor wrote it and passed through the one sanitiser on every
output (§4.10). Plain texts that render on one line (labels, titles, placeholders, button labels) take at most 500
characters and no line breaks, other plain texts 2,000 characters, rich texts 50,000.

**Page:**

| Property | Type | Default |
| --- | --- | --- |
| `key` | GUID | required |
| `title` | LocalizedText | null |
| `description` | LocalizedHtml | null |
| `rules` | Rule[] | `show` or `hide`, at most one |
| `groups` | Group[] | at least one |

**Group:**

| Property | Type | Default |
| --- | --- | --- |
| `key` | GUID | required |
| `title` | LocalizedText | null |
| `description` | LocalizedHtml | null |
| `columns` | 1 to 4 | 1 |
| `style` | `plain` or `card` | `plain` |
| `rules` | Rule[] | `show` or `hide`, at most one |
| `fields` | Field[] | may be empty |

**Field.** The common properties; a type declares which of them it supports (§4.3.2), and a property the type does
not support keeps its default (ADR-0034: booleans and numbers are always written).

| Property | Type | Default | Notes |
| --- | --- | --- | --- |
| `key` | GUID | required | Entries, rules and actions refer to the field by this key. |
| `type` | string | required | The type's alias in the registry. |
| `alias` | string | required for types that hold a value, absent for the others | `^[a-z][a-zA-Z0-9]{0,63}$`, unique in the form (repeater children included), not reserved (§4.3.1). |
| `formerAliases` | string[] | empty | Aliases the field had in published versions; merge tags outside the form (Razor views, a developer's code) still resolve them (§4.3.1). |
| `span` | 1 to the group's columns | 1 | |
| `label` | LocalizedText | null | A field that holds a value needs one before it can be published. |
| `hideLabel` | boolean | false | The label stays for screen readers. |
| `help`, `placeholder` | LocalizedText | null | |
| `required` | boolean | false | |
| `messages` | object | empty | A custom message per error kind of the type (`required`, `invalid`, `minLength`, `maxLength`, `minWords`, `maxWords`, `min`, `max`, `step`, `dateWindow`, `selections`, `fileType`, `fileSize`, `fileCount`, `full`, `confirmation`). |
| `defaultValue` | string, string[] or null | null | Merge tags allowed (ADR-0014). A list for types with several answers. |
| `prefill` | object or null | null | `{ "query": name or null, "member": property alias or null, "memberMode": "show" or "hide" }`. |
| `sensitive` | boolean | false | §4.10. |
| `includeInEmails` | boolean | true | False leaves the field out of `entry.answers` in mail (ADR-0014); a tag that names the field still renders it. |
| `keep` | `always` or `untilActionsRun` | `always` | §4.4. |
| `autocomplete` | string | null | An HTML autocomplete token. |
| `cssClass` | string | null | |
| `validation` | Check[] | empty | Checks the type does not own, below. |
| `rules` | Rule[] | empty | `show` or `hide` at most one, and `require` at most one. |
| `options` | Options | null | Choice types only. |
| `rows` | Row[] | | The grid only: `{ "key", "label", "required" }`. |
| `fields` | Field[] | | The repeater only: its child fields, no repeater among them. |
| `settings` | object | empty | The type's own settings, validated by the type. |

**Limits a type owns stay in its `settings`** (lengths, ranges, steps, date windows, selections, file rules), and
their errors take the custom text from `messages`. **`validation` holds only the checks no type owns:**

- `{ "kind": "pattern", "pattern": "...", "message": LocalizedText }`, run on the matcher of ADR-0005;
- `{ "kind": "matchesField", "field": key, "message": LocalizedText }`;
- `{ "kind": "uniquePerForm", "message": LocalizedText }`.

Each check's message is its own; there is one place for each fact.

**Options:**

| Property | Type | Notes |
| --- | --- | --- |
| `source` | `items`, `list`, `content`, `dataType`, `url`, `file`, `countries` or `code` | `items` are the field's own; `list` is a library option list; the other kinds are configured on the field with the same configuration a library list of that kind has (`jfbOptionList.Config`). |
| `items` | Option[] | For `items`: `{ "value", "label", "description", "image", "places", "parentValues" }`. `value` is what entries store (1 to 200 characters, unique in the field); `image` a media key; `places` the number of places (§4.4); `parentValues` the answers of the parent field this option belongs to. |
| `list` | GUID | For `list`. |
| `config` | object | For the other kinds; validated by the kind. |
| `parent` | GUID or null | The choice field whose answer filters this one (cascading). |

An option's `value` is generated from its label and can be edited until a published version has entries; after
that, a change asks first, as an alias does.

**Rule:**

```jsonc
{ "key": "guid", "effect": "show", "enabled": true, "match": "all", "conditions": [
    { "field": "guid", "op": "equals", "value": "phone" },
    { "context": "culture", "op": "startsWith", "value": "de" },
    { "match": "any", "conditions": [ /* a nested group */ ] } ] }
```

- **Effects:** `show` and `hide` (pages, groups and fields), `require` (fields that hold a value), `run` (actions:
  the action runs only when the rule matches) and `use` (outcomes). Each element takes at most one rule per effect,
  and `show` and `hide` exclude each other.
- **Conditions** name fields by key. Operators, operands (`"x"`, `["x", "y"]`, `{ "field": key }`,
  `{ "today": "+2d" }`) and context subjects are those of §4.3.3 and ADR-0005; the shared test vectors write fields by
  alias for readability, and the runners map them.
- **A form-level validation rule** is `{ "key", "enabled", "field": key, "message": LocalizedText, "match",
  "conditions" }`: when its conditions match, the field shows the message as an error.

**Action:** `{ "key", "type", "name", "enabled": true, "trigger": "submitted", "stopOnFailure": false, "rules", "settings" }`.
`trigger` is `submitted`, `approved`, `rejected` or `verified`; `rules` holds at most one `run` rule; `settings` belong
to the action type. Secrets appear only as `$JironFormBuilder:Secrets:<Name>` references (§4.10).

**Outcome:** `{ "key", "kind": "message", "message": LocalizedHtml, "showAnswers": false, "redirect": null, "rules" }`.

- `kind` is `message` or `redirect`.
- `redirect` is `{ "content": key or null, "url": string or null, "delaySeconds": 0 }`. A message with a redirect
  after some seconds is a `message` outcome with a redirect.
- **A redirect URL** is site-relative or absolute `https`. Its scheme and host are always the editor's own: merge
  tags may appear only after the host, and their values are encoded (ADR-0014), so no visitor can steer a redirect to
  another site.
- `rules` holds at most one `use` rule. When no outcome applies, the built-in thank-you message in the visitor's
  language is shown, and the form check warns that the last outcome should have no rule.

**Form settings** (the groups follow §4.3.4; `null` means "inherit the site setting" or "off", as noted):

```jsonc
"settings": {
  "submission": {
    "submitLabel": null, "nextLabel": null, "backLabel": null,   // LocalizedText; null = the default label
    "summaryPage": false,
    "progress": "steps",                                         // steps | bar | none
    "store": "always", "storeDays": null,                        // always | never (until the actions are final) | days
    "approval": "automatic",                                     // automatic | manual
    "verification": null,                                        // { "emailField": key, "days": 7 } turns double opt-in on
    "resume": null,                                              // { "days": 30 } turns save and resume on
    "entryTitle": null,                                          // a merge-tag template; sensitive fields render empty
    "entryReference": null                                       // { "prefix": "REQ-", "year": true, "digits": 5 }
  },
  "availability": {
    "opensAt": null, "closesAt": null,                           // instants, ISO 8601 in UTC
    "maxEntries": null, "closedMessage": null,                   // LocalizedHtml
    "members": "anyone", "memberGroups": [],                     // anyone | members | groups
    "onePerMember": false
  },
  "protection": {
    "level": "inherit",                                          // inherit | standard | proofOfWork | captcha
    "captcha": null,                                             // { "provider": alias, "settings": {} }
    "contentRules": "inherit"                                    // inherit | off | normal | strict
  },
  "privacy": {
    "retentionDays": { "submitted": null, "approved": null, "rejected": null, "spam": null },
    "ip": "inherit",                                             // inherit | none | anonymised | full
    "insights": true
  },
  "appearance": {
    "theme": null,                                               // a theme alias; null = the site's default
    "requiredMarker": "inherit",                                 // inherit | required | optional | none
    "validationSummary": true, "choiceLayout": "vertical", "cssClass": null
  },
  "developer": { "headless": false, "queryValues": [], "contextValues": [] }
}
```

Email verification is the `verification` setting only, never an action: its mail is a system mail (ADR-0007). While
the entry is `Unverified` no action runs; confirming makes it `Submitted` and queues its `submitted` and `verified`
actions together, in the order shown. "Unique per form on a chosen field" shows under Availability in the designer and
is stored as that field's `uniquePerForm` check. ADR-0012 fixes the protection levels and ADR-0009 the privacy values;
their shape here is theirs to refine before v1 is fixed (below).

**The built-in types' settings** (§4.3.2). Numbers that answers are compared with (min, max, step) are wire
strings (ADR-0005); counts are JSON integers; relative dates use the operand form `{ "today": "+2d" }`.

| Type | `settings` (default) |
| --- | --- |
| `text` | `minLength` (0), `maxLength` (255), `prefix`, `suffix` (LocalizedText), `counter` (false) |
| `textarea` | `rows` (4), `autoGrow` (true), `minLength` (0), `maxLength` (4000), `minWords`, `maxWords`, `counter` (false) |
| `email` | `confirm` (false), `confirmLabel`, `domains` (`{ "mode": "any", "list": [] }`, mode `any`, `allow` or `block`) |
| `phone` | `defaultCountry` (ISO 3166 alpha-2 or null) |
| `url` | `schemes` (`["https"]`; `http` may be added) |
| `number` | `min`, `max`, `step`, `decimals` (0), `prefix`, `suffix` |
| `date` | `min`, `max` (a date or a relative operand), `weekdaysOnly` (false), `excluded` (dates), `defaultToday` (false) |
| `time` | `min`, `max`, `stepMinutes` (null) |
| `datetime` | the date and time settings combined |
| `rating` | `count` (5, 3 to 10), `symbol` (`star`, `heart`, `circle`), `lowLabel`, `highLabel` |
| `scale` | `from` (0 or 1), `to` (10, 5 to 10), `lowLabel`, `highLabel` |
| `slider` | `min` ("0"), `max` ("100"), `step` ("1"), `unit`, `showValue` (true) |
| `calculation` | `formula`, `format` (`{ "decimals", "prefix", "suffix" }`; `decimals` 0 to 15, 2 when unset), `visible` (true) |
| `dropdown` | `prompt`, `multiple` (false), `minSelections`, `maxSelections` |
| `radio` | `layout` (`inherit`, `vertical`, `horizontal`, `cards`: a card per option, with the option's image when it has one), `other` (false), `otherLabel` (the type's own label when not given) |
| `checkboxes` | `layout`, `minSelections`, `maxSelections`, `other`, `otherLabel` |
| `grid` | `multiple` (false); columns come from `options` or `scale` (`{ "from", "to" }`); rows from `rows`. A required grid requires every row; otherwise each row's `required` decides. |
| `ranking` | `top` (null = rank all) |
| `checkbox` | `text` (LocalizedHtml beside the box) |
| `yesno` | `yesLabel`, `noLabel`, `style` (`buttons` or `switch`) |
| `consent` | `text` (LocalizedHtml, required). `required` defaults to true for this type. No `defaultValue`: a consent is never ticked in advance (§3.7). |
| `country` | `allowed` (alpha-2 codes; empty = all), `top`, `default` |
| `name` | `parts` (`title`, `first`, `middle`, `last`), `requiredParts` (none: every part asked for) |
| `address` | `parts` (`street2`, `region`, `country`), `requiredParts` (none: the street, the postcode and the city) |
| `file` | `types` (`images`, `pdf`, `office`, `text`, `archives`; with none and no extensions, images and PDFs), `extensions`, `maxBytes`, `maxFiles` (1), `dropZone` (true), `thumbnails` (true), `camera` (false) |
| `signature` | `width`, `height` |
| `repeater` | `minRows` (0), `maxRows` (10), `addLabel`, `removeLabel` |
| `hidden` | none: its value is `defaultValue` or `prefill.query` |
| `heading` | `text` (LocalizedText), `level` (2 to 4) |
| `paragraph` | `html` (LocalizedHtml) |
| `divider` | none |
| `image` | `media` (key), `alt` (LocalizedText, required), `size` (`small`, `medium`, `large`, `full`) |
| `captcha` | none |

A type's `settings` belong to it: publishing holds them to its settings schema and to the type's own check
(ADR-0056), and a type reads older shapes of its own settings itself.

### Reading and writing

- **C#:** immutable records in Core (`FormDefinition`, `FormPage`, `FormGroup`, `FormField`, `FormRule` and so on),
  read and written with System.Text.Json source generation. A type's settings travel as a JSON object and are bound
  to its settings class by the type.
- **Strict reading:** unknown properties, numbers where a string belongs, enum values as numbers and a property
  named twice are refused with the JSON path (ADR-0034 has the steps and every code). A new property, a new enum value or a new effect therefore needs a new
  `schemaVersion`, so an older site never drops part of a newer document without noticing.
- **The canonical form:** properties in declaration order, `null` and empty arrays and objects left out, booleans and
  numbers always written, no indentation, and the keys of `LocalizedText` and of settings objects sorted ordinally
  (`""` first). `DefinitionHash` is the SHA-256 of its UTF-8 bytes, in lowercase hex.

### Refusing

- **Every save** (draft, import, template, mirror file) refuses what makes the document unreadable: invalid JSON,
  unknown and missing properties, a property named twice, wrong types, invalid keys, two elements with one key, an
  unknown or newer `schemaVersion`, the size limits (2 MB, the text lengths above) and the element limits of §4.3.1
  with 50 outcomes per form. Codes: `invalid_json`, `unknown_property`, `missing_property`, `duplicate_property`,
  `invalid_type`, `invalid_value`, `duplicate_key`, `unsupported_schema_version`, `limit_exceeded` (ADR-0034). An
  import refuses an envelope of another `format` first, with `unsupported_format` (ADR-0046).
- **Publishing** also runs the structural validation of 4.2b and refuses every problem it finds: aliases, names and
  option values (`alias_invalid`, `alias_reserved`, `alias_duplicate`, and so on), references (`unknown_field`,
  `invalid_reference`, `unknown_type`), cycles in the graph of rules and containment over fields, groups and pages
  (`cycle`), the condition checks both runtimes run (`unknown_operator`, `operator_not_for_kind`, `invalid_operand`,
  `unsafe_pattern`, ADR-0005), the last also for the `pattern` checks of a field's `validation` list, what a field
  type does not support, missing and misplaced parts, rule effects, and redirects that could leave the site.
  ADR-0035 has every code and where it points. The form check
  (9.5b) adds its errors (a missing label, an email action without an email field, a text that recalls a later field,
  a consent text with a merge tag other than `form`, `page`, `site` or `dictionary`) and its warnings, which never
  block.
- **A draft keeps** what the editor did, and the form check lists every problem with a "fix it" link. Deleting a
  field that rules use says so and can be undone; it does not force the editor to rewrite the rules first. Drafts
  reach visitors only through the preview, which runs both runtimes' checks first and shows a broken rule as an issue
  instead of evaluating it.

### Versions

- **Every save that changes the definition appends a row to `jfbFormVersion`:** `Number` counts up per form; `Kind`
  is `Draft` or `Published`. A save whose definition is the draft's, and a publish whose draft is the live version,
  write nothing (ADR-0044).
- **Publishing** appends a `Published` row with the draft's definition, and in one transaction: upgrades the document
  to the current `schemaVersion`, validates it, updates the field catalogue (new fields get rows, or their draft-only
  row gets its `FirstVersionNumber`; removed ones get `LastVersionNumber`, ADR-0003), sets `PublishedVersionId`, and
  raises `FormPublishing` (cancelable) and `FormPublished`. A cache refresher tells every server.
- **The draft** stays the latest `Draft` row. "Unpublished changes" means the draft's `DefinitionHash` differs from the
  published one's.
- **Unpublishing** clears `PublishedVersionId`; the versions and entries stay.
- **Restoring** a version copies its definition into a new draft row; publishing it is a separate step. "Restored from
  version N" is in the audit entry and the save notifications, not in the row (ADR-0045).
- **Pinning and comments:** a version can carry a comment and be pinned; neither changes the form's
  `ConcurrencyVersion` (ADR-0045). The retention job (ADR-0007) keeps every published version, every pinned one and
  every one an entry points at (preview entries point at drafts), plus the form's draft, its newest 50 draft saves and
  the draft saves of the last 90 days, both set in `general.history` (ADR-0045).
- **Concurrency:** every change to a form (save, publish, restore, rename, move) raises `jfbForm.ConcurrencyVersion`. A save
  sends the value it read; a stale one gets `409 concurrency_conflict` with the current state.
- **Diff** matches elements by key and reports elements added, removed and moved, and properties changed, per culture
  for texts (ADR-0045 has the details).

### Schema versions and upgraders

- A stored version is never rewritten. Reading it runs the upgraders from its `schemaVersion` to the current one in
  memory (`IFormDefinitionUpgrader`, JSON in and JSON out, one per step), and the result is cached.
- Saving and publishing always write the current `schemaVersion`. Imports and mirror files of any older version are
  upgraded; a newer one is refused with `unsupported_schema_version`.
- Tests keep a stored definition of every released schema version and upgrade it (D21, ADR-0010).
- **v1 is fixed at the first prerelease** (18.4). Until then, a step that needs another property changes this ADR,
  the JSON schema and the projection's allowlist in the same commit, and says so in WORKLOG. From then on, every
  change is a new `schemaVersion` with an upgrader.

### The public projection

- **Built from an allowlist,** never by removing internal parts: a separate set of public types holds each property
  that may reach a visitor, so a property added to the definition stays private until it is added there. A test
  serialises fully populated definitions (property-style, 4.2b) and compares the projection with the allowlist.
- **It contains:** the form's key and alias, its public revision, and the culture's resolved texts; the settings
  visitors need (labels, the summary page, progress, the required marker, the validation summary, the theme, whether
  save and resume is on, whether the form is open and its closed message, whether it is for members, and what the
  protection needs: the challenge, proof of work, the captcha provider, its site key and the consent mode, and the
  honeypot's name for this revision); per consent field, the `jfbConsentText` hash of its wording for the requested
  culture and content (ADR-0009); the pages, groups and fields with their public properties and the public part of
  each type's settings (the type declares it), each with its effective value, the field's own or else the type's
  default (ADR-0062); switched-on rules; checks; switched-on form-level validation rules;
  and the allowed query values. The projection of a definition holds what the definition decides; the form endpoint
  adds the alias, the revision, whether the form is open, the protection data and the consent hashes outside
  `PublicHash` (ADR-0035).
- **It never contains:** actions, outcomes (the submission's answer carries the outcome, §4.6), recipients,
  `sensitive`, `includeInEmails`, `keep`, `formerAliases`, option-source configuration, member prefill mappings (only
  the fact that a field is prefilled from the member), the entry title and reference, storage, approval, retention,
  IP handling, insights, context values, switched-off rules, the hidden field's default value (resolved when the
  entry is submitted), or anything a type marks private.
- **Options:** a field's own `items` are part of the projection, without their `places` (places left are per moment,
  and come with the challenge, §4.6). Options from a library list or another source are resolved when the form is
  rendered or asked for (`GET forms/{key}/options/{fieldKey}`), cached by the source's `CacheSeconds`, and are not
  part of the version. Editing a list therefore needs no publish, and an entry stores the option's value.
- **Merge tags in visitor texts** keep their `field.*` tags for answer recall; everything else is resolved before the
  text reaches the projection or the page (ADR-0014).
- **The public revision** (`jfbForm.PublicRevision`, and `PublicRevision` on each published version row) starts at 1
  and goes up by one when a publish produces a projection whose `PublicHash` differs from the last published one's.
  `PublicHash` is the SHA-256 of the canonical projection over all cultures, built from the stored texts before the
  sanitiser and the dictionary, so a package update or a dictionary edit never changes it.
- **The ETag** of `GET forms/{keyOrAlias}` is a hash of the response itself, so it also changes when the dictionary
  or the sanitiser changes what the visitor gets.
- **"Older but compatible"** (§4.4, step 4): a submission against an older revision is validated against the current
  published version. When every error falls on a field that the posted revision also had, the answer is the usual
  `422 validation_failed`. When an error falls on a field the visitor never saw, when a field the visitor answered no
  longer exists, or when a consent text the visitor agreed to has changed (by the hash of the wording shown, which the
  form posts, so a change through the dictionary, a page property or a link counts too), the answer is `form_changed`,
  and the runtime refreshes the form (ADR-0004). The server loads the posted revision's projection to decide.

### Exports, templates and mirror files

- **The export envelope** (`jfb-form-export/1`):
  `{ "format", "packageVersion", "exportedUtc", "form": { "key", "alias", "name" }, "definition", "optionLists" }`.
  It includes the library option lists the form uses, so an import can reuse them, or create them from 5.5 on. Content,
  media and member groups are named only by key, and themes by alias; an import clears a theme the site lacks, and
  the form check shows the content, media and member groups it lacks. The file is indented and ASCII-escaped, and at
  most four times the definition limit (ADR-0046).
- **Import** creates a new form with new keys (4.5c), or, from 12.5 on, when the form's key exists and the user
  chooses it, a new draft of that form.
- **Duplicating** gives every element a new key and remaps every reference (rules, checks, option parents, the
  verification field, and type settings that name an element by key: ADR-0046 finds them by a walk over the
  canonical JSON). **Copying actions to another form** maps the fields they name by alias and lists the ones it
  cannot map, which publishing refuses until they are fixed.
- **Templates** (`jfbFormTemplate`): a form template holds a definition, a group template one group; both are
  inserted as copies with new keys. Built-in templates ship as embedded envelopes.
- **Mirror files** (D25) are envelopes of the published definition with `"published": { "number", "hash" }` added,
  written to `<alias>.json`; the hash is what conflict detection compares (12.5).

### What a definition never holds

Secrets (only references), internal database ids, member data, entry data, or the site's own address. Redirect URLs
and texts are the editor's.

## Consequences

- One document shape serves the designer, versions, diffs, exports, templates and mirror files; the field catalogue
  and the entries never depend on how a field looked in an older version (D5).
- A form can need two rules on one field, and later effects arrive as new effect values (with a schema version)
  rather than new properties.
- Editors never lose a draft to a broken rule; visitors never see one, because publishing refuses it.
- Library option lists and dynamic sources change without publishing, and the public revision moves only when
  visitors would see something different, so cached pages and in-progress visitors are disturbed as little as
  possible.
- The strict reader makes every format change explicit, at the price of a schema version for each new property after
  the first prerelease.
- Roadmap: §4.3 shows this shape; §4.3.1 notes the form's name and alias on the row and the projection's options;
  §4.3.3 says that publishing refuses structural problems while a draft keeps them; §4.5 gains
  `jfbFormVersion.PublicRevision`; §4.6 takes the ETag from the response (WORKLOG).
- **Revisit** if editors need more than one rule per effect on one element, or if the strict reader turns out to
  block real upgrade paths (a newer development site exporting to an older live site).
