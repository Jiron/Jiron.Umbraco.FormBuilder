# 0060. Designer locks of field settings

- Status: accepted; builds on [ADR-0008](0008-settings-model.md) (designer locks),
  [ADR-0042](0042-host-configuration-and-start-up-checks.md) (their shape), [ADR-0055](0055-field-type-framework.md)
  and [ADR-0056](0056-field-settings-checks.md) (settings schemas and their rules); the form endpoint's validator
  built in 6.1a ([ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md))
- Date: 2026-10-26
- Roadmap step: 5.1f (locks of kind `field`); 8.1 applies the locks of kind `action` the same way; a value from
  before a lock follows the field's key whatever its type there, since 9.4d
  ([ADR-0174](0174-changing-a-fields-type-in-the-designer.md))

## Context

A host fixes a field type's setting in every form with `JironFormBuilder:Designer:SettingLocks`, for example
`{ "kind": "field", "type": "text", "setting": "maxLength", "value": "100" }`, and `hidden` takes the setting out of
the designer. ADR-0042 checks a lock's shape when the site starts. This step checks it against the site's field types,
marks it in the schema the designer reads, refuses a save that gives the setting another value, and makes the site use
the locked value. Four questions came up:

1. **What a lock's value is.** Configuration holds text: JSON's `12` arrives as `"12"` and `true` as `"True"`. A list
   or an object cannot be a value at all, because configuration reads it as a section, which ADR-0042 refuses where a
   value belongs.
2. **Where a lock takes effect.** Forms hold values from before a lock came. Section 4.8 says a lock added later
   applies where the value is used, without rewriting stored rows.
3. **Which saves are refused.** The designer shows a locked setting read-only and sends back the draft it read, value
   and all. A form that held another value before the lock came would never save again.
4. **What the designer is told,** and what a hidden lock gives away.

## Options

1. **Reading the value.**
   - JSON for every kind: `"value": "\"noreply@example.com\""`, awkward for the texts most locks hold.
   - **By the setting's kind:** a text, a number, a date, a time, a choice or a key as it is; `true` or `false` in any
     letter case; a whole number as digits; a localised text as the text of every culture; a list, an object or JSON
     as JSON text. Chosen.
2. **Where a lock takes effect.**
   - Rewriting stored definitions, at start-up or on the next save: section 4.8 rules it out, and lifting a lock would
     not give editors their values back.
   - **Where values are used:** what the site runs on gets a copy with the locks applied, and stored definitions keep
     the editors' values, which come back when the host lifts the lock. Chosen.
3. **Which saves are refused.**
   - Any value other than the lock's: a form saved before the lock could not be saved again.
   - **A change:** a draft save refuses a locked setting whose value is neither the lock's (or the default, for a lock
     without a value) nor the value the stored draft holds for the same field with the same type. A setting the field
     leaves out is always fine. Chosen.
4. **What the designer gets.**
   - Leaving hidden settings out of the schema: the designer could no longer tell a hidden setting from an unknown one
     in a draft.
   - **Every setting, with a lock mark:** read-only with the locked value, or hidden without it. Chosen.

## Decision

**The locks.** `FieldSettingLocks` (internal, in `Core/FieldTypes`, a singleton) resolves the locks of kind `field`
against the registry when it is first used. Each lock names its configuration key
(`JironFormBuilder:Designer:SettingLocks:<index>`), the setting's descriptor, its value as settings store it (none
when the lock keeps the default) and whether it is hidden. Locks of kind `action` wait for 8.1.

**The value** is read by the setting's kind, then held to the setting's kind and rules as a field's own value would be
(ADR-0056):

| Setting kind | The lock's value |
| --- | --- |
| `boolean` | `true` or `false`, in any letter case |
| `wholeNumber` | digits, with a minus if need be |
| `number`, `text`, `date`, `time`, `choice`, `key` | the text as it is (a number in the wire form of ADR-0005) |
| `localizedText`, `localizedHtml` | the text, for every culture (the default text `""`) |
| `list`, `nested`, `json` | JSON text, such as `["pdf","png"]` |

A value that settings leave out (an empty list or object, or JSON's `null`) counts as no value.

**At start-up,** `FieldSettingLockStartupCheck` refuses a lock whose type the site lacks, whose setting the type lacks
(both compared exactly, with the right spelling suggested for another letter case), whose value the setting refuses,
or that keeps a required setting without a value. Each message names the lock's key and what the setting takes, never
the value: it may be an address or a reference that belongs in the configuration only (ADR-0042). The check leaves
locks that the options' own validation refuses, and a registry that the field types' check refuses, to those checks.

**Applying a lock** gives a field of the type the locked value, or leaves the setting out so that its default applies
when the lock has no value; child fields of a repeatable group are fields too. A definition that no lock touches stays
the same instance. The locks apply in two places:

- **The published form the website runs** (`IFormBuilderFormService.GetPublishedAsync`): the published version read,
  upgraded and with the locks applied; null for a missing form, one in the trash and one that is not published. The
  public API and the pipeline of 6.1 read it, with a cache of their own.
- **Publishing:** every publish check runs over the draft with the locks applied, so a combination that only the lock
  makes wrong (a locked maximum below a field's minimum) is found before it goes live. What is stored is the draft as
  the editor saved it.

They do not apply to the draft the designer reads, to versions and their comparison, to exports, or to the stored
`PublicHash`: those show what editors stored.

**Saving.** `SaveDraftAsync` checks the locks after the site's limits and before anything is written. The first locked
setting in document order whose value is neither the lock's (the setting's default, for a lock without a value) nor the
one the stored draft holds for the same field key (and, until 9.4d, type: ADR-0174) refuses the save with `FormBuilderSettingLockedException`:
`409 setting_locked`, naming the setting's path, such as `$.pages[0].groups[0].fields[1].settings.maxLength`, and the
lock's key. Values compare as their kind does: `"2.50"` is `2.5`, and a date or a time as its value. Every other write
keeps what it carries: a new form's defaults, templates, copies, imports and restored versions are not values an editor
gives, an import brings a whole form from elsewhere that a refusal would stop moving between sites whose locks differ,
and the site uses the locked value wherever it runs.

**The schema.** Every setting at the top of a type's settings in the field types endpoint carries `lock`: null, or
`{ hidden, configurationKey, value }`, where `value` is the locked value as settings store it, null when the lock keeps
the default, and always null for a hidden lock.

## Consequences

- Lifting a lock gives every form its stored value back; nothing was rewritten.
- 6.1 caches what `GetPublishedAsync` answers. The public revision moves with the stored projection, so a lock that a
  restart brings changes what visitors get without a new revision: the public definition's cache validators carry a
  fingerprint of the locks too.
  **Built in 6.1a ([ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md)):** the validator is a hash of the
  answer's bytes, which hold the locked values, so it moves with the locks without a fingerprint of its own.
- 9.4 shows a locked setting read-only with its key, never a hidden one, and its change-type preview treats a locked
  setting of the target type as set by the lock: neither carried over nor needed.
- 8.1 checks and applies the locks of kind `action` against the action types in the same way.
- A setting nested in an object cannot be locked on its own; a lock fixes the whole object.
