# 0211. Insights

- Status: accepted
- Date: 2026-11-07
- Roadmap step: 11.4, its parts 11.5 (insights) and 11.5b (answer summaries; ADR-0197); builds on
  [ADR-0003](0003-entry-storage.md) (`jfbDailyStat`), [ADR-0008](0008-settings-model.md) (the global privacy section),
  [ADR-0009](0009-privacy-defaults.md) (no personal data), [ADR-0088](0088-the-submission-pipeline-and-the-entries-endpoint.md) (what a stored
  entry counts), [ADR-0146](0146-the-runtimes-budget-grows-to-60-kb.md) (the runtime's budget) and
  [ADR-0209](0209-the-inbox-spam-badges-and-the-summary-email.md) (the idle gate); IA 5.5, 2 (Forms at a glance) and 9
  (Data collection), copy.md

## Context

Step 11.4 counts what visitors do with a form (views, starts, pages reached, the fields that stop them), adds an entry's
origin page, hour and time to complete to the unit that stores it, and shows it all in a form's Insights view and in
the Overview's "Forms at a glance", with a switch to turn insights off for the site and for each form. A survey of the
code (2026-11-07) found the `events` rate-limit policy declared but unused, the per-form switch `privacy.insights`
stored but read by nothing, the global `privacy` settings section seeded but without a record or a screen, a
`durationSeconds` column that nothing fills, the daily-stats table with its repository, no chart component, and a
website runtime 0.2 KB under its 60 KB budget. The owner answers no questions during the run (CLAUDE.md rule 8), so each
choice below is this record's.

## Decisions

1. **Where to count is in the markup.** A form whose insights are on, for the site and for the form, carries
   `data-jfb-insights` with the address of `POST /jfb/api/v1/forms/{key}/events` (the Razor view and the headless
   element alike); a preview, a notice, an outcome and a form whose insights are off carry none, and the runtime then
   sends nothing. No cookie, no storage and no identifier is involved.
2. **What the runtime sends.** One small function of the frame, with no chunk of its own, sends `view` as the form is
   enhanced, `start` at the visitor's first real touch (a trusted event: the preview's and the page's own focus do not
   count), `page` with the page's key for each page reached after the first, and `invalid` with the field's alias and
   the kind of error for each error that stopped the visitor, as `navigator.sendBeacon` with a JSON text body. Each
   beacon goes once in a page's life, so a visitor who tries five times is one stop of that field, and the share of
   "attempts" a field stopped is a share of starts; one the browser could not queue goes again the next time it comes
   up, and an empty address counts nothing. It costs 0.16 KB, which the runtime's budget holds; with the fix of
   ADR-0113 that its test found, the runtime is at 59.97 of 60 KB.
3. **What the server takes.** The endpoint reads at most 1 KB as text whatever its content type (a beacon's is
   `text/plain`), takes `type`, `page`, `field` and `kind` and ignores every other member, so a value posted along is
   never read, let alone kept. It counts only for a published form whose insights are on, a page the published
   version has, a field it has (by alias; a part's or a row's alias counts under its field) and a kind of error the
   server knows. It answers `204` whether it counted or not, `404` for a form that is not served; the `events` rate
   limit applies (120 a minute per address, 500 a second for the site).
4. **Roll-ups.** Beacons do not each write: each server adds them up in memory per form, day, metric and dimension and
   writes the sums to `jfbDailyStat` every 10 seconds and when it stops, so a busy form's beacons never queue behind the
   form's row that a submission holds. A server that crashes loses at most 10 seconds of beacons; entries are never
   counted this way. Metrics: `views`; `starts`; `page` by page key; `invalid` by field key and kind; `refused` by field
   key and kind, which the server counts itself when it refuses a post sent without JavaScript, whose page sends no
   beacons. A page with JavaScript shows the server's refusals as its own errors, so its beacons count them already.
5. **What a stored entry adds to its unit** (ADR-0088): besides `submissions`, an entry that is neither spam nor a test
   counts its origin page (`origin` by content key), its hour (`hour`, 0 to 23 in the site's time zone) and its time to
   complete (`completion`, in buckets: up to 15 s, 30 s, 1, 2, 3, 5, 10, 15, 30 and 60 minutes, and more), all in the
   same unit, so a rollback counts none of them. The roadmap's `completionSeconds` becomes these buckets: a daily sum of
   seconds gives an average, which one forgotten tab skews, while buckets give the median the report shows. The time to complete is from the challenge's first issue to the
   submission, which also fills the entry's `durationSeconds` for the entry panel. A form whose insights are off counts
   only its submissions, and so does an entry that leaves Spam later: its page and hour were those of an entry nobody
   counted then.
6. **Days are UTC days,** as the table keeps them; hours are the site's. A report groups days by day, week (from
   Monday) or month, and compares a calendar period with the same days of the one before, a rolling one with the span
   just before it (IA 5.5).
7. **Reports.** A form's report gives views, starts, entries (submissions) and conversion (entries per view), spam
   caught, entries per day (or week or month) with the comparison, the funnel (starts, then each page reached, then
   entries), the median time to complete (the bucket that holds the middle entry), the fields that stop visitors most
   (stops per start, with the kind that stops most), the top origin pages by name, entries by hour, and the number of
   members who sent entries, counted from the entries at query time. Only users who may view the form's entries see
   it. The Overview's "Forms at a glance" lists the forms the user may view entries of with their views, starts, entries
   and conversion, each with its change from the period before: the ten with the most entries, then views, of the
   first 500 by name, and none with nothing in either period. Both endpoints read the last 30 UTC days to today when a
   request names no span; the table compares with as many days just before when it names none to compare with. A page
   of the website is named only for a user who may open it in Content. Views per website page are not counted: a beacon
   names no page of the site, so the website pages list entries only.
8. **Charts** are small own SVG components: bars for entries per period (the period before as a quieter bar beside each),
   horizontal bars for the funnel, the fields, the pages and the hours. Each has "Show as table", which swaps it for
   its table in place and is remembered per user in `jfbUserSetting` (`insights.tables`). No chart library.
9. **Opt-out.** The global privacy section (`privacy`, ADR-0008) gets its record with the switch `insights` (on by
   default), on the Privacy › Data collection screen, which holds that switch until the rest of its settings come with
   their features. The form's own switch is the first setting of its Settings view's Privacy group. With either off, the
   form carries no `data-jfb-insights`, counts nothing new, and its Insights view says so in one line naming where to
   turn it on. What was counted stays until retention removes it.
10. **Answer summaries** (11.5b) are computed in the database over `jfbEntryValue`, grouped by field and value, for the
    entries the list's filter holds within the range, never for test entries, entries in Spam, sensitive fields or
    values kept only until the actions run.

## Consequences

- Positive: no personal data: a beacon holds a type, a page, an alias and a kind; the stats hold counts.
- Positive: beacons never contend with submissions for the form's row.
- Negative: up to 10 seconds of beacons are lost when a server crashes; the funnel's numbers are approximate there.
- Negative: the runtime's budget has 26 bytes left; the next runtime feature pays for itself first.
