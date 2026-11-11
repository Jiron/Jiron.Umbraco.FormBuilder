// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The culture formats the vector suites print with (ADR-0064): data written for the vectors, which pins the formatter
// and never any platform's culture data. The merge tags' suite and the messages' suite share them (ADR-0099). Text with
// code points is written as {hex}, so this file stays ASCII.

const BS = String.fromCharCode(92);
const x = (text) => text.replace(/\{([0-9a-f]{2,6})\}/g, (_, hex) => String.fromCodePoint(Number.parseInt(hex, 16)));
const NBSP = x('{a0}');
const NNBSP = x('{202f}');
const QUOTE = x('{2019}');
const MINUS = x('{2212}');
const words = (text) => x(text).split(' ');
const englishMonths = words('January February March April May June July August September October November December');
const englishShortMonths = words('Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec');
const englishDays = words('Sunday Monday Tuesday Wednesday Thursday Friday Saturday');
const englishShortDays = words('Sun Mon Tue Wed Thu Fri Sat');

/** A culture's formats, with genitive month names like the nominative ones unless given. */
function culture(formats) {
  return {
    monthsGenitive: formats.months,
    monthsShortGenitive: formats.monthsShort,
    am: 'AM',
    pm: 'PM',
    minus: '-',
    ...formats,
  };
}

const germanMonths = words('Januar Februar M{e4}rz April Mai Juni Juli August September Oktober November Dezember');
const germanShortMonths = words('Jan. Feb. M{e4}rz Apr. Mai Juni Juli Aug. Sept. Okt. Nov. Dez.');

/** The cultures by name. */
export const CULTURES = {
  'de-CH': culture({
    dates: {
      short: 'dd.MM.yyyy',
      medium: 'd. MMM yyyy',
      long: 'dddd, d. MMMM yyyy',
      time: 'HH:mm',
      dateTime: 'dd.MM.yyyy HH:mm',
    },
    months: germanMonths,
    monthsShort: germanShortMonths,
    days: words('Sonntag Montag Dienstag Mittwoch Donnerstag Freitag Samstag'),
    daysShort: words('So. Mo. Di. Mi. Do. Fr. Sa.'),
    number: { decimal: '.', group: QUOTE, sizes: [3], negative: '-n' },
    currency: {
      code: 'CHF',
      symbol: 'CHF',
      decimal: '.',
      group: QUOTE,
      sizes: [3],
      positive: '$ n',
      negative: '$-n',
    },
  }),
  'en-US': culture({
    dates: {
      short: 'M/d/yyyy',
      medium: 'MMM d, yyyy',
      long: 'dddd, MMMM d, yyyy',
      time: 'h:mm tt',
      dateTime: 'M/d/yyyy h:mm tt',
    },
    months: englishMonths,
    monthsShort: englishShortMonths,
    days: englishDays,
    daysShort: englishShortDays,
    number: { decimal: '.', group: ',', sizes: [3], negative: '-n' },
    currency: { code: 'USD', symbol: '$', decimal: '.', group: ',', sizes: [3], positive: '$n', negative: '-$n' },
  }),
  'fr-FR': culture({
    dates: {
      short: 'dd/MM/yyyy',
      medium: 'd MMM yyyy',
      long: 'dddd d MMMM yyyy',
      time: 'HH:mm',
      dateTime: 'dd/MM/yyyy HH:mm',
    },
    months: words('janvier f{e9}vrier mars avril mai juin juillet ao{fb}t septembre octobre novembre d{e9}cembre'),
    monthsShort: words('janv. f{e9}vr. mars avr. mai juin juil. ao{fb}t sept. oct. nov. d{e9}c.'),
    days: words('dimanche lundi mardi mercredi jeudi vendredi samedi'),
    daysShort: words('dim. lun. mar. mer. jeu. ven. sam.'),
    number: { decimal: ',', group: NNBSP, sizes: [3], negative: '-n' },
    currency: {
      code: 'EUR',
      symbol: x('{20ac}'),
      decimal: ',',
      group: NNBSP,
      sizes: [3],
      positive: 'n' + NBSP + '$',
      negative: '-n' + NBSP + '$',
    },
  }),
  'it-IT': culture({
    dates: { short: 'dd/MM/yyyy', medium: 'd MMM yyyy', long: 'dddd d MMMM yyyy', time: 'HH:mm', dateTime: 'dd/MM/yyyy HH:mm' },
    months: words('gennaio febbraio marzo aprile maggio giugno luglio agosto settembre ottobre novembre dicembre'),
    monthsShort: words('gen feb mar apr mag giu lug ago set ott nov dic'),
    days: words('domenica luned{ec} marted{ec} mercoled{ec} gioved{ec} venerd{ec} sabato'),
    daysShort: words('dom lun mar mer gio ven sab'),
    number: { decimal: ',', group: '.', sizes: [3], negative: '-n' },
    currency: {
      code: 'EUR',
      symbol: x('{20ac}'),
      decimal: ',',
      group: '.',
      sizes: [3],
      positive: 'n' + NBSP + '$',
      negative: '-n' + NBSP + '$',
    },
  }),
  'nl-NL': culture({
    dates: { short: 'dd-MM-yyyy', medium: 'd MMM yyyy', long: 'dddd d MMMM yyyy', time: 'HH:mm', dateTime: 'dd-MM-yyyy HH:mm' },
    months: words('januari februari maart april mei juni juli augustus september oktober november december'),
    monthsShort: words('jan feb mrt apr mei jun jul aug sep okt nov dec'),
    days: words('zondag maandag dinsdag woensdag donderdag vrijdag zaterdag'),
    daysShort: words('zo ma di wo do vr za'),
    number: { decimal: ',', group: '.', sizes: [3], negative: '-n' },
    currency: {
      code: 'EUR',
      symbol: x('{20ac}'),
      decimal: ',',
      group: '.',
      sizes: [3],
      positive: '$' + NBSP + 'n',
      negative: '$' + NBSP + '-n',
    },
  }),
  'es-ES': culture({
    dates: {
      short: 'dd/MM/yyyy',
      medium: 'd MMM yyyy',
      long: "dddd, d 'de' MMMM 'de' yyyy",
      time: 'H:mm',
      dateTime: 'dd/MM/yyyy H:mm',
    },
    months: words('enero febrero marzo abril mayo junio julio agosto septiembre octubre noviembre diciembre'),
    monthsShort: words('ene feb mar abr may jun jul ago sept oct nov dic'),
    days: words('domingo lunes martes mi{e9}rcoles jueves viernes s{e1}bado'),
    daysShort: words('dom lun mar mi{e9} jue vie s{e1}b'),
    number: { decimal: ',', group: '.', sizes: [3], negative: '-n' },
    currency: {
      code: 'EUR',
      symbol: x('{20ac}'),
      decimal: ',',
      group: '.',
      sizes: [3],
      positive: 'n' + NBSP + '$',
      negative: '-n' + NBSP + '$',
    },
  }),
  'da-DK': culture({
    dates: {
      short: 'dd.MM.yyyy',
      medium: 'd. MMM yyyy',
      long: "dddd 'den' d. MMMM yyyy",
      time: 'HH.mm',
      dateTime: 'dd.MM.yyyy HH.mm',
    },
    months: words('januar februar marts april maj juni juli august september oktober november december'),
    monthsShort: words('jan. feb. mar. apr. maj jun. jul. aug. sep. okt. nov. dec.'),
    days: words('s{f8}ndag mandag tirsdag onsdag torsdag fredag l{f8}rdag'),
    daysShort: words('s{f8}n. man. tirs. ons. tors. fre. l{f8}r.'),
    number: { decimal: ',', group: '.', sizes: [3], negative: '-n' },
    currency: {
      code: 'DKK',
      symbol: 'kr.',
      decimal: ',',
      group: '.',
      sizes: [3],
      positive: 'n' + NBSP + '$',
      negative: '-n' + NBSP + '$',
    },
  }),
  'pl-PL': culture({
    dates: {
      short: 'dd.MM.yyyy',
      medium: 'd MMM yyyy',
      long: 'dddd, d MMMM yyyy',
      time: 'HH:mm',
      dateTime: 'dd.MM.yyyy HH:mm',
    },
    months: words(
      'stycze{144} luty marzec kwiecie{144} maj czerwiec lipiec sierpie{144} wrzesie{144} pa{17a}dziernik listopad grudzie{144}',
    ),
    monthsShort: words('sty lut mar kwi maj cze lip sie wrz pa{17a} lis gru'),
    monthsGenitive: words(
      'stycznia lutego marca kwietnia maja czerwca lipca sierpnia wrze{15b}nia pa{17a}dziernika listopada grudnia',
    ),
    days: words('niedziela poniedzia{142}ek wtorek {15b}roda czwartek pi{105}tek sobota'),
    daysShort: words('niedz. pon. wt. {15b}r. czw. pt. sob.'),
    number: { decimal: ',', group: NBSP, sizes: [3], negative: '-n' },
    currency: {
      code: 'PLN',
      symbol: x('z{142}'),
      decimal: ',',
      group: NBSP,
      sizes: [3],
      positive: 'n' + NBSP + '$',
      negative: '-n' + NBSP + '$',
    },
  }),
  'hi-IN': culture({
    dates: { short: 'dd-MM-yyyy', medium: 'dd-MMM-yyyy', long: 'dd MMMM yyyy', time: 'HH:mm', dateTime: 'dd-MM-yyyy HH:mm' },
    months: englishMonths,
    monthsShort: englishShortMonths,
    days: englishDays,
    daysShort: englishShortDays,
    number: { decimal: '.', group: ',', sizes: [3, 2], negative: '-n' },
    currency: { code: 'INR', symbol: x('{20b9}'), decimal: '.', group: ',', sizes: [3, 2], positive: '$n', negative: '-$n' },
  }),
  'sv-SE': culture({
    dates: { short: 'yyyy-MM-dd', medium: 'd MMM yyyy', long: 'dddd d MMMM yyyy', time: 'HH:mm', dateTime: 'yyyy-MM-dd HH:mm' },
    months: words('januari februari mars april maj juni juli augusti september oktober november december'),
    monthsShort: words('jan. feb. mars apr. maj juni juli aug. sep. okt. nov. dec.'),
    days: words('s{f6}ndag m{e5}ndag tisdag onsdag torsdag fredag l{f6}rdag'),
    daysShort: words('s{f6}n m{e5}n tis ons tors fre l{f6}r'),
    minus: MINUS,
    number: { decimal: ',', group: NBSP, sizes: [3], negative: '-n' },
    currency: {
      code: 'SEK',
      symbol: 'kr',
      decimal: ',',
      group: NBSP,
      sizes: [3],
      positive: 'n' + NBSP + '$',
      negative: '-n' + NBSP + '$',
    },
  }),
  // Every token of the pattern subset, genitive names unlike the nominative ones, and the number patterns' edges.
  'x-patterns': culture({
    dates: {
      short: 'd dd ddd dddd|M MM MMM MMMM|y yy yyy yyyy yyyyy',
      medium: 'y yy yyy|MMM MMMM|ddd dddd',
      long: "'Day' d 'of' MMMM, MMM",
      time: 'h hh H HH m mm s ss t tt',
      dateTime: BS + 'd "at" HH:mm ' + "'o" + BS + "'clock'" + ' gz/:% ' + "'open",
    },
    months: englishMonths,
    monthsShort: englishShortMonths,
    monthsGenitive: englishMonths.map((month) => month + '-g'),
    monthsShortGenitive: englishShortMonths.map((month) => month + '-g'),
    days: englishDays,
    daysShort: englishShortDays,
    am: 'vorm.',
    pm: 'nachm.',
    number: { decimal: '.', group: ' ', sizes: [3, 0], negative: '(n)' },
    currency: {
      code: 'XTS',
      symbol: x('{a4}'),
      decimal: '.',
      group: ' ',
      sizes: [3, 0],
      positive: 'n$',
      negative: '($n)',
    },
  }),
  // Names missing and no grouping: what wrong data prints.
  'x-incomplete': culture({
    dates: { short: 'dd.MM.yyyy', medium: 'MMM', long: 'MMMM MMM dddd ddd', time: 't tt|h', dateTime: 'dd.MM.yyyy' },
    months: [],
    monthsShort: ['Jan'],
    monthsGenitive: [],
    monthsShortGenitive: [],
    days: [],
    daysShort: [],
    am: '',
    pm: '',
    number: { decimal: '.', group: ',', sizes: [], negative: 'n-' },
    currency: { code: 'XXX', symbol: '', decimal: '.', group: ',', sizes: [0], positive: '$n', negative: '-$n' },
  }),
};
