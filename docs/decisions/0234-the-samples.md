# 0234. The samples: one project, each sample turned on by its name

- Status: accepted; builds on [ADR-0233](0233-the-extension-surface.md) (the extension points the samples use)
- Date: 2026-11-11
- Roadmap step: 15.1 (the sample extension of part 15.1, and the project part 15.2 fills)

## Context

Part 15.1 asks for a sample extension that registers a custom setting editor and a field preview, shown working in a
browser; part 15.2 asks for a sample of each kind of extension, compiled and tested in CI and documented. The samples
must run in the browser tests' host, the demo, without changing what every other spec of that host sees: a spam check,
a file scanner or a file store of a sample would change how every entry is stored, and a workspace view of a sample
would stand beside every form's views. They are also what a site copies, so they should read like a site's own code.

## Decision

1. **One project, `samples/Jiron.Umbraco.FormBuilder.Samples`,** a Razor class library like a site's own (its views
   compiled into the assembly, `wwwroot` served from the site root, so its backoffice files live under
   `/App_Plugins/JironFormBuilderSamples/`), referencing the meta package as a site does. It is never packed.
2. **No sample registers itself.** The project has no composer; `builder.AddFormBuilderSample(name)` turns one sample on,
   once however often it is named, and refuses a name no sample has. A site writes the same lines in its own composer,
   or copies the sample. The demo turns on the samples its setting `Demo:Samples` names, none by default, so
   `scripts/demo.ps1` shows Jiron Form Builder as a site gets it; the browser tests' host names `colour` and
   `embedView`, which change nothing another spec reads.
3. **A sample's backoffice manifests come from code** (`SampleManifests`, an `IPackageManifestReader` that Umbraco reads
   beside every `umbraco-package.json`), and only for the samples turned on, so an editor, a preview or a view comes with
   its server part and never without it. The documentation shows the `umbraco-package.json` a site usually writes
   instead. The samples' words come once, in English and German, from one `localization` manifest each.
4. **Plain JavaScript modules,** loaded by the backoffice as they are, with Lit and Umbraco's mixins from the
   backoffice's import map: the least a site needs, without a build. They are typed with JSDoc against Umbraco's types
   and the declarations Jiron Form Builder ships, and the client's `npm run typecheck` checks them
   (`tsconfig.samples.json`), so a change of the declarations that breaks a sample breaks the build.
5. **Tested where CI already tests:** the samples' unit tests are in `Tests.Unit` (the colour type's checks, validation
   and display; the manifests), their browser tests in the `backoffice` project (`extending.spec.ts`).
6. **The colour sample** keeps its colours in one setting of the kind `Json`, a list of `{ name, value }`, because a
   list setting holds only plain values and a colour needs a name a visitor reads: a hash code is no accessible name.
   A value is `#` and six lower-case hexadecimal digits, as a colour input gives it, which is also all that reaches a
   style; a name is one text for every language, at most 40 characters; a field offers 1 to 24 colours, each value once.
   The answer is the colour's value, shown as "Blue (#1f6feb)" in mail, merge tags and exports. An empty list is no
   setting at all, so the field would hold the default colours again: the editor never removes the last colour.

## Alternatives considered

- **An `umbraco-package.json` per sample:** Umbraco loads every such file it finds, so the demo and the tests' host
  would show the embed view on every form and load editors for types that are off.
- **A composer per sample behind a setting:** the samples would read the demo's settings, which no site's code does.
- **A test project of the samples' own:** a second test pipeline in CI and in `ci-local.ps1` for what the existing
  projects run already.
- **TypeScript with a build:** a build step a site copying a sample does not need; the JSDoc types give the same check.

## Consequences

- The tests' host has a sample field type in its palette and an Embed view on every form; specs pick by name, so none
  that ran before reads them.
- A site copies a sample's C#, view and script, and lists its manifests in its own `umbraco-package.json`.
- Part 15.2 adds its samples to the same project, each behind its name.
