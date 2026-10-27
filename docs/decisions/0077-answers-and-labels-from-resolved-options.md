# 0077. Answers and labels from resolved options

- Status: accepted; amends [ADR-0062](0062-the-built-in-types-checks.md) (a choice's answers are held to the options
  the source resolves, cascading included; the vectors' options) and [ADR-0066](0066-answers-as-merge-tags.md) (an
  option's label from a list or another source); amended in 5.5g by
  [ADR-0081](0081-what-visitors-and-failures-cost-the-option-lists.md) (the parent answers a submission's check resolves for)
- Date: 2026-10-27
- Roadmap step: 5.5d (decided and built); 6.1 runs the reader in the pipeline, 6.4 gives the browser's checks and
  display the options the page resolved

## Context

Until 5.5d a choice whose options come from a library list or another source (ADR-0073) takes any answer on the
server and in the browser, and its merge tags show the stored value instead of the option's label: only a field's own
options were known where answers are checked and shown. A cascading field (ADR-0073) filters its options by the
parent field's answers, but only the option service applied that filter; the checks held an answer to every option of
the field, whichever parent answer it belongs to. Open were:

1. **How resolved options reach the checks.** A type checks its answers in `Validate(FieldValidation)`, which is
   synchronous and knows only the field; resolving a list is asynchronous and needs the culture, the page and the
   parent's answers.
2. **Where cascading is decided.** The option service filters a field's options for the parent's answers it is given;
   the checks see the parent's answers as the rules see them, row by row in a repeatable group.
3. **A source that cannot give its options when a visitor submits:** a list deleted since the page was shown, a code
   source that throws, a source slower than 5 seconds.
4. **Labels in merge tags.** `FieldDisplay` is synchronous too, and one rendering may show many fields.
5. **The vectors**, which give a field's own options as values only, and treat a choice without options as one whose
   list nobody checks.

## Options

1. **How resolved options reach the checks.**
   - The type resolves them itself in `Validate`: every site's type would need the option service, and the check
     would turn asynchronous for one helper.
   - The reader resolves them before the check and hands them over with the check's input; the check reads one set.
     Chosen.
2. **Where cascading is decided.**
   - Only in the option service, the check taking what it is given: the browser's check would then need the page's
     already filtered options, and a field's own options, which the check reads from the field, would never be filtered.
   - In the check, in both runtimes, by the parent's values as the rules see them, over a field's own options and
     resolved ones alike; the option service filters by the same rule, so options it already filtered pass again
     unchanged. Chosen.
3. **A source that cannot give its options at submission.**
   - Refuse every answer as `invalid`: the visitor chose from options the page showed, cannot put it right, and the
     entry is lost unless the source comes back; a slow source under load would turn real visitors away.
   - A new error kind ("the options could not be checked, try again"): a message in seven languages for a fault of the
     site, which the visitor still cannot put right.
   - Take the answers as given, held to the type's limits as every answer is (at most 200 characters, cleaned), and log
     the field without its values. Chosen: no entry is lost (D6, D14), the values are escaped wherever they show, a URL
     list keeps its last copy while its address fails (ADR-0076) and a library list's options are kept, so this is
     rare, and the log tells the site which field to look at.
4. **Labels in merge tags.**
   - Resolve a field's options each time a tag shows it: a list read for every tag of a mail.
   - Resolve the options of a rendering's answered fields once, as its dictionary texts are read once (ADR-0067), and
     look the labels up synchronously. Chosen.
5. **The vectors.**
   - A case-level list of resolved options: the field would not say where its options come from.
   - The field says `fromSource`, as the public projection does, and its `options` are those the source resolved; an
     option is a value or `{ "value", "parentValues" }`, and `parent` names the field whose answers filter them.
     Chosen.

## Decision

**The options the answers are held to** are the field's own, or those its list or source resolved for the submission,
compared exactly; for a field with a `parent`, only those that belong to the parent's answers: an option that names no
parent value belongs to every answer, one that names some belongs to those. The parent's answers are its values as the
rules see them (none when the rules hide it), in a repeatable group the row's own child before the form's field. A
field without a parent filters nothing, even when its options name parent values. `FieldValidation.Options` gives the
set to every type's `Validate`, the built-in choice types (ADR-0062's `invalid`) and a site's own: a dropdown, radio
buttons and checkboxes hold their own values to it, a grid without a scale its columns, a ranking its options and how
many it ranks. It is null when the options come from a list or source that could not give them, and then any answer is
taken.

**The reader** (ADR-0072) resolves, through `IFormBuilderOptionService`, the options of each shown field whose options
come from a list or another source and that has an answer to check, in the submission's culture and page and for the
parent's answers, once per field and parent answers in a submission. A source that cannot give its options leaves the
field's answers unchecked against them, which is logged with the field's alias, never its values.

**Labels in merge tags:** the answers' texts (ADR-0066) resolve, once for each rendering, the options of every answered
field whose options come from a list or another source, in the rendering's culture and page and for all of the
parent's answers (every row of a repeatable group), and `FieldDisplay.OptionLabel` shows the label the source resolved
for a value, as it shows a field's own option's; a value the source no longer offers, or a source that cannot give its
options, shows the value. The scope of a rendering for an entry carries the entry's culture, so its tags show the
labels of that culture.

**In the browser** `checkField` takes the options in the field's `options` (a value, or a value with its
`parentValues`) and its `parent`, with the other fields' values by id, and filters them by the same rule; options that
are absent are a source that could not give any, and take any answer, as on the server. The display already takes the
options with their labels in the page's culture, which the runtime (6.4) gives for a list as for a field's own options.

**The vectors:** `fromSource: true` marks a field whose options come from a list or another source; its `options` are
those resolved, and without `options` it could not give any. A type that cannot be answered without options gives
`options` or `fromSource`, and `parent` comes only with one of them. The Core runner passes a source's options to the check as
the reader does, and builds a field's own options with their parent values.

## Consequences

- A choice from a list refuses what the list lacks on both sides, and a cascading field what does not belong to the
  parent's answer, so a visitor who changes the parent is told to choose the child again before the server says so.
- A site's own type with options gets the resolved, filtered set without knowing where the options come from.
- Mail and outcomes show the labels editors wrote in the list, in the entry's culture, instead of the stored values.
- A submission costs one option lookup per answered field from a source; library lists, URL lists and content lists
  are kept, a code source is asked each time as ADR-0073 decided.
- **Revisit** if sites want a choice whose source fails to refuse its answers instead (a setting per form), or the
  stored entry to carry the label it was chosen by.
