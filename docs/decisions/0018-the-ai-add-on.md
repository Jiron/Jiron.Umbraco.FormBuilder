# 0018. The AI add-on: drafting, translating and suggesting on Umbraco.AI, for editors only

- Status: accepted; builds on [ADR-0001](0001-solution-structure-and-packages.md) (the add-on's package and its shared
  internals) and [ADR-0233](0233-the-extension-surface.md) (the designer's extension points, which this adds two to)
- Date: 2026-11-11
- Roadmap step: 15.1 (part 15.3, the AI add-on); D22, D23

## Context

Part 15.3 asks for an optional package, `Jiron.Umbraco.FormBuilder.AI`, that helps editors in three ways: a draft of a
new form from a description or a pasted questionnaire, a translation of a form's texts into another language with a
preview, and suggestions for help texts and accessibility. D23 keeps the core packages free of any Umbraco.AI reference
and keeps entry data away from any AI; D22 allows no real key during the build. The information architecture already
places the features: "Draft a form with AI…" in the library's create menu, "Translate with AI…" in the "…" of the
designer's toolbar, "Suggest help text" beside the help text in the inspector, and a drafted form marked "Drafted by
AI, please review" until its first save (docs/ux/information-architecture.md, sections 3 to 5).

The spike confirmed Umbraco.AI's chat API on 17.5.0 and later: `IAIChatService.GetChatResponseAsync(Action<AIChatBuilder>,
messages, cancellationToken)`, whose builder takes the feature's alias, a profile by id and an output schema
(`WithOutputSchema(AIOutputSchema.FromJsonSchema(...))`, applied as the request's JSON response format). A provider
that ignores the schema still answers text, so the reply is always read as untrusted text.

## Decision

1. **Packages.** `src/Jiron.Umbraco.FormBuilder.AI` is a Razor class library that references Core, Web and the
   `Umbraco.AI` meta package, so installing the add-on installs a working Umbraco.AI; a site still adds the provider
   package it wants. Like its siblings it shares their internals and depends on them with the exact version
   (ADR-0001), so it builds on the management API's access declarations, the form creation, the texts walker and the
   definition reader instead of public copies of them. It compiles against Umbraco.AI `[17.5.2, 18.0.0)`: 17.5.2 is the
   first release whose profile picker needs only backoffice access, not Umbraco.AI's own section. The demo and the
   add-on's test host run the latest 17.x it was tested with (17.7.0). The core packages never reference Umbraco.AI;
   an architecture test reads their references. Its endpoints sit in the management API below
   `/umbraco/jfb/management/api/v1/ai`, in an OpenAPI document of their own, `jfb-ai`, so the core's document and the
   client made from it are the same with or without the add-on; the add-on's backoffice client is generated from that
   document's committed snapshot, as the core's is (ADR-0050). Web's controller discovery takes the add-on's assembly
   as it takes its own.
2. **Its tests live apart.** Umbraco.AI.Core holds a package migration plan that Umbraco's type scanning registers in
   every host whose folder holds the assembly. The fake provider therefore lives in
   `tests/Jiron.Umbraco.FormBuilder.Testing.AI`, not in the shared Testing project, and the add-on's tests in
   `tests/Jiron.Umbraco.FormBuilder.Tests.AI`, which link the integration project's harness; the other test hosts never
   load Umbraco.AI. Both are never packed.
3. **The fake provider** is Tandem's, ported: a strict scripted chat client that refuses what a real API refuses,
   reports usage, and records every request in a journal that tests read. It answers by the schema the request names
   (a draft, a translation or suggestions) with a deterministic answer made from the request, and a directive in the
   editor's text (`draft:`, `say:`, `error:`) scripts a test's own answer. No test and no demo run ever uses a real key
   (D22); trying the add-on with a real provider is an owner's task (docs/OWNER-TODO.md). The demo installs the add-on
   as a site does, and references Testing.AI as it references Testing: the fake provider is left out of Umbraco.AI's
   providers unless the host's `JironFormBuilderTesting:FakeAI` offers it (`Offered` for the add-on's test host,
   whose tests set up their own profile; `Chosen` for the browser tests' host, which gets the fake connection and chat
   profile at start-up, chosen in Settings › AI, and logs a line the harness waits for). `scripts/demo.ps1` offers
   nothing, so the demo shows what a site without a provider shows.
4. **Draft a form with AI** is an option of the library's create menu, where the user may create a form (`design` where
   it lands, as for a blank form). Its sidebar takes a description or a pasted questionnaire of up to 20,000
   characters and answers with a new form in the designer. The server asks for a small draft of its own shape (a
   name, pages, groups and fields of the built-in types that hold an answer or show content, with labels, help texts,
   whether required, and options), reads the reply strictly within limits, builds a definition from the blank form's
   defaults, and has it read and checked as every save is. A reply that is no usable form creates nothing and says
   so. A usable one is created as a draft, never published, its first version commented "Drafted by AI, please
   review", its audit entry `form.drafted`, and its notifications say `FormSource.Drafted`.
5. **The mark.** The add-on keeps the version it drafted per form, and a footer app of the form workspace shows
   "Drafted by AI, please review" while the open draft is still that version; the editor's first save ends it, as
   the information architecture says. A form's deletion removes its mark.
6. **Translate with AI** is an item of the designer toolbar's "…". Its sidebar says first whether the AI can
   translate, saving nothing when it cannot. **Translate** saves unsaved changes first, as Preview does, and then sends the saved draft's texts that have a default text and lack the chosen language (or all of them, when the
   editor asks to replace what is there) to the AI, never more than 400 texts. A dictionary item (`#key`) is not
   sent: the dictionary translates it. Each answer must keep its source's merge tags and pass the text checks of a
   save, or it is left out and counted. The preview lists each text beside its translation; the editor may change
   one, or leave it out, and **Apply** writes them into the open draft as one step that Undo takes back. Nothing is
   saved until the editor saves.
7. **Suggest** sits beside a field's help text while the designer shows the site's default language. Its list opens
   in the inspector, under the label's row and above the box, so nothing covers the text **Use** changes. It asks first
   whether the AI can answer, saving nothing when it cannot; then it saves and asks for a help text and up to three accessibility improvements to the field's own texts (its label, help or
   placeholder: an abbreviation spelled out, a placeholder that holds what the help should), each with its reason.
   **Use** writes one into the draft as one step Undo takes back.
8. **Two new designer extension points** carry the last two, and a site's own code may use them too:
   `jfbDesignerAction` (an item in the toolbar's "…", after the designer's own) and `jfbFieldTextAction` (an element
   beside a field's label, help or placeholder in the inspector). Both get a `JfbFormDesigner`: the form's key, the
   language shown, the draft as the designer holds it, whether it has unsaved changes, a save, and a change of texts
   by their paths that the designer makes as one undoable step. The paths are the ones the server's texts walker
   gives (`$.pages[0].groups[0].fields[1].help`); a change to anything that is not a text of the draft is refused.
9. **Editors only, and no entry data.** Every endpoint is in the management API and declares the permission its
   screen needs: `design` on the form or where a new one lands, `manageSettings` for the add-on's settings. The prompts
   are built from what the editor typed and from the saved definition, never from entries, members or files; a test
   creates entries with distinctive answers and finds none of them in any prompt the fake provider saw. The editor's
   text and the form's texts sit inside tagged blocks that the fixed instructions call data, never instructions. Logs
   name only the failure's kind, never a prompt or a reply.
10. **Settings and health.** Settings › AI (shown with the add-on, to users with `manageSettings`) holds one choice,
    the Umbraco.AI profile the add-on uses, with Umbraco.AI's own profile picker (chat profiles only); without one, it
    uses Umbraco.AI's default chat profile. The choice is kept in `jfbKeyValue` and audited. A health check, "AI
    add-on", says whether that profile exists and can chat and whether its connection is active and answers a test
    without spending tokens, and the features say why they cannot work instead of failing.

## Consequences

- An editor gets a first draft, a translation or a better help text in seconds, and always reviews it: nothing an AI
  writes is published, or even saved, without an editor's own save.
- Visitors carry no new risk: no entry, member or file reaches the AI, and the add-on adds nothing to the website.
- The add-on is the one package that pulls Umbraco.AI and its dependencies onto a site, and only where a site installs
  it; the licence check and the notices cover them.
- Two more test projects run in CI. The integration project's harness is linked into the AI tests rather than shared
  through a library, which keeps the existing hosts exactly as they were.
- `jfbDesignerAction` and `jfbFieldTextAction` are public extension points with their declarations and documentation.
- The add-on's backoffice has budgets of its own, measured by `scripts/size-check.mjs` where its project serves the
  files: at most 8 KB gzip added to every backoffice session's start (its entry, the entry's static imports, and its
  words in English and the largest other language), and at most 64 KB for its whole bundle. It is packed with the core
  packages, its client assets checked as Web's are.
