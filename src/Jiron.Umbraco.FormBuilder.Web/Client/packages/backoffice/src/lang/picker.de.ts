// The form picker's texts in German (ADR-0129), in Umbraco's own German terms (docs/ux/copy.md). The type below holds
// them to the keys of picker.en.ts, every one and no other.
import type pickerEn from './picker.en';

export default {
  jfbPicker: {
    notAKey: 'Dieser Wert ist kein Schlüssel. Wählen Sie ihn erneut aus der Liste.',
    oneOnly: 'Diese Auswahl nimmt ein Formular. Entfernen Sie die anderen.',
    noSuchForm: 'Ein ausgewähltes Formular gibt es nicht mehr, oder es liegt im Papierkorb. Wählen Sie ein anderes.',
    notAllowed:
      'Diese Auswahl erlaubt ein ausgewähltes Formular nicht. Wählen Sie eines aus ihren Ordnern oder Formularen.',
    noSuchTheme: 'Die Website hat kein Theme mit diesem Namen.',
    tooMany:
      'Eine Auswahl kann höchstens 1.000 davon erlauben. Entfernen Sie einige, oder erlauben Sie einen Ordner, der sie enthält.',
  },
} satisfies { [Area in keyof typeof pickerEn]: Record<keyof (typeof pickerEn)[Area], string> };
