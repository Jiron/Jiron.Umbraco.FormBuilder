# 0008. The settings model

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 1.7a (decided); built in 4.4 (the framework), each section in the step that builds its service, the
  screens in Phase 9 and later

## Context

§4.8 has two layers: host configuration (`appsettings.json` → `JironFormBuilder`), which is for developers and locks
and wins, and typed backoffice sections in `jfbSetting`, global with per-site overrides where marked. Every setting
has a default, validation, help text, a docs entry and a test, and shows "locked by configuration" when the host locks
it. A change reaches every server at once (§4.8), and the website never sees more than it needs (§4.3.1, D13).

§1.3 asks for several sites in one installation, each with its own sender address, base URL and defaults. A form's
own settings (ADR-0002) often say "inherit", so the site's settings are the defaults of every form on it.

Tandem (Tandem ADR-0008, as built in its 3.3) proved typed sections, schema versions per section, host rules with
three kinds of lock, a cache refresher with a loading token, and an IntelliSense schema that Umbraco merges into the
site's. Ligata's earlier settings were one JSON blob with sentinel strings and no schema version, which made every
change a migration of everything.

## Options

1. **One JSON document per scope.** Simple, but every save rewrites everything, two editors on unrelated tabs
   conflict, and every change migrates the whole document.
2. **Typed sections, one row per scope and section,** each with its schema version and an optimistic version; host
   options bound to typed classes. Saves stay small and per tab, conflicts are rare and visible, and each section
   migrates on its own.
3. **An Umbraco document type for settings.** Editors know the screens, but settings would mix with content, appear
   in the content APIs and the published cache, and need custom property editors for nearly everything.

## Decision

**Option 2.**

**The host layer** (`JironFormBuilder`):

- One options class tree, bound once, with defaults applied after binding and one `IValidateOptions` validator run at
  start-up (`ValidateOnStart`). It collects every failure, and each message names the configuration key and what is
  allowed, so an invalid configuration stops start-up with a sentence a developer can act on. Lists start empty,
  because the binder appends to lists that already hold items, and defaults fill them afterwards.
- It holds what belongs to the host or to security (§4.8): the public API's base path, allowed origins and server
  keys; rendering switches; file roots and hard caps; privacy permissions (`AllowFullIp`, `AllowRetentionOff`); captcha
  keys; webhook hosts; the pause switch; secrets; the definition mirror; limits; designer locks; `LockedSections`.
- **The IntelliSense schema** `appsettings-schema.Jiron.Umbraco.FormBuilder.json` is generated from the options
  classes and their XML docs by a test that fails when the committed file is stale. Its `$ref`s resolve within the
  file itself (Tandem 11.1b). The Core package ships it with a `buildTransitive` props file that adds it to
  `UmbracoJsonSchemaFiles`, so Umbraco's build merges it into the site's schema (4.4, 18.1).

**The backoffice layer:**

- **Sections**, one immutable C# record each, with a static alias and schema version: `general`, `email`,
  `protection`, `files`, `privacy` and `integrations` (§4.8). Each section's record arrives with the service it
  configures ("Settings come with their feature").
- **Access** (which user groups hold which permissions) is stored in `jfbPermission` (ADR-0013), not in a section,
  and follows the same rules for locks, validation, conflicts and the audit.
- **Scopes:** `global`, and a site, which is a root content node keyed by its GUID. `general` and `email` can be
  overridden per site field by field: a site row holds only the fields it overrides, and the screen shows the others
  as inherited, with a way back to inheriting. The other sections are global.
- **The site of a request** is the root of the content the form is shown on or submitted from (`contentKey`). Without
  content (a headless call without `contentKey`, an entry created in code) the global values apply.
- **Storage:** each record is camelCase JSON in `jfbSetting (Scope, Section)` with `SchemaVersion`, `Version`,
  `UpdatedUtc` and `UpdatedByUserKey`. Enums are camelCase text (numbers are refused, so a stored value never changes
  meaning when an enum grows), and nulls are left out.
- **Defaults** live in code. A missing or `null` property means "use the default", and for texts "use the built-in
  text in the visitor's language", never a sentinel string. Unknown properties are dropped when a row is read, so a
  row written by a newer version still loads after a downgrade. A row that cannot be read at all logs an error, reads
  as the defaults and keeps its version, and the next save repairs it; forms keep working.

**Effective values,** in order: the code default, the global row, the site row, then the host rules. For a form
setting that says `inherit` (ADR-0002), the effective site value is its default.

- **The host rules:**
  - feature switches can only switch things off;
  - host limits clamp backoffice values and the form limits of §4.3.1;
  - `LockedSections` (whole sections) and the specific locks make values read-only.
- **Three kinds of lock**, each reported with the configuration key behind it:
  - `Locked`: the field is fixed (a locked section, a host-set value);
  - `Restricted`: some values are refused (`ip: full` while `AllowFullIp` is false, retention off while
    `AllowRetentionOff` is false);
  - `Clamped`: values above a host limit are lowered to it.
- **A restriction that arrives later** (a host locks a value that forms or sections already store) is applied where
  the value is used: the stored value is replaced by the nearest allowed one (`full` becomes `anonymised`), the
  screen shows the lock, and the form check says so. Nothing is rewritten in the database.
- **Designer locks** (`JironFormBuilder:Designer:SettingLocks`) fix or hide a setting of a field type or an action
  type in every form: `{ "kind": "action", "type": "email", "setting": "from", "value": "noreply@example.com", "hidden": true }`,
  with `kind` `field` or `action`. The designer shows a locked setting read-only (or not at all when hidden), the
  management API refuses another value with `409 setting_locked`, and the runtime uses the locked value whatever a
  stored definition says.
- The management API returns, per field, the effective value, whether and how it is locked, and the key that locks
  it. The screen shows a locked field read-only with that key (§4.13, "Safe by design").

**Validation:**

- One validator per section. Errors are `SettingsError(Field, Code, Message)` keyed `section.field` (and
  `section.list[2]` for items), returned as ProblemDetails `validation_failed` with `errors`.
- Stable codes for the client: `required`, `range`, `maxLength`, `maxCount`, `enum`, `singleLine`, `pattern`,
  `email`, `url`, `path`, `alias`, `culture`, `timeZone` (an IANA id the server can resolve, ADR-0005), `cidr`,
  `domain` and `secretReference`.
- The client mirrors the simple constraints (required, length, range, enum) from a JSON schema generated from the
  records, so most errors show before saving; cross-field rules run on the server, which decides.

**Saving** (one section and scope at a time, in one scope):

1. refuse a change to a `Locked` field, a locked section or a refused value of a `Restricted` field
   (`409 setting_locked`, naming the field and the key);
2. validate (`400 validation_failed`);
3. write with the `Version` the client read (`409 concurrency_conflict` with the current values, the same code as
   for forms);
4. clear this server's cached copy and send the cache instruction;
5. write the audit entry `settings.saved` with the section, scope, version and changed field paths, never values;
6. publish `SettingsSaved` with the changed field paths.

**Schema versions:** each section has a current `SchemaVersion` and an ordered list of migrations (JSON in, JSON
out). An old row is migrated when it is read and written back in the new shape at its next save. A missing step
throws, naming the section and the version. The upgrade test (16.5) reads a migrated row.

**Caching across servers:**

- Each server keeps the effective settings per scope and section in Umbraco's runtime cache, keyed
  `Jiron.FormBuilder.Settings.<scope>.<section>`.
- A read first stores a loading token beside the key and caches its row only if the token is still there afterwards,
  so a read that raced a save cannot put the old value back.
- A payload cache refresher (its own GUID, ADR-0001) clears one section and scope, or everything; other servers apply
  it on their next sync, usually within seconds and at worst about twice `TimeBetweenSyncOperations`. The saving
  server applies it at once.

**What the website sees:** the public projection and the rendered markup carry only allowlisted settings: default
button labels, the theme, the required marker and what the protection needs (the captcha provider, its site key and
the consent mode). A test fills every section and checks the public output against the allowlist, in the step that
first exposes a setting to visitors.

**Secrets never live in settings.** A setting may hold a reference (`$JironFormBuilder:Secrets:<Name>`), resolved
from configuration when it is used; the screen shows only whether it resolves.

**Per-site values:**

- **The base URL** (`general`) is the only source of links in mail and of absolute URLs the server builds (§4.10). By
  default it is the site's first `https` domain in Umbraco, otherwise `Umbraco:CMS:WebRouting:UmbracoApplicationUrl`,
  and never the request's Host header. A health check warns when a site has neither.
- **The time zone** (`general`) is an IANA id; saving refuses one the server cannot resolve (ADR-0005).
- **The sender** (`email`) is a name and an address per site, never taken from a visitor (§4.10).

**User settings** (`jfbUserSetting`) are a backoffice user's own choices (columns, filters, the digest). They are
validated per name and have no scopes, locks or audit, because they change nothing for anyone else.

**Documentation:** a reference of every host key and backoffice field is generated from the records and options
classes, and a test fails when `docs/configuration.md` is stale (17.x).

## Consequences

- Each settings tab saves one section, so the save bar, conflicts and validation errors stay local to that tab.
- Adding a field with a default needs no migration; renaming or restructuring one needs a section migration and its
  test.
- A site's own sender, base URL, time zone and defaults are one override row each, and forms that inherit follow
  them without being republished.
- Host locks are honest: the screen says which key locks a value, and a lock added later takes effect at once
  without rewriting stored rows.
- The "only switch off" rule, the public allowlist test and the generated reference are written once in 4.4 and
  guard every later section.
- Roadmap: §4.8 says that Access is stored in `jfbPermission`, which sections are per site, how the site of a request
  is found, where the base URL comes from, and the shape of designer locks (WORKLOG).
- **Revisit** if sites need per-site values in sections that are global now (privacy per country, for example), or
  if Umbraco offers a package settings store with locks and distribution.
