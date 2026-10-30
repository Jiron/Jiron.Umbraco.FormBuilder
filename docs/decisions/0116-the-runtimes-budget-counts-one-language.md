# 0116. The runtime's budget counts one language

- Status: accepted; amends [ADR-0026](0026-client-workspace-and-asset-delivery.md) (what the size check counts as the
  runtime with every feature) after [ADR-0112](0112-checks-and-messages-on-the-page.md) gave each language's words a
  file of its own
- Date: 2026-10-30
- Roadmap step: 6.4g (decided and built); every later step of 6.4 is held to it

## Context

Section 4.12 gives the website runtime 16 KB gzip for its core and "40 KB gzip with everything", the core with every
feature it loads on demand (uploads, signature, repeater, calculations, captchas). ADR-0026 wrote the check as "the
runtime with every feature at most 40 KB" and measured it as every file the runtime's build emits, which was the same
thing while every file was code.

ADR-0112 (6.4d) moved the visitor messages' words into a file per language, `chunks/messages.<language>-<hash>.js`, so
that a page loads its culture's words and no other. Since then the check has added every language's words to the
features: English and German today, and the seven languages visitor texts come in (CLAUDE.md) would add five more
files that no page ever loads together. Answer recall (6.4g, ADR-0115) brought the count past 40 KB with 1.6 KB of
English words that a German page never loads, and the next languages would push any runtime past it without a byte of
new code.

## Options

1. **Keep counting every file.** The budget would then shrink with every language added, and measure no page.
2. **Count every file but the languages' words, plus the largest language's words:** the most any one page loads,
   whichever its culture. Chosen.
3. **Count no words.** A page always loads one language's, so this would measure less than any page loads.

## Decision

- **The budget of every feature** counts every file of the runtime but the languages' words, and the largest
  language's words beside them, at most 40 KB gzip. A file is a language's words when every module it holds lies in
  `packages/logic/src/lang/`; the chunk report now lists each chunk's modules (relative to the Web project), so the
  check needs no file names.
- **The sum with every language** is printed beside it, without a budget, so the files no page loads together stay in
  view.
- The core's budget (16 KB, the entry and its static imports) is unchanged.
- 6.4g also keeps the logic's base modules (errors, texts, decimals, dates, cultures and the answers' displays) in the
  form chunk, which every feature chunk loads after: once two features shared them, the bundler made them a file of
  their own, which cost its banner and its compression (ADR-0115). The calculation language and the merge tags stay
  chunks of their own.

## Consequences

- The check measures what a page can load at most: with every feature on 6.4g's runtime, 39.93 KB gzip of 40.00 (the
  sum with every language is 41.49 KB).
- A new language's words count only when they are the largest, and a feature's code counts whole, as before.
- The 0.07 KB left will not hold the features 6.4i to 6.4n add (sending, form_changed, uploads, the signature pad,
  rows in place, counters, cards); `docs/OWNER-TODO.md` asks the owner whether section 4.12's 40 KB should grow, and
  until then each of those steps has to make room for what it adds.
