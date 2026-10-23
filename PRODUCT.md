# Product

<!-- impeccable:product-schema 1 -->

The product record that the `impeccable` design skill reads before any design work (ADR-0020). Its facts come from the
owner's written brief, ROADMAP §1, §2 and §4.13, not from an interview: the owner answers no questions during the
build (CLAUDE.md rule 8), so nothing here was confirmed in conversation, and each fact names the roadmap part it comes
from. Where the roadmap and this file ever differ, the roadmap wins and this file is corrected.

## Platform

web

## Stack

Set by the roadmap, not chosen per surface (§2, D9 and D12):

- **The backoffice:** a top-level Umbraco 17 section, built with Lit, TypeScript and Umbraco's UUI components, using
  Umbraco's own patterns (trees, workspaces with views, the save bar, sidebars, modals, entity actions, toasts).
- **The website forms:** server-rendered Razor views, improved by a small framework-free TypeScript runtime; a
  headless path renders the same markup through a web component.
- **Design documents** (wireframes, mock-ups) are static HTML and CSS without a build step.

## Users

From §1.2. Each works in a different situation:

- **Editors** (marketing, communications) build and change forms in the backoffice without a developer. They want
  to see the form as visitors will, to be warned about mistakes, and to be unable to break a live form by accident.
- **Entry handlers** (sales, HR, support, events) open the section several times a day to work through new entries:
  read, approve or reject, note, forward, export. Speed and trust matter most: every email went out, or they can
  see why not.
- **Privacy officers** come rarely, for a person's request or a retention question. They need one place to find,
  export or erase a person's data, and proof of consent.
- **Administrators and developers** install and configure the package, decide who may do what, deploy forms
  through source control and extend it through documented interfaces.
- **Visitors** fill in the forms on the website, mostly on phones. They want a short, clear form that says exactly
  what is wrong, never loses their answers and doesn't track them.

## Product Purpose

Jiron Form Builder is a form builder for Umbraco 17 sites, installed as a NuGet package (§1.1). Editors design
forms in a **Forms** section of the backoffice, visitors fill them in on the website (Razor or headless), and the
site's team works with the entries. Success means that an editor builds and publishes a working form in minutes,
that no entry is ever lost, and that each team member finds their daily task in a few clicks.

## Positioning

It lives entirely inside Umbraco and is built from Umbraco's own components and patterns, so it reads as part of
the backoffice rather than as a tool added to it. It stores nothing outside the site, sends visitor data to no
third party by default, and needs no community packages. It is free to use, including commercially, under the
PolyForm Perimeter License 1.0.1: "free and source-available", never "open source" (D2).

## Operating Context

- **The backoffice** runs in desktop browsers. Umbraco needs at least 920 px, which leaves a 620 px workspace;
  wider screens give lists and the canvas more room (`docs/research/umbraco-backoffice.md`, section 12).
- **The section** sits beside Umbraco's own sections, in English or German (D20). Editors move between content
  pages and forms; a form is put on a page with a picker or a block.
- **The work:** entry handlers repeat the same actions many times a day, editors work in longer sessions in the
  designer, and administrators set things up once and return when something fails.
- **Visitors** meet the forms on phones and desktops, in seven built-in languages, with or without JavaScript.

## Capabilities and Constraints

- **Capabilities (§1.1):** a form library with folders and templates; the designer (pages, groups, fields, rules,
  the form check, drafts, publishing and history); option lists; actions after a submission (emails, webhooks,
  Slack and Teams, content, state changes); entries with an inbox, spam, notes, exports and insights; themes and
  email layouts; privacy tools, retention and an audit log; permissions per user group, folder and form; an
  optional AI add-on for editors.
- **Words:** form, page, group, field, option list, rule, action, entry, theme, merge tag, template (D4); the
  glossary and tone are in `docs/ux/copy.md`.
- **Constraints:**
  - original work, with no other product as a reference (CLAUDE.md rule 2);
  - no foreign design system in the backoffice (D12);
  - nothing loaded from third-party hosts on a visitor's page unless an editor picks a third-party captcha (D13);
  - permissive licences only for anything that ships (CLAUDE.md rule 4).

## Brand Commitments

- **Names:** the display name is **Jiron Form Builder**, and the backoffice section is labelled **Forms** (D1).
- **The voice** is plain, friendly and direct, in sentence case, with verbs on buttons (`docs/ux/copy.md`).
- **Calm screens** are the owner's first UX requirement (D18): one primary action per view; at most three visible
  secondary actions; each fact said once per screen; nothing squashed on narrow screens.
- **The family:** the owner's packages should feel related while each stays native to Umbraco. The finished
  screens of Tandem (`docs/screenshots/` in that repository) are the polish bar (§4.13).

## Evidence on Hand

- **None yet:** no customers, testimonials, usage numbers, benchmarks or press exist. Design work must not invent
  any.
- **Demonstration content:** the demo site (Phase 3) gets realistic seeded forms and entries, which are labelled as
  sample data wherever they appear.
- **Design references:** the information architecture, the words and the top tasks with their click paths are in
  `docs/ux/` (step 2.1).

## Product Principles

1. **Native first.** Where Umbraco already has a pattern, use it, so the section needs no learning of its own.
2. **Calm by default.** Progressive disclosure, defaults that suit most people, one place for each thing.
3. **Nothing is lost.** Drafts before publishing, undo for reversible actions, Spam instead of dropping, an outbox
   for every action.
4. **Privacy without configuration.** Retention, masking and no tracking are on from the start (D13).
5. **Every top task in three clicks or fewer,** checked against the real screens (2.1, 16.6).

## Accessibility & Inclusion

WCAG 2.2 AA for the rendered forms and the backoffice: keyboard paths for every action (including drag and drop),
visible focus, names for every control, reduced motion respected, and right-to-left layouts for languages written
that way. Visitors get targets of at least 44 px, labels above fields and errors that say how to fix them (§4.13).
