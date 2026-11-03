// The option sources' texts in English (ADR-0073, ADR-0172): the settings and choices of the kinds a field may take its
// options from, and the standard lists' names, which the management API serves as `#<area>_<key>` keys
// (docs/api/option-sources.json holds them). options.de.ts has the same keys.
export default {
  jfbOptionListSettings: {
    contentRoot: 'Start page',
    contentRootHelp: 'The page whose pages become the options. Without one, the page the form is on.',
    contentDescendants: 'Every page below it, not only those directly under it',
    contentDocumentTypes: 'Document types',
    contentDocumentTypesHelp: 'Only pages of these types become options. Without any, pages of every type.',
    contentValueFrom: 'Stored as',
    contentValueFromHelp:
      "What an entry stores: key for the page's key, name for its name, or the alias of one of its properties.",
    contentLabelFrom: 'Shown as',
    contentLabelFromHelp: "What visitors read: name for the page's name, or the alias of one of its properties.",
    contentOrder: 'Order',
    dataType: 'Data type',
    dataTypeHelp: 'A dropdown, checkbox list or radio button list data type whose values become the options.',
    url: 'Address',
    urlHelp:
      'The web address that answers with the options as JSON. The site calls only the hosts its configuration allows.',
    urlListPath: 'Where the list is',
    urlListPathHelp: 'A JSON path in the answer, such as $.items. $ is the answer itself.',
    urlValueFrom: 'Stored as',
    urlValueFromHelp: 'The JSON path in each item to what an entry stores, such as $.id.',
    urlLabelFrom: 'Shown as',
    urlLabelFromHelp:
      'The JSON path in each item to what visitors read, such as $.name. Without one, visitors read what is stored.',
    countries: 'Countries',
    countriesHelp: 'Two-letter codes, one a line, such as CH. Without any, every country.',
    source: 'List',
    sourceHelp: 'The name of a list the site registers in its code.',
  },
  jfbOptionListChoices: {
    contentOrderTree: 'As in the content tree',
    contentOrderLabel: 'Alphabetical, by what visitors read',
  },
  jfbOptionSources: {
    months: 'Months',
    weekdays: 'Days of the week',
    agreement: 'Agreement, from strongly disagree to strongly agree',
    satisfaction: 'Satisfaction, from very dissatisfied to very satisfied',
    yesNoNotSure: 'Yes, no, not sure',
  },
};
