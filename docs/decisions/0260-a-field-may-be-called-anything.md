# 0260. A field may be called anything: the website's script reads a form through its prototype

- Status: accepted. Keeps the control's name of [ADR-0015](0015-themes-and-the-markup-contract.md), the alias rule
  and the runtime's files of [ADR-0026](0026-client-workspace-and-asset-delivery.md) and
  [ADR-0115](0115-answer-recall-on-the-page.md) as they are
- Date: 2026-11-19
- Roadmap step: 23.1

## Context

**What a browser does.** A form element answers a property read with its control of that name, in front of every
property and method of its own. A field's control is named by the field's alias, and an alias is a lowercase letter
followed by letters and digits, 64 at most. So an editor can call a field `id`, `dataset` or `querySelector`, and on
that form `form.id`, `form.dataset` and `form.querySelector` are the field's control. The website's script read a
form's members plainly in about 280 places.

**What it did.** Tried on the demo on 2026-11-18, the contact form with one more text field, in Edge:

| The field is called | With JavaScript |
| --- | --- |
| `dispatchEvent` | the form cannot be sent |
| `replaceWith` | the entry is stored and no outcome shows, so a visitor may send again |
| `dataset`, `elements`, `querySelector`, `addEventListener`, `removeEventListener`, `ownerDocument`, `hasAttribute` | the script gives up and the form is sent by the post without JavaScript |
| `classList`, `contains` | the form is sent, with errors thrown on the page |

Without JavaScript every one of them is sent as it should be: the server reads a post by its names and has no form
element. The names it adds to a post itself start with `jfb-`, which no alias can.

**Why no test saw it.** The test DOM does not answer a form's property with a control, so the runtime's and the
element's unit tests passed on forms that fail in a browser, and no browser test had a field with such a name.

## Decision

### 1. Aliases stay free

No name is reserved for this, and a control's name stays its alias.

- A list that is right would hold every member a form element has in any browser, now and later: several hundred
  names, `name`, `title`, `action`, `hidden`, `lang` and `style` among them, which are names editors choose.
- A short list would be a guess at what the script happens to read. It would be wrong after the next change to the
  script, and after the next browser release.
- Either list would refuse forms that exist and work without JavaScript today.

### 2. The script asks the prototype

One small module, `proto` in `@jfb/logic/proto`, gives a view of an object's members as its prototype chain defines
them, with the object as the receiver: a getter and a setter run on the form, a method comes back bound to the form,
and `in` asks the chain. A control's name is never on the chain, so the view never meets one.

- The runtime and the `<jfb-form>` element read, write and call every member of a form through `proto(form)`.
- That holds for everything that can be the form: what the types call a form, the root a fit or a notice is given,
  what a selector that matches the form finds, the target of a listener on the form, and the parent of an element the
  markup puts in a wrapper.
- The view is written where it is read and never kept. What goes to the browser's own methods, to a `WeakMap` or to a
  comparison is the form itself, as a browser refuses a view where it expects an element.

### 3. Kept so

- **A lint rule of the client's own,** `jfb/form-members`, reads the types and refuses a read, a write, a call, a
  destructuring and an `in` on anything that is or may be a form, unless it goes through `proto(...)` written in
  place. It runs on the runtime's and the element's code with their project, and costs the lint under two seconds.
- **The unit tests' forms are covered.** Every form a test of the runtime or the element makes gets, for each name of
  its prototype chain that could be an alias, a member of its own that answers the website's code with a control and
  refuses its write, as a browser does. A test's own read still gets the form's member, so the suites read their
  forms as before. This is what finds a form held under a wider type, which the lint rule cannot see: put on the
  code before this record, it fails 705 of the 831 tests. The cover tells the website's code from a test by the file
  of the frame that reads, and has a test of its own, so it cannot stop covering unseen.
- **Browser tests:** a form whose fields carry such names, and which has a control for every other name a form
  element's member has in that browser that could be an alias, shows its errors, is sent and shows its outcome, on a
  server-rendered page and through `<jfb-form>`; the demo's largest form runs its journey covered the same way. The
  names are read from the browser, so a member a later browser adds is covered without a list to keep.
- **A post without JavaScript** with fields called `id`, `action`, `method`, `submit`, `constructor` and `toString`
  has a test of its own.

### 4. A plain object by alias has no prototype

Where the website's code reads an object by an alias, the object has no prototype or the read asks for the object's
own property. One place did neither: the count of what was sent to Insights, where a field called `constructor` or
`toString` found a member of every object and its beacon was never sent. It is fixed.

### 5. The entry still loads nothing before it runs

The runtime's entry and every chunk use the module, and a module two files use is one more file by default: a chunk
of 0.4 KB that the entry imports. That was built first, and two browser tests failed on it:

- **The entry hears a visitor's first answers while the form chunk is on its way,** so that an error the server
  showed goes once its field is answered. With a file to load before it, the entry starts one request later, and an
  answer given in between is lost. A size budget does not show this; the test of an answer given before a slow
  runtime woke does.
- **A chunk cannot import from the entry** to share it either: a page loads the entry with its fingerprint in the
  address, and an import of its plain address would run it a second time.

So the build gives the entry's own modules a copy of the module, made from the one source
(`entryRunsAlone` in `packages/runtime/vite.config.ts`), and the chunks share theirs in the form chunk, which each of
them loads first. The two copies keep their views apart, which is no fault, as a view is never kept or compared. The
build fails when the entry imports a file. The entry is 2.94 KB of its 16 KB, from 2.80 KB, in one file as before.

### 6. A site's own script

A site's script meets what any HTML form does: on a form with a field called `id`, `form.id` is that field's
control. [rendering.md](../rendering.md#javascript) says so, and how to read the attribute past it.

## Ruled out

- **Reserving names:** see 1.
- **Giving the form members of its own that stand in front of the controls.** Tried in Edge: a form refuses a
  property of its own under a name one of its controls has or had, as it remembers the names it once answered.
- **Handing the view on in the form's place.** The browser's own methods refuse it, and a comparison with the form
  fails.
- **Another name for the control,** such as a prefix. The name is part of the markup a site styles and scripts
  (ADR-0015), browsers fill in forms by it, and the post without JavaScript is read by it.
- **Calling each member from its prototype where it is read:** 280 places of `Element.prototype.querySelector.call`,
  with the right prototype for each member, which nobody keeps right.

## Consequences

- A form works with JavaScript whatever its fields are called. Nothing changes for a form that worked before, in its
  markup, its events or `window.JironFormBuilder`.
- The runtime is 64.00 KB of its 100 KB, from 63.54 KB, in nine files as before.
- A new place that reads a form is held by the lint rule where the types call it a form, and by the covered tests
  where they do not, as far as a test drives it. A site's own script is the site's.
