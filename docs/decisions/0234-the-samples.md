# 0234. The samples: one project, each sample turned on by its name

- Status: accepted; builds on [ADR-0233](0233-the-extension-surface.md) (the extension points the samples use), and
  extends [ADR-0142](0142-the-captcha-providers.md) (a site's own captcha script) and [ADR-0006](0006-file-storage-and-uploads.md)
  (the blob store, tested against the emulator)
- Date: 2026-11-11
- Roadmap step: 15.1 (the sample extension of part 15.1, and the samples of part 15.2)

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
   Its swatches on the website are SVG circles whose fill is the value, so no style attribute meets a strict content
   security policy.
7. **What the samples showed the extension surface lacked,** added to it in the same part:
   - `FieldTypeDeclaration.GroupsControls`: a site's type whose control is a group (the colour's radio buttons) is
     labelled by a fieldset's legend and marks each control at an error, as the built-in choices; before, every site's
     type had one `<label for>`, which names only its first control;
   - `FormBuilderCaptchaClient.ScriptGlobal`, which the challenge carries as `global`: the global object a site's own
     captcha script defines with the calls the built-in providers' scripts have (`render`, `reset`, and optionally
     `ready` and `execute`); the runtime knew only `hcaptcha` and `grecaptcha`. A provider's `Client` is read for every
     challenge, so the arithmetic sample gives each page a sum of its own, sealed with Data Protection, and needs no
     endpoint;
   - the control writer of `<jfb-form>` gets the theme's classes as a third argument (`JfbThemeClasses`), and its field
     `choiceErrorId` and `isDisabled`, so a site's control looks and reads as its Razor view;
   - a Razor class library whose only MVC code is its views is not among the application parts a site lists for itself:
     the colour sample adds its assembly as one, and the guide says so.
8. **The blob store's contract tests** run against Azurite's blob service, a development tool (MIT) in `scripts/`, which
   CI installs before the .NET tests: the fixture starts it in memory on a port the system assigns (section 4.11 allows
   that), without telemetry, for an account whose key it makes for the run, and stops its process tree after. The
   tests that every store passes moved into `FileStoreContract`, which the physical store's fixture runs with every
   check it had; each store adds its own. Azurite brings `uri-templates` 0.2.0, whose licence field is the address of
   tv4's public-domain statement: a dedication ADR-0027 accepts in development tools, read as such by the licence check.

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
