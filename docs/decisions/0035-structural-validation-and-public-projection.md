# 0035. Publishing's structural validation and the public projection in code

- Status: accepted; amends [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (the publish codes,
  what the projection holds and what its hash covers)
- Date: 2026-10-23
- Roadmap step: 4.2b (decided and built); 4.5 calls it when a form is published, 5.1 moves the field-type traits into
  the registry, 6.6 serves the projection, 7.2 and 8.1 add provider and action types

## Context

ADR-0002 lets a draft keep problems and makes publishing refuse them. It names some codes (`alias_*`, `unknown_field`,
`unknown_type`, `cycle` and the condition codes of ADR-0005) and says what the public projection may and may not hold.
Step 4.2b builds both on the model of ADR-0034. Building them raised questions ADR-0002 leaves open:

- **What a field type supports.** §4.3.2 lists each type's settings and the common properties that apply to it, but
  the field-type registry arrives only in 5.1. ADR-0034 keeps an unsupported property at its default and leaves the
  refusal to 4.2b.
- **Codes.** ADR-0002's list has no code for:
  - a missing part (a choice field without options, a grid without rows);
  - a misplaced field (a captcha inside a repeatable group, a second captcha);
  - a reference to the wrong kind of thing;
  - an effect an element does not take;
  - an option value that no answer could match;
  - a redirect that leaves the site.
- **What a cycle is.** Rules make pages, groups and fields depend on each other, and so does containment: a field in a
  hidden group is hidden too. Some rules decide nothing about visibility: `require`, and any rule that is switched off.
- **Redirects.** §4.3.4 lets an outcome redirect to a URL with merge tags. A merge tag in the host would let a
  visitor's answer choose the site.
- **Letter case and folding.** SQL Server compares aliases without case and SQLite with it, and entry filters compare
  folded text (ADR-0033).
- **The public types.** ADR-0002 wants a separate set of public types. Some leaf types are the same in both sets:
  texts, default values, conditions, checks and messages.
- **What the hash covers.** Some things change without a publish: the alias, and whether the form is open. The
  revision is the hash's own result.

## Options

**Where the type knowledge lives until 5.1:**

1. **Checks per type alias, written into the validation.** Quick now, but 5.1 has to rewrite them.
2. **A traits record per type.** It says what the type holds, how rules compare it, which properties and messages it
   has and which settings are public. The built-in table sits in one place, and 5.1's registry supplies the same record
   for every type, built-in or custom.

**The cycle graph:**

1. **Rule edges between fields only.** This misses "show group G when field F, inside G, has an answer".
2. **Rule edges plus containment edges,** over pages, groups and fields.

**The public types:**

1. **A public twin for every record, leaf types included.** The most code, and nothing shared.
2. **Public twins for every container, with the leaf types shared.** The containers are the form, its settings, page,
   group, field, options, option, grid row, rule and validation rule. The shared leaf types are those whose properties
   are all public by nature. A property added to a container stays private until its twin gets it. A property added to
   a shared leaf type fails the allowlist test until the test lists it.

## Decision

Option 2 for all three. In detail:

### Field-type traits

`FieldTypeTraits` (internal) holds:

- `HoldsValue`;
- `Kind`: how rules compare the type's answers, as one of ADR-0005's kinds. It is null for a name, an address, a
  repeatable group and a grid itself; a grid's rows are choices.
- `MultipleSetting`: whether `multiple: true` makes a dropdown, or a grid row, hold several choices;
- `Features`: label, hidden label, help, placeholder, required, default value, prefill, autocomplete, the three checks,
  show and hide rules, options, required options, places, rows and child fields;
- `Messages`: the error kinds of §4.3.3;
- `Single`: at most one per form and never inside a repeatable group (the captcha placement);
- `ServerOnlyDefault`: the default is resolved when the entry is submitted and never reaches the page (the hidden
  field, whose default may use member and context values, ADR-0014);
- `PublicSettings`.

How the traits are used:

- `FieldTypeCatalog.BuiltIn` holds the 33 types of §4.3.2.
- A type's public settings are those a browser needs to show and check the field. An email field's allowed and blocked
  domains are the site's policy, so they stay on the server, which checks them.
- `FieldTypeSupportTests` restates §4.3.2 apart from the code. For each type it covers every common property, check,
  rule and message the type does not support.
- 5.1's registry builds the same traits from each type's declaration. The validation and the projection then change
  only where they get the traits from.

### Publishing

`CheckPublish` runs the save checks first: limits, unique keys and ranges (ADR-0034). When they pass, it runs the
structural validation. Like a read, it reports at most 100 problems, each with a code, a JSON path and a message.

It never throws for a definition the save checks accept. A property-style test publishes 500 generated definitions
and resolves every reported path in the document.

| Code | Raised for | At |
| --- | --- | --- |
| `unknown_type` | A field type the site does not have | `…type` |
| `property_not_supported` | A non-default value of a property, check, message or rule effect the type does not support; places on options of a type without places; member groups when the form is not for some member groups; a captcha provider below the captcha level; a message, the answers or a delay on a redirect outcome | The property |
| `part_missing` | A form without a page; a page without a group; a choice field without options; own options without an item; a library source without its list; a grid without rows; a repeatable group without fields; "some member groups" without a group; the captcha level without a provider; a redirect outcome without its redirect, or a redirect without a target | Where the part belongs |
| `field_not_allowed` | A repeatable group or a captcha inside a repeatable group; a second captcha | `…type` |
| `alias_invalid` | A field that holds a value without an alias, or with one outside `^[a-z][a-zA-Z0-9]{0,63}$` | `…alias`, `…formerAliases[n]` |
| `alias_reserved` | `form`, `entry`, `page`, `member`, `query`, `context`, `site`, `date` or `ufprt`, in any letter case | The same |
| `alias_duplicate` | An alias or former alias already used in the form, repeatable-group children included, in any letter case | The same |
| `name_invalid` | An allowed query value or a context value outside `[A-Za-z0-9_-]{1,64}` | The item |
| `name_duplicate` | One of those named twice, in any letter case | The item |
| `option_value_invalid` | An option value that is empty once cleaned, or that cleaning would change (spaces at its ends, not in NFC) | `…items[n].value` |
| `option_value_duplicate` | Two values of one field that are equal once cleaned and folded as rules compare text (ADR-0005) | The later one |
| `options_invalid` | Own items next to a library list or another source; a list or a configuration the source does not use; parent values without a parent | The property |
| `unknown_field` | A reference that names no field holding a value: a condition's field or operand, a `matchesField` check, a validation rule's field, an option parent, email verification. Also a condition on a field that rules cannot compare, and a query context the form does not allow: the shared logic knows neither, and this is its code | The reference |
| `invalid_reference` | A reference to the wrong thing: the field itself, a `matchesField` check on a field whose answers cannot be compared, a field whose value the server sets in a rule or check the browser runs (see "Which runtime looks"), a parent without options, a parent value the parent lacks, verification by anything but an email field outside repeatable groups, or a prefill from a query value the form does not allow | The reference |
| `invalid_condition`, `unknown_operator`, `operator_not_for_kind`, `invalid_operand`, `unsafe_pattern` | ADR-0005's checks, run by the shared logic itself (`Rules.Check`) once a condition's references hold; `unsafe_pattern` also for `pattern` checks | The condition, `…op`, `…value` or `…context` |
| `rule_effect_invalid` | An effect the element does not take: pages and groups take `show` or `hide`; fields take what their type supports; actions take `run`; outcomes take `use` | `…effect` |
| `rule_duplicate` | Two rules with one effect on one element, or a `show` and a `hide` rule together | The later rule |
| `cycle` | See "Cycles" | The first rule in the cycle |
| `invalid_redirect` | See "Redirects", and a redirect to both a content page and a URL | `…redirect` or `…redirect.url` |

**References.** A reference to a field whose type the site lacks is not reported, and neither is one to a row of such
a field. Only `unknown_type` at the field itself is, because one cause should give one problem. Installing the type,
or changing the field's type, brings out any problem that remains.

**Which runtime looks.** The rules of pages, groups and fields, form-level validation rules and `matchesField`
checks run in the visitor's browser as well as on the server (ADR-0005). The browser never gets the value of a field
whose type keeps its default on the server (the hidden field), so it would see that field as empty while the server
sees its value: a page the browser skips could be one the server checks, and the entry could never be sent. Those
rules and checks therefore may not look at such a field (`invalid_reference`). The rules of actions and outcomes run
only on the server, so they may. A condition on the query value or the member looks at the same source from both
runtimes.

**Cycles:**

- **Nodes:** every page, group and field, repeatable-group children included, in document order. A grid row is shown
  with its grid.
- **Containment edges:** a field depends on its group, a group on its page, and a child on its repeatable group.
- **Rule edges:** an element with a switched-on `show` or `hide` rule depends on every field its conditions name, as
  the subject or as an operand.
- **What adds no edge:** `require`, `run` and `use` rules decide no visibility, and a switched-off rule decides nothing.
  So "B is required when A has an answer, and A when B has one" publishes.
- **How a cycle is found and reported:**
  - the strongly connected parts are found with Tarjan's algorithm, written without recursion so that a large form
    cannot exhaust the stack;
  - a part with more than one node, or a node that depends on itself, is one `cycle` problem;
  - it is reported at the first rule of the part in document order, and its message names each element.
- **Option parents:** a field whose options are filtered by its own answer, however many fields lie between, is a
  `cycle` at the first `parent` of the circle in document order. Each field has at most one parent, so one walk from
  each field that no earlier walk reached finds every circle once, in time linear in the number of fields; a field
  that leads into a circle without being part of it is not reported.

**Redirects.** A redirect URL is either a path on the site or an `https` address:

- **A path on the site** starts with one `/`. It never starts with `//`, `/%2F` or `/%5C`, in any letter case, since
  browsers or later decoding treat those as another host. The character after the first `/` is the editor's own text,
  never a merge tag: `/{{ a }}/{{ b }}` with an empty `a` would render as `//` followed by a host the visitor chose.
- **An `https` address** has a host name (labels of letters, digits and hyphens, 63 characters each and 253 in all) or
  an IPv4 address. It may have a port from 1 to 65535, with no leading zero. It never names a user or a password, and
  it never has an IPv6 literal.
- **Merge tags** (`{{…}}`) appear only after the host, so the address always leads where the editor chose. Their values
  are encoded as URI components (ADR-0014).
- **Outside merge tags,** there is no whitespace, no control character and no backslash, and every `%` starts an escape
  of two hexadecimal digits.
- **When the outcome runs (6.1),** the rendered URL is checked again by the same rule. A URL that fails it is not
  followed: the visitor gets the default thank-you message, and the site logs a warning without the answers.

**Outcomes.** A `redirect` outcome has a redirect, shows no message and no answers, and leaves at once, with no delay.
A `message` outcome may redirect after its message, with a delay. A redirect names a content page or a URL, never both.

### The public projection

**Types.** The public types live in `Jiron.Umbraco.FormBuilder.Core.Definitions.Projection`, with XML docs:

- `PublicForm` and `PublicFormSettings`;
- `PublicPage`, `PublicGroup` and `PublicField`;
- `PublicPrefill`, `PublicOptions`, `PublicOption` and `PublicGridRow`;
- `PublicRule` and `PublicValidationRule`.

They share the leaf types `LocalizedText`, `LocalizedHtml`, `FieldDefaultValue`, `RuleItem`, `FieldCheck` and
`FieldMessages`. The projection is written by the definition's canonical writer (ADR-0034) through its own
source-generated context.

**What it holds.** These are the parts of the definition that ADR-0002 lists:

- **The settings,** flat:
  - the submit, next and back labels, the summary page and the progress indicator;
  - whether save and resume is on, but not for how long;
  - the closed message, and whether the form is for members, but not which groups;
  - the protection level and, at the captcha level, the provider, whose keys are site settings;
  - the theme, the required marker, the validation summary, the choice layout and the CSS class;
  - the allowed query values.
- **Pages, groups and fields** with their public properties, and each field type's public settings only.
- **A field's own options** without their places, which change from moment to moment and come with the challenge
  (§4.6). Options from a library list or another source say only that they come from one (`fromSource`), with no list
  key or configuration.
- **A prefill** gives its query value's name, whether it comes from the member (`fromMember`) and the member mode. It
  never gives the member property.
- **Rules and form-level validation rules** that are switched on, without the `enabled` flag. A switched-off rule is
  left out, so switching one off or on is a public change.

**What it never holds:**

- a hidden field's default value;
- any setting or default value of a field whose type the site lacks, because the projection cannot know which of them
  are public;
- actions and outcomes;
- `sensitive`, `includeInEmails`, `keep` and `formerAliases`;
- any form setting outside the list above.

**`PublicHash`** is the SHA-256 of the projection's canonical bytes, over every culture, taken from the stored texts.
The form endpoint adds the rest of what ADR-0002 lists, outside the hash:

- the alias, which lives on the form's row, and the public revision, which the hash moves;
- whether the form is open now;
- the protection data: the challenge, proof of work, the captcha's site key and consent mode, and the honeypot's name;
- the consent texts' hashes;
- the texts resolved for the request's culture.

None of these needs a new revision.

**Tests:**

- a property-style test fills 500 generated definitions with a sentinel in every private place (action settings,
  recipients, list keys, source configuration, member properties, private settings, switched-off rules) and finds it
  in no projection;
- a test lists every property name the projection may write and finds no other;
- the full fixture projects exactly to a reviewed file (`v1-full.public.json`);
- 33 private changes leave `PublicHash` as it was, and 12 public changes move it.

### Deferred, with the step that does it

- **Unknown action types and their settings (8.1).** Publishing refuses an action type the site lacks with
  `unknown_type` at `…actions[n].type`, and the type validates its own settings.
- **Unknown captcha providers (7.2).** These give `unknown_type` at `settings.protection.captcha.provider`.
- **A field type's own settings (5.1, 5.4).** Each type validates them at publish, for example that a minimum is not
  above the maximum.
- **How deeply condition groups nest (5.2).** This is the shared logic's check, so publishing gets it through
  `Rules.Check`.
- **The references and cycles of calculations (5.3).**
- **The rendered redirect's second check (6.1).**
- **What the form check adds (9.5b),** such as a missing label, merge tags that recall a later field, and warnings.

## Consequences

- Publishing can tell the editor everything that blocks it at once. Each problem has a code the backoffice translates
  and a path the designer can point at, while drafts keep every problem (ADR-0002).
- The type table exists twice, in the traits and in their test. A change to §4.3.2 needs both, which is the point.
  5.1 replaces the built-in table with the registry, and the test then runs against the registry.
- Switching a rule off is a public change, so it moves the public revision.
- An unknown type stays quiet about everything that depends on it, so its true problems show only once it has a type.
- ADR-0002 is amended: its status line, "Refusing" and "The public projection". The roadmap gains notes in 5.1, 7.2
  and 8.1 (WORKLOG).
- **Revisit** if an extension type needs a feature the traits cannot say, or if visitors need an option's places in
  the projection rather than with the challenge.
