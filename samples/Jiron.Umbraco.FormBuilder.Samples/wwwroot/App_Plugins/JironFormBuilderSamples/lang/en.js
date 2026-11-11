// The samples' words in English (docs/extending.md, section 7). Umbraco's `localization` manifest loads this module's
// default export: an area of terms, which `#jfbSamples_<term>` names in a manifest or a setting's label, and
// `this.localize.term('jfbSamples_<term>')` in an element. lang/de.js has the same terms.
export default {
  jfbSamples: {
    colour: 'Colour',
    colours: 'Colours',
    coloursHelp: 'The colours a visitor chooses from, each with its name.',
    showNames: "Show the colours' names",
    addColour: 'Add a colour',
    newColour: 'New colour',
    colourName: (/** @type {number} */ position) => `Name of colour ${position}`,
    colourValue: (/** @type {string} */ name) => `Colour of ${name}`,
    removeColour: (/** @type {string} */ name) => `Remove ${name}`,
    noColours: 'No colours yet. A visitor needs at least one to choose from.',
    embed: 'Embed',
    embedHeadline: 'Place this form on a page',
    embedRazor: 'In a Razor view',
    embedRazorHelp: 'Where the form goes in a template or a partial view.',
    embedHeadless: 'On another site',
    embedHeadlessHelp:
      "For a headless site: turn on headless delivery in the form's Settings, and add the site's address to JironFormBuilder:PublicApi:AllowedOrigins in this site's settings.",
    copy: 'Copy',
    copied: 'Copied',
    copyFailed: 'Select the text and copy it yourself.',
    crm: 'Add to the CRM',
    crmEmail: 'Email address',
    crmName: 'Name',
    crmList: 'List',
    crmNewsletter: 'Newsletter',
    crmLeads: 'Leads',
    crmCustomers: 'Customers',
    products: "The shop's products",
    icalendar: 'iCalendar (.ics)',
    arithmetic: 'A sum to work out',
  },
};
