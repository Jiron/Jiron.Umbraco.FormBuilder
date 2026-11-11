# 0086. The options endpoint

- Status: accepted; extends [ADR-0081](0081-what-visitors-and-failures-cost-the-option-lists.md) (how the endpoint
  holds its `parent` and `culture`) and [ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md) (another
  endpoint of the public API)
- Completed by: [ADR-0231](0231-members-entries.md), which builds what this left to step 14.3 (members)
- Date: 2026-10-28
- Roadmap step: 6.1c (decided and built); 6.4's runtime asks it for a cascading field's options, 6.6's renderer too,
  14.3 admits only members to a members-only form's options

## Context

A choice field takes its options from itself or from a library list or another source (ADR-0073), and a cascading
field's options depend on its parent field's answers. The page a form is rendered on (6.2) and the form endpoint
(ADR-0085) give the options that do not depend on an answer; once a visitor answers a parent, the browser needs the
child's options for that answer. Section 4.6 names the endpoint `GET forms/{key}/options/{fieldKey}?parent=&culture=`,
and ADR-0081 decided that it holds its `parent` to the options the parent offers and its `culture` to the site's
cultures, as the submission reader holds a post's answers, so no text a visitor gives becomes a cache key or reaches
a source unchecked.

Open were: which forms it serves; how it holds the answers of a parent whose own options cascade; what it answers when
a source cannot give its options; and how its answers are cached.

## Options

1. **Which forms.**
   - Those the form endpoint serves, the headless ones: a form rendered on a page with Razor could not cascade from a
     list, since its runtime asks the same endpoint.
   - **Every published form not in the trash,** as the page that shows it does; the field's options are what that page
     shows. Chosen. The headless setting keeps its meaning: whether the definition endpoint serves the form.
2. **A parent whose own options cascade.** A country, its regions from a source by country, a region's cities from a
   source by region: holding a city request's region answers needs the regions offered for its country.
   - The parent's answers alone: the regions would be taken unchecked (a visitor's text reaching the city source) or
     refused (no three-level cascade at all).
   - **The answers of every field up the chain, each by its field's key:** `parent` for the field's parent, as section
     4.6 names it, and `parent.<fieldKey>` for a field further up. Each level is held to the options its own parent's
     held answers give, as the submission reader does. Chosen.
3. **A source that cannot give its options.**
   - `503`: a page would treat a field without options as a failed request.
   - **`200` with `available: false` and no options,** as the option service reports it (ADR-0073), so the browser
     shows the field as the server renders one whose source failed. Chosen.
4. **Caching.**
   - `no-store` (the frame's default): every change of a parent's answer costs a full answer.
   - **`no-cache` with an `ETag` of the answer's bytes,** as the form endpoint (ADR-0084): a source's own keeping
     (`cacheSeconds`) decides when the options change, and a client revalidates cheaply. Chosen.

## Decision

- **The route:** `GET forms/{key}/options/{fieldKey}?culture=&contentKey=&parent=&parent.<fieldKey>=`, with `parent`
  and each `parent.<fieldKey>` repeated once per answer. The field's own parent may answer as `parent.<fieldKey>`
  too, so a client may name every level alike; a `parent.` name that holds no field key answers `400 invalid_request`.
  - `key` is the form's key; anything else, and a form that is missing, unpublished, in the trash or whose published
    version cannot be read, answers `404 form_not_found`, as the form endpoint answers.
  - `fieldKey` is a field of the published form, a repeatable group's child among them, that has options of its own or
    from a source; anything else answers `404 not_found`.
  - `culture` is held to the site's languages and `contentKey` to published content, as the form endpoint holds them;
    one that cannot be read answers `400 invalid_request`.
- **The answers held:** a level's answers are those of its field's answers, cleaned as answers are, that are options
  that field offers (its own options, or those its list or source resolves for its own held answers), each once and in
  their order. A level whose options are unknown makes the field's unknown (`available: false`); a field outside the
  form's fields, a chain that leads back to itself, or a `parent.<fieldKey>` the chain does not name offers nothing. The
  option service then drops what no option could be and keeps options per answer only for none or one (ADR-0081).
- **The answer:** `{culture, available, options}`, each option as `{value, label, description, image}` with the label in
  the culture (a blank one shows the value) and `description` and `image` only when the option has them, the field's
  own options filtered by the held answers as a list's are. `no-cache` with an `ETag` of the first 32 hex digits of the
  SHA-256 of the answer's bytes, and `304` for a weak `If-None-Match` that holds it.
- **The declarations:** the `read` rate-limit policy, no body, and a time limit of 20 seconds: a source waits at most
  5 seconds (ADR-0073), and a chain of three sources fits with room to spare.

## Consequences

- A visitor's answers cost the sources and the caches no more than the options the form offers, on the endpoint as on
  a submission.
- A three-level cascade works on a page and on a headless site, and a fourth level as long as its sources answer
  within the time limit.
- Three places walk a chain of parents by ADR-0081's rule: the submission reader with a post's rows, the merge tags
  with the answers' rows and what the rules hid, and the endpoint with a request's answers. Each has its own tests.
  **Revisit** if a fourth needs it: one walk over a lookup of answers would serve them all.
- A members-only form's options answer to anyone until 14.3 builds members' access, which admits only the members the
  form admits.
