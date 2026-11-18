# Field types

Every field type the designer offers, what visitors see, its settings and what it stores. The settings sit in the
inspector's sections, Essentials, Validation, Messages, Appearance, Privacy and Advanced, as
[the editor's guide](editor-guide.md#the-inspector) describes; the tables below name each type's own settings with their
defaults.

## Contents

- [Settings every field has](#settings-every-field-has)
- [Text](#text): Short text, Long text, Email, Phone, Web address
- [Numbers and dates](#numbers-and-dates): Number, Date, Time, Date and time, Rating, Scale, Slider, Calculation
- [Choices](#choices): Dropdown, Radio buttons, Checkboxes, Grid, Ranking, Checkbox, Yes or no, Consent, Country
- [People](#people): Name, Address
- [Files](#files): File upload, Signature
- [Structure](#structure): Repeatable group, Hidden field
- [Content](#content): Heading, Text block, Divider, Image
- [Protection](#protection): Captcha placement
- [Options](#options)
- [Default values and prefill](#default-values-and-prefill)
- [Changing a field's type](#changing-a-fields-type)
- [What entries store](#what-entries-store)
- [Limits](#limits)

## Settings every field has

Most fields share these settings; a type that has no use for one leaves it out (a date has no placeholder, a hidden
field no label on the page).

| Setting | What it does |
| --- | --- |
| Label | The question. Screen readers and the entries name the field by it, so the form check asks for one. |
| Help text | A line below the label. |
| Required | Visitors must answer. A rule can make a field required only sometimes ([rules](rules-and-validation.md)). |
| Placeholder | A hint inside an empty box. It is not a label: it disappears as the visitor types. |
| Hide the label | Hides the label from sight. Screen readers still read it. |
| Width | In a group with columns, how many columns the field spans. |
| Alias | The name of the answer in merge tags, exports and the API. |
| Default value | The answer the field starts with ([below](#default-values-and-prefill)). |
| Prefill | An answer taken from the page's address, the signed-in member or the site's code. |
| Autocomplete | The browser's autofill token, such as `email` or `postal-code`, so browsers can fill in what visitors have typed before. |
| Sensitive | The answer is masked in entries for users who may not see sensitive data, left out of the answers emails list and written as `•••` where a merge tag names it ([privacy.md](privacy.md)). |
| Keep the answer | **As long as the entry**, or **Until the entry's actions have run**: then emails and webhooks still get the answer, after which it is removed from the entry and shows as "not kept". Sensitive only masks an answer; this removes it. |
| Include in emails | Whether emails that list the answers list this one. On by default. |
| CSS class | Added to the field for the site's own styles. |

**The alias** is made from the label when the field is created, in camel case: "Your email address" becomes
`yourEmailAddress`. It starts with a small letter, holds only letters and digits, and has at most 64 characters. A few
names are kept for the form's own use: `form`, `entry`, `page`, `member`, `query`, `context`, `site`, `date` and
`ufprt`. Changing the alias of a field that already has entries asks first, and the old alias keeps working in merge
tags.

**Messages:** each check a type makes has a message box in the Messages section, such as "Message when no answer is
given". Empty, the standard message applies, in the visitor's language. Administrators can reword the standard
messages for the whole site, and editors for one form, under **Visitor texts** ([multi-language.md](multi-language.md)).

## Text

### Short text

One line of text, up to 4,000 characters.

| Setting | Default |
| --- | --- |
| Minimum length, Maximum length | 0 and 255 characters |
| Text before, Text after | none: short words beside the box, such as "CHF" or "kg" |
| Show a character counter | off: shows "Up to 255 characters", then how many are left |

### Long text

A box of several lines, up to 100,000 characters.

| Setting | Default |
| --- | --- |
| Lines shown | 4 |
| Grow with the text | on |
| Minimum length, Maximum length | 0 and 4,000 characters |
| Minimum words, Maximum words | none |
| Show a character counter | off |

### Email

An email address, checked for a complete form such as `name@example.com`, up to 254 characters.

| Setting | Default |
| --- | --- |
| Ask for the address twice | off; with it on, a second box (label "Repeat your email address" unless you change it) must match |
| Email domains | any domain; or only these domains, or all but these. A domain also allows its subdomains. |

The domain check runs only on the server, so a visitor learns of it when sending.

### Phone

A phone number as typed: digits with spaces, `-`, `.`, `/`, one pair of brackets and a leading `+`, 4 to 17 digits,
and an optional extension. **Default country** helps the page show the number.

### Web address

A full address such as `https://example.com`. **Allowed protocols** is `https://` by default; `http://` can be
allowed too.

## Numbers and dates

### Number

A number, stored exactly up to 15 digits.

| Setting | Default |
| --- | --- |
| Minimum, Maximum | none |
| Step | none: then the step is what the decimal places allow |
| Decimal places | 0 |
| Text before, Text after | none, as "CHF" before or "kg" after |

Entries show it in the reader's number format.

### Date

The browser's own date picker.

| Setting | Default |
| --- | --- |
| Start with today | off; a default value comes first |
| Earliest date, Latest date | no limit; either a fixed date, or a number of days, weeks, months or years from today (below 0 counts back) |
| Monday to Friday only | off |
| Excluded dates | none, one date a line, as 2026-12-31 |

### Time

A time of day. **Earliest time** and **Latest time** limit it, and **Step in minutes** (1 to 720) allows only every
so many minutes, counted from the earliest time or from midnight.

### Date and time

A date with a time of day, as the visitor gives it, with the settings of both: start with today, earliest and latest, Monday
to Friday only, excluded dates and a step in minutes.

### Rating

A row of symbols to choose from, such as stars. Stored as the number chosen.

| Setting | Default |
| --- | --- |
| Number of symbols | 5 (3 to 10) |
| Symbol | Stars; or Hearts or Circles |
| Label of the lowest rating, Label of the highest rating | none |

### Scale

Numbered points, such as "How likely are you to recommend us, from 0 to 10". **Starts at** 0 or 1, **Ends at** 5 to
10 (10 by default), with optional labels for the first and last number.

### Slider

A slider between a **Minimum** and a **Maximum** (0 and 100) in steps (1), with a **Unit** and **Show the value** (on).
It starts in the middle and always has a value, so it never shows a required mark.

### Calculation

A result worked out from other answers as the visitor types, such as a price estimate.

| Setting | Default |
| --- | --- |
| Formula | required, such as `hours * 85` ([merge-tags.md](merge-tags.md#calculations) describes the language) |
| Show the result | on; off keeps it out of the page and out of the listed answers, as a hidden value |
| Result format | 2 decimal places, with optional text before and after |

The server works the result out again when the entry arrives, and its result is the one stored. Rules and merge tags
can use it like any number.

## Choices

Choice fields take their options from the field itself or from a source ([Options](#options)). A new one starts with
three options to rename.

### Dropdown

A list to choose one option from, with a prompt ("Choose one") until something is chosen.

| Setting | Default |
| --- | --- |
| Allow several options | off; on, it becomes a list in which several can be chosen |
| Minimum options chosen, Maximum options chosen | none, with several options allowed |
| Prompt text | "Choose one" |

### Radio buttons

The options side by side or one below the other, to choose one.

| Setting | Default |
| --- | --- |
| Offer "Other" | off; on, an extra option with a box for the visitor's own answer |
| Layout | as the form sets; or one per line, side by side, or cards (which show each option's image) |

### Checkboxes

The options with a box each, to tick any number. The same settings as radio buttons, plus **Minimum options ticked**
and **Maximum options ticked**.

### Grid

Statements in rows, each answered on the same columns, such as a row of ratings from "Poor" to "Excellent".

- **Rows:** the statements. Each row can be marked **Needs an answer**; a required grid needs every row.
- **Columns:** the options, or **Numbered columns** from 1 (or 0) to 5 (up to 10).
- **Several options per row:** off.

Each row is stored like its own answer, and rules can look at each row. On phones the rows stack.

### Ranking

The options put in order, with move-up and move-down buttons that also work from the keyboard and with screen
readers. **Options to rank** limits it to the top few; all by default.

### Checkbox

One box to tick, such as "Send me a copy". **Text beside the box** can hold links. Required means it must be ticked.

### Yes or no

A yes or no question, as **Two buttons** or a **Switch**, with your own **Label of yes** and **Label of no**. Stored as
`yes` or `no`, which rules compare.

### Consent

A box with the consent wording, such as agreeing to the privacy policy. A new consent field is required.

- **Consent text:** the wording, with links. It may use only merge tags that are the same for every visitor, because
  **the exact wording each visitor agreed to is stored** with the entry. A form whose wording changed while a visitor
  filled it in refuses the old wording rather than storing an agreement to words that were never shown.
- **Ticking this loads the captcha:** where the form's captcha waits for consent, it loads only once a visitor ticks
  this box ([spam-protection.md](spam-protection.md)).

A consent field is never ticked in advance, so it takes no default value or prefill.

### Country

A list of countries, named in the visitor's language. **Allowed countries**, **Countries shown first** and
**Preselected country** take two-letter codes such as `CH`, one a line. Entries store the code.

## People

### Name

The parts of a name, each with its own box and autofill: **Title**, **First name**, **Middle name** and **Last name**
(first and last by default). **Required parts** says which parts are needed once any is given; without any listed, a
required name needs every part it asks for.

### Address

A postal address: **Street and number**, **Postcode** and **Town** always, plus **Second street line**, **Region** and
**Country** if chosen. **Required parts** works as for a name; without any listed, a required address needs street,
postcode and town.

Rules can't compare a name or an address, but merge tags can use each part ([merge-tags.md](merge-tags.md#paths)).

## Files

### File upload

Visitors choose files from their device, or drop them on the field. Files are kept in a private folder outside the web
root, never in Umbraco's media ([files.md](files.md)).

| Setting | Default |
| --- | --- |
| Allowed file types | images and PDF documents; choose from images, PDF documents, Office documents, plain text and archives |
| Other extensions | none; more extensions, such as `dwg` (the site's blocked extensions stay blocked) |
| Largest file | the site's limit, in megabytes |
| Maximum files | 1 (up to 20) |
| Offer a drop zone, Show thumbnails | on |
| Offer the camera on phones | off |

SVG files are not images here, because they can carry scripts.

### Signature

A pad to sign on with a mouse, finger or pen, and **Type your name instead** for visitors who use a keyboard or a
screen reader; a typed name is drawn as a signature. Stored as a PNG picture of at most 1 MB. **Pad width** and **Pad
height** follow the theme unless set.

## Structure

### Repeatable group

A set of fields that visitors fill in once for each item, such as one row per guest.

| Setting | Default |
| --- | --- |
| Minimum rows, Maximum rows | 0 and 10 (up to 100) |
| Label of a row | "Row {0}", where `{0}` is the row's number, as in "Guest {0}" |
| Label of "Add", Label of "Remove" | "Add a row" and "Remove" |

A repeatable group can't hold another repeatable group or the captcha. Rules inside a row look at that row's answers.

### Hidden field

A value visitors never see, stored with the entry: for example where they came from, from `{{ query.utm_source }}`.
Its **default value** is worked out on the server when the entry arrives, so it may use every merge tag, the member's
and the site's code's values included, and never reaches the page. Rules on pages, groups and fields can't look at it,
because the page doesn't know its value; rules on actions and outcomes can.

## Content

These hold no answer. They take show and hide rules like fields.

- **Heading:** a heading of level 2, 3 or 4, with merge tags.
- **Text block:** formatted text with links and lists, with merge tags. Anything unsafe in it is removed.
- **Divider:** a line between parts of a page.
- **Image:** an image from Umbraco's media, with **Alternative text** (required) and a **Size** from small to full
  width.

## Protection

### Captcha placement

Marks where the form's captcha appears; without it, the captcha sits before the send button. It is available once
a captcha provider has its keys in the site's configuration, a form holds one at most, and it can't go into a
repeatable group. The form chooses its captcha in Settings › Spam protection ([spam-protection.md](spam-protection.md)).

## Options

**Options from** says where a choice field's options come from:

| Source | Options |
| --- | --- |
| This field | Its own, added one by one or pasted, one a line. |
| An option list | A list from the library, which many forms can share. Changing the list changes every form at once, without publishing. |
| A standard list | Months, days of the week, an agreement scale, a satisfaction scale, or "Yes, no, not sure"; developers can add more. |
| Pages of the site | The pages below a start page, or below the page the form is on, optionally only of chosen document types. |
| A data type | A dropdown, checkbox list or radio button list. |
| The country list | Countries, or only those listed. |
| A JSON address | A list fetched from an address on a host the site's configuration allows, with the paths of the list, the value and the label. |

Library › Option lists holds the shared lists and can also make one from an uploaded CSV or text file.

Each option of the field's own has details:

- **Stored as:** what entries store and rules compare. It starts as the option's name and needn't change when the label
  does; entries already stored keep what they hold.
- **Description** and **Image**, shown in the cards layout.
- **Places:** once this many entries chose it, the option is full. Visitors see how many places are left, and a full
  option can't be chosen. If every option of a required field has places, the form check warns that the form can fill
  up.
- **Shown for:** with **Depends on** set to another choice field, the answers to that field this option is offered for,
  such as cities for the country chosen.

If a source can't be read, or takes longer than 5 seconds, visitors see "The choices could not be loaded. Please try
again later." instead of an empty list.

## Default values and prefill

**A default value** is the answer a field starts with. It may use merge tags that are the same for every visitor,
such as `{{ date.now }}`, a dictionary item or an allowed query value, because pages with forms may be cached. A type
that takes several answers takes one default a line.

**Prefill** fills a field for a particular visitor:

- **From a query value:** from the page's address, as `?plan=pro`. The name must be listed under the form's Settings
  view › Developer › **Query values**.
- **From a member property:** from the signed-in member, either shown and changeable, or **Not shown, taken when the
  entry is sent**. A property the member type marks sensitive, or doesn't let members see, can't be used.
- **From a context value:** from a value the site's code passes when it places the form, listed under Settings ›
  Developer › **Context values** ([rendering.md](rendering.md)).

When more than one applies, the member's value wins, then the context value, then the query value, then the default.

## Changing a field's type

**Change type…** turns a field into another type that holds a value, for example a short text into a dropdown. Grids,
repeatable groups, content and the captcha placement can't change type. Before you confirm, the dialog lists:

- **What goes:** settings, checks and messages the new type can't keep;
- **Needed before it publishes:** what the new type still needs, such as at least one option, or a rule condition that
  compares in a way the new type can't.

The key, alias, label, help and place stay. Entries already sent keep their answers; the Entries screen's filters and
sorting skip those older answers when the type stores its values differently.

## What entries store

| Types | Stored as | Shown as |
| --- | --- | --- |
| Short text, Email, Phone, Web address, Hidden field | text | as typed |
| Long text | long text | as typed |
| Number, Rating, Scale, Slider, Calculation | an exact number | in the reader's number format |
| Date, Date and time | a date, or a date with a wall-clock time | in the reader's date format |
| Time | a time | in the reader's time format |
| Dropdown, Radio buttons, Checkboxes, Ranking, Yes or no, Country | the options' stored values, in order | the options' labels |
| Checkbox | true or false | Yes or No |
| Consent | the agreement, with the exact wording shown | Yes |
| Name, Address | each part | the parts joined |
| File upload, Signature | the files, in the private folder | the file names |
| Grid, Repeatable group | each row as its own answer | row by row |

Exports write the answers as the lists show them ([entries-and-exports.md](entries-and-exports.md)). A field hidden by a
rule when the entry was sent stores nothing.

## Limits

A form holds at most 30 pages, 100 groups, 300 fields (grid rows and fields in repeatable groups count) and 200 rules,
and a field at most 1,000 options of its own.
