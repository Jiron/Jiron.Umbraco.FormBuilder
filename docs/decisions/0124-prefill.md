# 0124. Prefill

- Status: accepted; amends [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (`prefill.context`,
  and the projection's prefill), extends [ADR-0118](0118-defaults-with-merge-tags-on-the-page.md) (a query prefill is
  filled on the page as a default with a query tag is, and wins over it), [ADR-0035](0035-structural-validation-and-public-projection.md)
  (a field the page cannot know is kept out of its rules), [ADR-0080](0080-reading-a-submission-after-the-phase-5-review.md)
  (a hidden field's server-side values) and [ADR-0104](0104-the-output-cache-rules-and-the-render-endpoint.md) (one
  more render per visitor)
- Date: 2026-10-30
- Roadmap step: 6.5c (decided and built); 14.3 binds members to the page's rules and adds the member link on entries

## Context

A field's `prefill` has named a query value the form allows (`query`), a member property (`member`) and whether the
member's value shows (`memberMode`: `show`, or the tamper-proof `hide`) since the schema of ADR-0002. Only the query
part did anything: the reader takes a hidden field's posted value when the field is prefilled from the query. Step
6.5c asks for prefill from members, in both modes, from the allowed query values and from context values, the values
a page gives the form where it places it (`<jfb-render context-topic="…">`, `developer.contextValues`).

Three things decide the design. A page may come from Umbraco's output cache, whose key ignores the query string
(ADR-0004, ADR-0118). A member's values are the member's own and per visitor. And the page's rules and the server's
must decide alike (D10), while the page knows nothing the server keeps from it.

## Options

**Context values:**

1. **A default value with `{{ context.topic }}`.** Defaults with tags are the browser's to fill (ADR-0118), and the
   browser does not know the page's context values; the server would have to render some defaults and leave others.
2. **`prefill.context`, the name of a context value the form lists,** beside `query` and `member`. The server knows
   the placement's values while it renders, and they belong to the page as much as its own text does, so writing one
   into a control keeps a cached page what its own template made it. Chosen.

**What `hide` does for a visitor who is not signed in, or a member without a value:**

1. **The field is never shown,** as a hidden field. A form open to anyone would then ask nobody for it, and a required
   one could never be sent by them.
2. **The field shows and is answered as any other,** and hides only where the member's value stands in for it. Chosen:
   a contact form spares its members the email address and still asks everyone else.

**The page's rules and a field hidden from a member:** the page has no control for it and does not know the value.
Letting the rules read it would make the page and the server decide apart, so such a field is kept out of the page's
rules and calculations, as a hidden field is (ADR-0035); the rules of actions and outcomes, which run on the server,
still read it.

## Decision

**The sources, and which wins:** the member's value, then the context value, then the query value, then the field's
default. Each counts only when it is not empty. They give a control its start on a first render and in a row the
visitor adds; after a post the posted answers stand, as for a default.

- **`member`** names `name`, `email` or a member property, as `{{ member.<name> }}` reads it (ADR-0067): only a
  property the member type lets the member see and does not mark sensitive. The server resolves it for the member
  signed in on the request.
  - **`show`** (the default): the control starts with the value, and the member may change it; the posted answer is
    stored.
  - **`hide`**: for a signed-in member whose value stands in, the field is left out of the page, and the entry takes
    the member's value whatever is posted, so no post can put another value there. The value stands in where it is not
    empty, keeps the field's limits, its type reads a value from it and it passes the type's checks and the field's
    own (`FieldValues.StandsIn`, one rule for the page and the reader): an entry never fails on a value the member can
    neither see nor change. Where it does not stand in, and for any other visitor, the field shows and is answered as
    usual. The structure check refuses what the page could then not decide: a repeatable group's child that hides
    (every row would hold the same value); a hiding field with a check against another field or the other entries
    (`matchesField`, `uniquePerForm`); a page's rule, calculation, field check or form-level validation rule that
    reads such a field, and a validation rule whose message shows on it. Its message names the reason and the way out
    (`memberMode` `show`). The rules of actions and outcomes, which run on the server, still read it.
- **`context`** (new) names a value the form lists in `developer.contextValues` (the structure check refuses another);
  the server writes the placement's value into the control, read as the form's context inputs read it: the
  attribute's name in any letter case, and nothing for a value over their limit.
- **`query`** names a value the form allows, as before. The server marks the field `data-jfb-default` on a first
  render, and the page fills it from its own address as it fills a default with a query tag (ADR-0118), the query
  value first and the default when the address has none; the server writes nothing, so a cached page stays the same
  for everyone. Without JavaScript the control starts with its default. A query value the form does not allow never
  reaches a field: the page reads only the allowed names, and so do the post and the reader.
- **A hidden field** (`hidden`, whose value the server resolves) takes the member's and the context value at
  submission, before its default; its query prefill stays the page's to post (ADR-0080). It is never in the page.
- **A member name** is a name as merge tags write it (letters, digits, `_` and `-`), checked when the form is saved,
  so it can never break out of the tag it becomes.

**The member on a request.** Both transports (the post without JavaScript and the JSON endpoints) hand the pipeline
the key of the member signed in on the request (`SubmissionRequest.Member`), and the merge tags' scope carries it, so
hidden values, a hidden member field and an outcome's `{{ member.name }}` read that member. The rules' "member signed
in" stays false on the server, as on the page, until 14.3 binds members to the page's rules: the two always decide
alike.

**Caching.** A form with a field prefilled from the member renders `no-store` for a signed-in member (ADR-0104), and
stays cacheable for everyone else: their render holds no member value. Umbraco's own request filter keeps a member's
request out of the cache anyway; a site whose filter caches members still never stores a member's form.

**The projection** gives the page `prefill` with `query`, `context` and whether the member fills the field (and in
which mode), never the member property's name (ADR-0002).

**The demo.** The contact form takes the member's name (`show`) and email address (`hide`), and a new "What is it
about?" field the page's `topic` context value, which the contact page gives from a page property; the workshop
booking keeps its query default, and the contact form's "Where did you hear about us?" takes the allowed query value
`source`.

## Consequences

- `FieldPrefill.Context` joins schema v1 as an optional property: a form without it reads as before.
- A signed-in member's contact form is never cached; an anonymous visitor's still is.
- A text on the page that recalls a field hidden from a member stays empty on their page, as the page holds no value
  for it; the server's texts (an outcome, a mail) show the member's value.
- 14.3 sets the rules' member state on both runtimes together, adds the member link on entries, and audits prefill
  with members-only forms.
