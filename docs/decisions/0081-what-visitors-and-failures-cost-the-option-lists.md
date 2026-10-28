# 0081. What visitors and failures cost the option lists

- Status: accepted; amends [ADR-0073](0073-option-lists-and-sources.md) (the parent answers a source
  sees and a library list keeps options for, and a stored kind this version does not know),
  [ADR-0076](0076-url-and-file-lists.md) (a URL list that fails with no copy, and a body that breaks off or does not
  decompress) and [ADR-0077](0077-answers-and-labels-from-resolved-options.md) (the parent answers a submission's
  check resolves for); the parent answers merge tags resolve labels for held the same way, row by row too (5.5j), by
  [ADR-0083](0083-merge-tags-and-the-two-runtimes-after-the-phase-5-review.md), and a Brotli body that does not
  decompress, added in 5.5i; a stream disposed as the time limit passes counted as the time limit, added in 5.5k;
  the `options` endpoint's `parent` held up a chain of parents by
  [ADR-0086](0086-the-options-endpoint.md) in 6.1c
- Date: 2026-10-27
- Roadmap step: 5.5g (decided and built); 6.1's `options` endpoint holds its `parent` and `culture` the same way

## Context

The Phase 5 review read the option lists for what a visitor's post or a failing service costs the site. It found:

1. **Parent answers as keys.** A cascading field's options are asked for with its parent's answers (ADR-0073). The
   submission reader passed the parent's answers as the rules see them, which are whatever the visitor posted. They
   reached a code source as given, and a library list kept the source's options per set of parent answers for its
   `cacheSeconds`. Each new text a visitor posts was a new entry in every server's cache and a new call to the source:
   a few thousand posts filled the cache with entries nobody would read again. Step 6.1's `options` endpoint takes its
   `parent` from the query, so it would have done the same on every page view.
2. **A stored kind this version does not know.** A list's kind is stored as its camelCase name, and reading a row
   whose name the enum lacks threw (ADR-0033 refuses what something else wrote). A row of a kind a later version wrote,
   left behind after a downgrade, made every page that named the list fail.
3. **A failing address with no copy.** A URL list keeps its last copy for a minute while its address fails (ADR-0076).
   With no copy yet, nothing was kept, so every request asked the address again and waited up to its time limit.
4. **A broken body.** A connection that broke off while the body was read, and a body that did not decompress, escaped
   the outbound call as the runtime's own exceptions instead of a failure with its reason.

It also found that a form's create told its alias taken apart from other conflicts by a flag that the unit of work's
lambda set, and the database runs that lambda again on a transient error, so a second attempt could read the first
attempt's flag.

## Options

- **Parent answers:** cap their number and length in the options service (a visitor could still post every short
  text); hash them into the key (the number of entries is the same); or hold them to the options the parent offers
  before anything sees them, so only an option's value is ever a parent answer. The last is chosen, with the cap kept
  as a second wall in the service, which every caller goes through, and no keeping for two answers or more, since the
  sets of a parent's options that a checkbox list can post have no useful bound.
- **An unknown kind:** keep refusing it (a downgraded site fails on a visitor's page); map it to a kind it is not (the
  list would offer the wrong options); or read the row with no kind, so the list is unreadable, as a list whose
  configuration no longer reads is (ADR-0073). The last is chosen.
- **A failing address with no copy:** keep the failure for the same minute a last copy stands in, so the address is
  asked at most once a minute per server and configuration, as when it has a copy.

## Decision

- **Parent answers are held to the parent's options.** The submission reader passes a cascading field's source only
  those of the parent's answers that are options the parent offers: its own options, or those its list or source
  resolves for its own parent, in turn. Each answer counts once, in its order. When the parent's options are unknown
  (its source cannot give them), the field's are unknown too, and its answers are taken unchecked and logged, as for
  any source that cannot give its options (ADR-0077). A chain of parents that leads back to itself offers none.
- **The options service drops parent answers that no option could have:** blank, longer than an option value, not as
  cleaning leaves them, or repeated; at most as many as a field offers. It keeps a library list's options per parent
  answer only for none or one; for two or more it asks the source each time.
- **The `options` endpoint (6.1) holds its `parent` the same way** and its `culture` to the site's cultures, so no text
  a visitor gives becomes a cache key or reaches a source unchecked.
- **A stored kind this version does not know reads as no kind.** The repository gives such a row `Kind = null`; the
  list is unreadable, logged by its key, and offers no options; an export leaves it out and an import does not take it
  for the same list.
- **A URL list that fails with no copy offers none for a minute.** The failure is kept as the copy would be, until
  `RetryAfter`; meanwhile the kind says its options are unavailable without asking the address again, and without
  logging again, as the fetch logged why.
- **A body that breaks off is no connection, and one that does not decompress is unreadable**, each a failure with its
  reason, as every other failure of a call is. The decoders fail differently: gzip and deflate with invalid data, Brotli
  with an invalid operation, and each is unreadable (5.5i, through the real transport); a stream disposed while it is
  read is no answer of the host and is not taken for one (5.5j), unless the time limit has passed, when it is the time
  limit's (5.5k). A body cut short decodes without an error in all three, so the kind that called gets it short and
  reads it as the text it is.
- **A form's create tells its alias taken by the exception's type:** the form's own insert wraps the conflict, so each
  attempt of the unit tells it anew and nothing carries over from one attempt to the next.

## Consequences

- A visitor's post costs the option sources and the caches no more than the options the form offers.
- A site rolled back to an earlier version shows a list it cannot read as one with no options, not an error page.
- A service that is down costs each server one call a minute per list, with or without a copy.
- Six things now have tests of their own: an answer the parent lacks reaching neither the source nor a cache key, a
  parent whose options are unknown, two parent answers asked twice, a row of an unknown kind, a failing address asked
  twice within a minute, and a body that breaks off or does not decompress.
