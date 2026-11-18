# Merge tags

A merge tag puts a value into a text: an answer into an email, the visitor's name into the thank-you message, the
entry's reference into a subject line. Tags are written in double braces:

```
Thank you, {{ field.firstName }}. Your reference is {{ entry.reference }}.
```

Where a text takes merge tags, **Insert merge tag** below its box lists what it can use, with a search, and inserts the
tag where the cursor is. You can also type tags.

## Contents

- [Writing a tag](#writing-a-tag)
- [Paths](#paths)
- [Filters](#filters)
- [Where merge tags work](#where-merge-tags-work)
- [Missing values and mistakes](#missing-values-and-mistakes)
- [Sensitive and hidden answers](#sensitive-and-hidden-answers)
- [Calculations](#calculations)

## Writing a tag

A tag holds a **path**, the value it writes, and optionally **filters**, which change it:

```
{{ field.email }}
{{ query.ref | default: "web" }}
{{ field.message | truncate: 200 }}
{{ entry.date | date: "long" }}
```

- Spaces inside the braces are optional. Paths and filters are written in small letters; field aliases and property
  names keep their own letter case.
- A filter's arguments follow a colon, separated by commas: texts in double quotes, numbers as `12` or `12.5`.
- To write two braces as they are, write `{{ "{{" }}`. A single `{` or `}` needs nothing.

## Paths

| Path | Writes |
| --- | --- |
| `field.<alias>` | the answer, as the field shows it: option labels, numbers and dates in the reader's format, file names |
| `field.<alias>.value` | the stored value, as rules compare it: the option's stored value, a number in the form `1250.5` |
| `field.<alias>.label` | the field's label |
| `field.<alias>.first`, `.last`, `.title`, `.middle` | a part of a name |
| `field.<alias>.street`, `.street2`, `.postcode`, `.city`, `.region`, `.country` | a part of an address |
| `field.<group>.<alias>` | a field in a repeatable group: its answer in each row, as a list |
| `field.<group>` | a repeatable group: each row as "Label: answer" pairs |
| `entry.number`, `entry.reference` | the entry's number, and its reference, such as `REQ-2026-00042` |
| `entry.date`, `entry.state` | when it was sent, in the site's time zone, and its state |
| `entry.url` | a link to the entry in the backoffice, for emails to the team |
| `entry.answers` | every answer given: a table in an HTML email, "Label: answer" lines in a plain one |
| `entry.table` | the same, with the questions left unanswered |
| `form.name`, `form.alias` | the form |
| `page.name`, `page.url` | the page the form is on |
| `page.<property>` | a property of that page, by its alias |
| `page.up.<property>` | the property from the nearest page above that has a value |
| `member.name`, `member.email`, `member.<property>` | the signed-in member; a property only if members may see it and it isn't sensitive |
| `query.<name>` | a value from the page's address, if listed under the form's Settings view › Developer › Query values |
| `context.<name>` | a value the site's code passes when it places the form ([rendering.md](rendering.md)) |
| `site.name`, `site.url` | the site and its address |
| `date.now` | now, in the site's time zone |
| `dictionary.<key>` | an Umbraco dictionary item, in the visitor's language |

A field that had another alias before keeps it as a former alias, so tags written with the old one go on working.
Developers can add paths and filters of their own ([extending.md](extending.md)).

## Filters

| Filter | Example | Writes |
| --- | --- | --- |
| `default` | `{{ member.name \| default: "there" }}` | the text given when the value is empty |
| `upper`, `lower` | `{{ field.code \| upper }}` | the value in capitals, or in small letters |
| `truncate` | `{{ field.message \| truncate: 100 }}` | at most that many characters, then "…", or a text you give as a second argument |
| `date` | `{{ entry.date \| date: "long" }}` | a date in the reader's format: `"short"` (the default), `"medium"`, `"long"`, `"time"`, `"dateTime"` or `"iso"` |
| `number` | `{{ field.total \| number: 2 }}` | a number in the reader's format, with that many decimal places |
| `currency` | `{{ field.total \| currency: "CHF" }}` | an amount in that currency, as the reader's language writes it |
| `join` | `{{ field.topics \| join: " / " }}` | a list joined, by ", " unless you give another text |
| `html` | `{{ field.message \| html }}` | the text with its line breaks kept, in HTML only |
| `url` | `{{ field.email \| url }}` | the value encoded for use in an address |
| `json` | `{{ field.topics \| json }}` | the value as JSON, for webhook bodies |

Filters run from left to right, and a filter on a list changes each item, except `join`, `default` and `json`. `html`,
`url` and `json` must come last. `number`, `currency`, `date` and `json` work on the stored value behind an answer, the
others on the text it shows.

## Where merge tags work

| Text | What it can use |
| --- | --- |
| Labels, help texts, placeholders, page and group titles, headings, text blocks | answers to fields earlier in the form, the form, the page, the site, the date and dictionary items |
| A consent text | the form, the page, the site and dictionary items |
| A default value | the form, the page, the site, the date, dictionary items and allowed query values |
| A hidden field's value | everything |
| An outcome's message, and its copy of the answers | everything |
| A redirect address | everything, after the host name |
| The entry title | answers, the entry and the form |
| An email's subject and body, webhook bodies, chat messages, content names | everything |

Texts visitors see before they send may use only values that are the same for every visitor, because pages with forms
may be cached; the browser fills in earlier answers as the visitor types ("Thanks, Anna. Where should we send the
quote?"). Everything else is filled in on the server.

**Every value is made safe for where it goes.** In HTML, an answer can't add markup or links; in a subject line, line
breaks become spaces; in an address, a value is encoded; in a JSON body, it is a proper JSON string. The template's own
text is never changed.

So a tag can't stand everywhere: in HTML only in the text, not inside a tag's markup; in an address only after the host
name (a redirect to `https://{{ query.site }}` is refused); in JSON not right after a backslash.

## Missing values and mistakes

- **A value that is empty,** such as a question left unanswered, a member when nobody is signed in, or a query value the
  address doesn't carry, writes nothing. `default` fills the gap.
- **A path or filter that doesn't exist** writes nothing, and the form check warns about it. `default` doesn't help
  there, because the tag names nothing.
- **A tag that is broken,** stands where it can't, or uses a value its text can't use is written as it stands, and the
  form check reports an error that blocks publishing until it is fixed.
- **A redirect that carries answers** gets a warning: addresses end up in logs and browser history.

## Sensitive and hidden answers

- **A sensitive answer** writes `•••` in texts, redirects, outcomes and emails, unless an email action names that field
  itself. The entry title leaves it out, and `entry.answers` leaves it out except in outcomes, where it is masked.
- **An answer hidden by a rule** writes nothing, and lists of the answers leave it out.

## Calculations

A [calculation field](field-types.md#calculation) has a formula in a small language of its own. It names fields by
their bare alias, without braces:

```
hours * rate
round(nights * 120 + if(breakfast, 15 * nights, 0), 2)
yearsBetween(birthday, today())
```

| Part | Means |
| --- | --- |
| `+ - * /` and brackets | arithmetic |
| `= != < <= > >=` | a comparison, which gives 1 or 0 |
| `min(a, b, …)`, `max(a, b, …)` | the smallest or largest |
| `round(x)`, `round(x, 2)` | rounded to whole numbers or to that many places, halves away from zero |
| `if(condition, then, else)`, `and(…)`, `or(…)`, `not(a)` | choosing |
| `daysBetween(from, to)`, `yearsBetween(from, to)` | the days or whole years between two dates |
| `today()` | today, inside the two date functions |

What a field gives:

- numbers, ratings, scales, sliders and other calculations give their number;
- choices give their options' stored values as numbers (several add up; none gives 0), so options stored as `85`
  and `120` can carry prices;
- a checkbox gives 1 or 0;
- dates work inside the date functions only;
- text, files and times can't be used.

A formula has no result while an answer it needs can't be read, a date it needs is empty, or it would divide by zero.
Calculations are exact, without the rounding errors of floating-point arithmetic, and the server works them out again
when the entry arrives. Their results can then be used in merge tags as `{{ field.total }}` and in rules like any
number.
