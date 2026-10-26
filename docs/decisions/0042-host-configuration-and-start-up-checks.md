# 0042. Host configuration and start-up checks

- Status: accepted; the designer locks of field settings checked and applied in 5.1f
  ([ADR-0060](0060-designer-locks-of-field-settings.md))
- Date: 2026-10-24
- Roadmap step: 4.4a (decided and built); 4.4b (the backoffice sections and their host rules), 4.5 (reading stored
  definitions), 5.1 and 8.1 (applying designer locks), every step that adds a host key, 17.x (the configuration
  reference), 18.1 (the package)

## Context

[ADR-0008](0008-settings-model.md) decided the host layer: one options tree for the `JironFormBuilder` section,
bound once and validated when the site starts, with every message naming the key and what is allowed, and an
IntelliSense schema generated from the options and shipped in the Core package.
[ADR-0005](0005-shared-logic-and-test-vectors.md) requires ICU, and [ADR-0030](0030-porting-the-shared-logic.md)
takes time zones by their IANA names only. Step 4.3f bound `JironFormBuilder:Files` on its own
([ADR-0041](0041-file-store-audit-log-and-daily-stats.md)), and [ADR-0034](0034-definition-model-reading-and-writing.md)
left the definition limits for 4.4 to bind. These questions are open:

- **How much of section 4.8's tree exists now.** Settings come with their feature, and a key nothing reads is a fault
  the phase reviews look for.
- **A host that lowers a definition limit.** The reader applies the size and element limits to every document it
  reads, so forms stored under the old limit would stop reading, and the website would lose them.
- **How far a host may raise a limit.** A definition is read whole into memory on every request that uses it.
- **Misspelt keys.** The configuration binder ignores a key it does not know, so `LockedSection` or `Limit` silently
  leaves the default. For a lock, that is a security fault nobody notices.
- **Designer locks.** Section 4.8 gives their shape, but the field types and action types they name arrive in 5.1 and
  8.1.
- **Invariant globalization.** This step started the demo with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`: Umbraco
  installed its database and failed only when Kestrel built the middleware, because its request localisation asked for
  `en-US`. The message names neither the setting nor what to change.
- **Changes while the site runs.** `appsettings.json` reloads on change by default.

## Options

**Lowered limits:**

1. **The site's limits everywhere,** for reads and saves. A lowered limit takes forms off the website at once.
2. **The site's limits for documents from outside and for saves; the highest limits a host may set for what the site
   stored.** A stored form keeps working, and saving it again is refused until it fits.
3. **Trimming stored definitions** to the new limits. It changes editors' work behind their backs.

**Unknown keys:**

1. **Ignore them,** as the binder does.
2. **The binder's `ErrorOnUnknownConfiguration`.** It stops at the first unknown key with the binder's own message,
   and the validator never runs.
3. **A walk over the configuration in the validator,** matching every key against the options tree. Every unknown key
   is listed in the same start-up failure as the other problems, with the keys allowed where it stands.

**Where the globalization check runs:**

1. **In the composer,** the earliest point, before Umbraco touches its database. A composer cannot take services, so
   a test could not stand in for the runtime, and an exception there reads as a failed composition.
2. **As a start-up validation,** the same mechanism as the options: `Host.StartAsync` runs it before any hosted
   service, so before Kestrel and the request localisation that failed in the probe.
3. **In a hosted service.** Hosted services start in registration order, after Kestrel's, so it would come too late.

## Decision

**Option 2 for lowered limits, option 3 for unknown keys, option 2 for the globalization check.**

**The tree** is `FormBuilderOptions`, bound once from `JironFormBuilder` with `ValidateOnStart`. In this step it holds:

- `Files`: `FormBuilderFilesOptions` of 4.3f, now part of the tree;
- `Limits:Definitions`: `DefinitionLimits` itself, which the serializer and the save checks already take;
- `Designer:SettingLocks`;
- `LockedSections`.

The other keys of section 4.8 come with the steps that build what they configure. Each such step adds its key to the
tree, the validator, the schema and its tests. Services take `IOptions<FormBuilderOptions>`, so the host configuration
is read once: a change takes effect when the site restarts. A reload would skip the start-up validation, and roots and
limits must not change under a running site.

**Validation:** one `IValidateOptions<FormBuilderOptions>` collects every failure. Each message names the key, with
its list index, and what is allowed.

- **Unknown keys.** Every key under `JironFormBuilder` is matched against the tree, ignoring case as configuration
  does. An unknown key fails, and its message lists the keys allowed at its level. A value where a section belongs, or
  a section where a value belongs, fails too. A dictionary (the secrets, later) takes any name.
- **`Files:Root`** follows ADR-0041's rule.
- **`Limits:Definitions`:** each limit is at least 1 and at most ten times its default (`DefinitionLimits.Ceiling`):
  - 20 MiB;
  - 300 pages, 1,000 groups, 3,000 fields;
  - 10,000 options per field;
  - 500 actions, 500 outcomes, 2,000 rules.

  Ten times leaves room for unusual forms and still bounds what one read costs.
- **`Designer:SettingLocks`**, in section 4.8's shape:
  - `kind` is `field` or `action`, in any case;
  - `type` is a letter, then letters, digits, `.`, `-` and `_`, at most 64 characters;
  - `setting` is a lowercase letter, then letters and digits, at most 64 characters: a setting at the top of the type's
    settings;
  - `value` is text, at most 50,000 characters (the longest text ADR-0002 allows). Without it, the setting keeps its
    default;
  - `hidden` hides the setting in the designer.

  There is one lock per kind, type and setting. Whether the type and the setting exist, and whether the value fits the
  setting, the field type framework (5.1) and the action framework (8.1) check when the site starts. Those steps also
  apply the locks: read-only or hidden in the designer, `409 setting_locked` for a save with another value, and the
  locked value at run time. **Amended in 5.1f (ADR-0060):** the type and the setting are compared exactly, a value is
  read by the setting's kind (JSON text for a list or an object), a required setting cannot be locked without a value,
  and no message repeats a value.
- **`LockedSections`** names sections of ADR-0008 (`general`, `email`, `protection`, `files`, `privacy`,
  `integrations`) in any case. A section can be locked before its record exists.

**Lowered limits.** `DefinitionLimits` is a singleton taken from the tree:

- documents from outside (the management API, imports) and the save checks use the site's limits, so a lowered limit
  refuses a definition that the default allows;
- `FormDefinitionSerializer.ReadStored` reads what the site stored with `DefinitionLimits.Ceiling`. A form saved under
  a higher limit keeps reading, rendering and opening in the designer, and saving or publishing it is refused until it
  fits. The form service (4.5) reads its rows this way.

**Globalization.** `GlobalizationFacts` reads three facts of the runtime:

- **culture data:** `de-DE` exists and writes its decimals with a comma;
- **Unicode normalisation:** NFC composes `e` and U+0301 into one character;
- **IANA time zones:** `America/New_York` resolves as an IANA zone through `CalendarValues.FindTimeZone`, the lookup
  that "today" uses.

A start-up validation of an options type of its own (`FormBuilderStartupChecks`) fails when a fact is missing. Its
message names what is missing and what to change:

- remove `InvariantGlobalization` from the project, or set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` to false;
- keep ICU on (`System.Globalization.UseNls` off);
- on Linux, install the ICU and tzdata packages.

Umbraco's boot (its database install and migrations) runs before the check, which does no harm. The facts are a
service, so a test can stand in for the runtime.

**Testing start-up:**

- Unit tests build the services and run `IStartupValidator`, as the host does.
- A `startup` project in the browser tests runs the built demo in real processes: once with an invalid key and once
  with `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`. Each host gets an empty connection string, so Umbraco boots to its
  installer state without a database. The test expects the process to exit with the message.

**The IntelliSense schema** follows ADR-0008:

- a unit test generates it from `FormBuilderOptions` and Core's XML documentation, with each value's default and the
  enums as camelCase text;
- its `$ref`s are rebased under `JironFormBuilder`, and a second test resolves every `$ref` of the committed file within
  that file;
- `JFB_UPDATE_SCHEMA=1` rewrites the file;
- the Core package ships it at its root, `buildTransitive/Jiron.Umbraco.FormBuilder.Core.props` adds it to
  `UmbracoJsonSchemaFiles`, and a `!` exception in `.gitignore` keeps it tracked.

## Consequences

- A misspelt key, a limit out of range or a runtime without ICU stops the site with one message that lists everything
  to fix. Nothing in the host configuration is silently ignored.
- Lowering a limit never takes a form off the website; editors find out when they save.
- ADR-0034 (stored definitions read with the ceilings) and ADR-0041 (the files options are part of the tree) are
  amended.
- **Revisit** if hosts need limits beyond ten times the defaults, or settings that change without a restart.
