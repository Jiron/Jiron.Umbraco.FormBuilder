// The package's own theme says its name in the backoffice's language ("Default", "Standard"), as its name is a word
// rather than a name; a site's theme and the Bootstrap 5 theme keep the label they register (the German walk of 14.1).

/** The alias of the package's default theme. */
export const DEFAULT_THEME_ALIAS = 'default';

/** A theme with the default theme's label said in the language shown; any other theme as it is. */
export function withThemeLabel<T extends { readonly alias: string; readonly label: string }>(
  theme: T,
  term: (key: string) => string,
): T {
  return theme.alias === DEFAULT_THEME_ALIAS ? { ...theme, label: term('jfbSection_defaultTheme') } : theme;
}
