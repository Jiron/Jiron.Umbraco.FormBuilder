# 0043. The backoffice settings sections

- Status: accepted
- Date: 2026-10-24
- Roadmap step: 4.4b (decided and built); every step that adds a section or a field (4.5, 6.x, 7.x, 8.x, 13.x),
  6.1 (the first reader of the time zone), 12.4a (the screens and endpoints), 14.4 (the site of a request)

## Context

[ADR-0008](0008-settings-model.md) decided the backoffice layer: typed sections in `jfbSetting`, one row per scope and
section, global with per-site overrides field by field in `general` and `email`, defaults in code, schema versions with
migrations, host rules with three kinds of lock, a save pipeline, a cached reader with a loading token, and a payload
cache refresher. Step 4.3a built the repository, and the migration (4.1) wrote an empty global row of every section.
[ADR-0042](0042-host-configuration-and-start-up-checks.md) bound `LockedSections`. These questions are open:

- **Which sections exist now.** "Settings come with their feature": a field nothing reads is a fault the phase reviews
  look for. This step's own check needs the time zone of `general`.
- **What a field of a site row is,** and how a site sets a field back to its default rather than to the global value.
- **A field of a per-site section that every site shares.** Section 4.8 puts the draft cleanup in `general` "for every
  site at once" (4.5).
- **How a host rule is written** so that one mechanism shows a value as locked, replaces a value that a later lock
  refuses, and refuses a save, without a list of special cases in the save.
- **When the cache is cleared and `SettingsSaved` runs.** A save inside a caller's transaction must not clear caches or
  tell handlers about a change that rolls back.
- **The time zone's spelling.** A probe on this machine (.NET 10, Windows, ICU) corrects §3.4: the IANA lookup is
  case-sensitive, so a fresh process does not find `europe/berlin`, but the runtime's zone cache ignores case, so once
  `Europe/Berlin` has been looked up, every spelling of it resolves. `utc` resolves through the Windows name `UTC`,
  which is also an IANA id. ADR-0030 planned to store the zone as the server spells it, whatever an editor typed.

## Options

**When a save's effects run:**

1. **Scoped notifications** published inside the write's scope, which Umbraco delivers after the outermost commit
   (ADR-0007). Handlers then run in Umbraco's synchronous scope exit, so an asynchronous handler would have to block,
   and the cache handler must run before the public notification's handlers, an order nothing promises.
2. **A save is a unit of work of its own.** It refuses to run inside another unit or an Umbraco scope; when its write
   returns, it has committed, so it clears the cache, sends the instruction and awaits the notification. Tandem's proven
   order.

**Host rules:**

1. **A switch over the sections in the reader,** as Tandem did. Every section adds a case, and the save needs its own
   code for each restricted field.
2. **One rule per section in DI,** which takes a section and returns it with the host's rules applied, naming each lock.
   The save applies the same rule to what an editor sends: a field the rule changes is a value the host does not allow.

**The time zone's letter case:**

1. **Respell** a zone the server finds to its `TimeZoneInfo.Id` (ADR-0030's plan). Whether `europe/berlin` is found
   then depends on what the server looked up before, so the same save passes on one server and fails on another.
2. **Refuse every spelling but the server's,** on every server and platform. The screen offers the zones as a list
   (12.4a), so nobody types one.

## Decision

**Option 2 for all three.**

**Sections.** A section is an immutable record (`ISettingsSection`) whose property initializers are the defaults, with
a static alias, schema version and scope (global, or per site). This step builds `general` (schema version 1) with one
field, `timeZone` (default `UTC`), which this step's check needs; its first reader is availability and the rules'
"today" in 6.1, which gains a check that shows its effect. Every other field and section comes with the step that
builds what it configures, and adding a field with a default needs no migration.

**Storage** follows ADR-0008: camelCase JSON, enums as camelCase text (numbers refused), nulls left out, unknown
properties dropped when read. A global save writes every field the editor sent: the screen shows every value, so a
saved section holds what the editor saw. A row that cannot be read logs an error without its values, reads as the
defaults and keeps its version, so the next save repairs it. A row from a newer schema version (after a downgrade) is
read as it is. A missing migration step throws, naming the section and the version.

**Scopes.** The global scope, and a site by the key of its root content.

- A site row holds the **top-level fields** it overrides; a nested object is one field. A field whose value is `null`
  in the row overrides the global value with the default (a text then uses the built-in text), and a field the row
  leaves out follows the global value. A save names the fields the site overrides; none removes the row.
- `[GlobalOnly]` marks a field of a per-site section that every site shares: a site cannot override it, and a stored row
  that does is ignored for that field.
- Reading a global section for a site gives the global values. Whether a key names a site's root is the caller's check:
  12.4a's endpoints check it, and 14.4 removes a deleted root's rows.

**Effective values** are the code defaults, then the global row, then the site row, then the host rules, as ADR-0008
orders them. The reader returns the values with the version of the scope's own row (0 when it has none), the fields a
site overrides, and the locks by field path (`*` for the whole section), each with its kind and the configuration key.

**Host rules.**

- `LockedSections` locks a whole section in every scope, naming `JironFormBuilder:LockedSections:<index>`. The stored
  values stand and show read-only; every save of the section is refused.
- A section's other rules are an `ISettingsHostRule<T>`: it returns the section with the host's rules applied and names
  each lock. `Locked` pins a value the host sets, `Restricted` replaces a value the host refuses with the nearest
  allowed one, `Clamped` lowers a value to a host limit. The reader applies them where the value is used, so a lock
  added later takes effect at once and no row is rewritten.
- The rules of each section arrive with the host keys behind them (privacy permissions in 13.x, file caps in 7.x).

**Validation:** one `ISettingsValidator<T>` per section first brings values to their canonical form, then adds
`SettingsError(Field, Code, Message)` keyed `section.field`. The time zone must be an IANA zone the server resolves
(`CalendarValues.FindTimeZone`, the lookup "today" uses), spelled exactly as the server spells it (`TimeZoneInfo.Id`),
code `timeZone`. Errors on fields a site save does not write are left out, so a stale global value never blocks a
site's own save.

**Saving** one section and scope:

1. a locked section, or a field whose value a `Locked` or `Restricted` rule changes, is refused with
   `FormBuilderSettingLockedException` (`409 setting_locked`) naming the field and the key; a value a `Clamped` rule
   lowers is a validation error `range` naming the key;
2. validation errors are refused together (`FormBuilderSettingsValidationException`, `422 validation_failed`);
3. the row is written with the version the editor read; a stale version throws `FormBuilderConcurrencyException`
   (`409 concurrency_conflict`) and this server forgets its copy, so the answer can carry the current values;
4. the audit entry `settings.saved` (entity type `settings`, the site's key as the item) joins the write's unit with the
   section, the new version, the changed field paths (at most 200 named, all counted) and never a value;
5. after the commit this server forgets its copy and the payload cache refresher (id
   `b9ab1ec0-3f41-4c65-842b-339f59ee21c4`) tells the others;
6. `SettingsSavedNotification` (public) carries the section, the site, the version, the user and the changed field
   paths. A failing handler is logged and does not fail the save, which has committed.

**Caching:** each server keeps a snapshot per scope and section in Umbraco's runtime cache under
`Jiron.FormBuilder.Settings.<scope>.<section>`, for an hour. A read puts a loading token beside the key first and caches
its result only while the token is still there. A save clears its scope's key and token; a global save clears every
scope of the section, since each site's values rest on the global row.

## Consequences

- Each later section is a record, a validator, its migrations and its host rule, registered in the composer, with no
  change to the reader, the save or the cache.
- The time zone is the one setting of Phase 4 whose reader comes later: the 4.8 review names it, and 6.1 shows its
  effect.
- ADR-0008 is amended: a save is a unit of work of its own; a site row's fields are its top-level fields, with `null`
  meaning the default; `[GlobalOnly]`; host rules are applied by rule, with the code `range` for a clamped value.
- ADR-0030 is amended: a zone in another spelling is refused, not respelled; §3.4's Windows fact is corrected.
- Roadmap: 6.1 reads the site's time zone and tests it, counts a stored zone the server cannot resolve as UTC, and
  adds ADR-0008's allowlist test, since the zone is the first setting visitors see; 12.4a offers the zones as a list
  and checks that a site scope names a root; 14.4 removes a deleted root's rows; the 4.8 review names the time zone
  in its wiring sweep.
- **Revisit** if a section needs per-site values below the top level, or if Umbraco delivers scoped notifications
  asynchronously, which would allow saves inside a caller's transaction.
