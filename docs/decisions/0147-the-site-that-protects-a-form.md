# 0147. The site that protects a form

- Status: accepted; amends [ADR-0139](0139-the-signals-and-the-score.md), [ADR-0140](0140-content-rules-and-the-spam-folder.md),
  [ADR-0141](0141-proof-of-work-on-the-server.md) and [ADR-0142](0142-the-captcha-providers.md)
- Kept by: [ADR-0232](0232-the-site-of-a-request.md), whose site a host names never chooses the protection (step 14.4)
- Date: 2026-11-01
- Roadmap step: 7.4a

## Context

Protection is set per site (section 4.8), and a form at `inherit`, the default, takes its site's level, captcha, ranges and
content rules. The site came from the page the request names, in `contentKey` or by the address of a post without JavaScript.
A visitor chooses both. So a bot could name no page at all, or a page of the laxest site, and escape everything a stricter site
set, with its entry stored as `Submitted` and its actions run. Two readers of the phase review found this on their own.

Sending a client a different answer by its `Host` does not help (14.4), as a client chooses that too. What a client cannot
choose is where editors placed the form.

## Options

1. **Sign the site into the challenge.** The bot asks for its challenge naming the lax site. Rejected on its own.
2. **Only the shared values for a request without a page.** The bot names a lax site's page instead.
3. **The strictest of all sites, always.** A form on a lax site and a strict one would ask a captcha of the lax site's visitors,
   which that site chose not to have.
4. **A page's site only if that page holds the form, by the reference index (ADR-0017), else the strictest of all.** Chosen.

## Decision

- **A page that holds the form:** its site's settings apply, as before. A form on two sites takes each site's for its pages, so
  a bot can reach the laxer of the sites that really show it, and no others.
- **Anything else:** no page, a page of another site, a page that does not hold the form. The strictest of the shared values and
  of every site that sets something: the highest level and the hardest puzzle, every range, word and domain, the strictest content
  rules and the fewest links. Where several ask for a captcha, the shared values' comes first, then the sites' in their stored order.
- **On a site without per-site settings nothing changes:** the strictest of one section is that section, read as before.
- **Every read goes the same way:** the challenge, the puzzle, the captcha and the score, directly and without JavaScript. A
  submission reads it once. The list of the sites that set the section is cached like the sections, and a save clears it.
- **A puzzle's answer counts only for a challenge of at least the bits the site asks now.** The bits come from the challenge,
  so one taken for a lax page, or before the site raised them, would otherwise do. The answer `401 challenge_required` asks the page
  for a new challenge, as any answer that does not solve the puzzle does.

## Consequences

- **A form placed by a view** (`<jfb-render>` or the view component in a template, with no picker) holds no row in the reference
  index, so it takes the strictest settings, also on a lax site. That errs towards the protection, and a site that wants otherwise
  gives the form a level of its own or uses the picker. A form's own level still wins, since `inherit` is what this is about.
- A site row whose root has gone counts until 14.4 removes it (ADR-0043), which errs the same way.
- A headless site that sends no `contentKey` gets the strictest settings. With per-site settings, it should name its page.
- Each protected request reads one row of the reference index, after the gate, under the rate limits.
