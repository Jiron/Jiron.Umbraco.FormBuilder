// The tag picker's texts in English (ADR-0173): its words, a heading for each built-in source, and a name for each
// built-in path the merge tag paths endpoint serves (docs/api/merge-tag-paths.json holds them; a path's key is its dots
// as underscores). A field's path shows the field's label, a site's own path what the site labels it.
// merge-tags.de.ts has the same keys.
export default {
  jfbMergeTags: {
    insert: 'Insert merge tag',
    find: 'Find a merge tag',
    none: 'No merge tag matches.',
    loading: 'Reading the merge tags',
    failed: "The merge tags couldn't be read.",
    source_field: 'Answers',
    source_entry: 'The entry',
    source_form: 'The form',
    source_page: 'The page',
    source_member: 'The member',
    source_query: "The page's address",
    source_context: 'The context',
    source_site: 'The site',
    source_date: 'Date and time',
    source_dictionary: 'Dictionary',
    source_own: "The site's own",
    path_form_name: 'Name of the form',
    path_form_alias: 'Alias of the form',
    path_page_name: 'Name of the page',
    path_page_url: 'Address of the page',
    path_site_name: 'Name of the site',
    path_site_url: 'Address of the site',
    path_date_now: 'Now',
  },
};
