// How an action is named and what it does in one line (IA 5.3), as its card in the Actions view says it and as the
// preview's Run actions... lists it (ADR-0204): the same words in both places.
import type { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import type { ActionTypeResponseModel } from '../../api';
import { summaryOf, type DesignAction } from './action-model';

/** What the words need: the site's action types, and the backoffice's words in the user's language. */
export interface ActionWords {
  readonly types: readonly ActionTypeResponseModel[] | undefined;
  readonly localize: Pick<UmbLocalizationController, 'term' | 'string' | 'lang'>;
}

/** The type of an action, among the site's. */
export const typeOf = (words: ActionWords, alias: string): ActionTypeResponseModel | undefined =>
  words.types?.find((type) => type.alias === alias);

/** An action's name as it shows: its own, else its type's, else its type's alias. */
export function actionName(words: ActionWords, action: DesignAction): string {
  const typeName = typeOf(words, action.type)?.name;
  return action.name?.trim() || (typeName ? words.localize.string(typeName) : '') || action.type;
}

/**
 * What an action does in one line, as its card says it; empty for an action whose settings say nothing yet. A content
 * action's document type is named as the site names it, among the choices its type serves.
 * @param names The form's elements' names, by their keys, which an email to a field's answer says.
 */
export function actionLine(words: ActionWords, action: DesignAction, names: Readonly<Record<string, string>>): string {
  const choices = typeOf(words, 'content')?.settings.find((setting) => setting.name === 'documentType')?.choices ?? [];
  const summary = summaryOf(
    action,
    (key) => names[key],
    (alias) => choices.find((choice) => choice.value === alias)?.label,
  );
  if (!summary) return '';
  const list = new Intl.ListFormat(words.localize.lang(), { type: 'conjunction' });
  return words.localize.term(
    `jfbActions_${summary.term}`,
    ...summary.args.map((arg) => (Array.isArray(arg) ? list.format(arg as string[]) : arg)),
  );
}
