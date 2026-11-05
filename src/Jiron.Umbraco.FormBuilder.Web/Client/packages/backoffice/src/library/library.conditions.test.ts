// The library's conditions (ADR-0166): Rename... only in the tree's "...", Delete... only for a folder that holds nothing,
// and neither where its place says otherwise.
import { UmbBasicState } from '@umbraco-cms/backoffice/observable-api';
import { UMB_TREE_ITEM_CONTEXT } from '@umbraco-cms/backoffice/tree';
import { afterEach, describe, expect, it } from 'vitest';
import { settle, testHost } from '../testing';
import { FOLDER_SCREEN_CONTEXT } from './folder-screen.context';
import { EmptyFolderCondition, InTreeCondition } from './library.conditions';

type Condition = typeof InTreeCondition | typeof EmptyFolderCondition;

/** What a condition said, in order, under a host that provides what `provide` gives it. */
async function heard(condition: Condition, provide: (host: ReturnType<typeof testHost>) => void): Promise<boolean[]> {
  const said: boolean[] = [];
  const outer = testHost();
  provide(outer);
  const inner = testHost();
  outer.append(inner);
  new condition(inner, {
    manifest: {} as never,
    config: { alias: 'Jiron.FormBuilder.Condition.Test' },
    onChange: (permitted: boolean) => said.push(permitted),
  } as never);
  await settle();
  return said;
}

// Stand-ins for the contexts, which Umbraco asks for the element they belong to.
const treeItem = (hasChildren: UmbBasicState<boolean | undefined>) => (host: ReturnType<typeof testHost>) =>
  host.provideContext(UMB_TREE_ITEM_CONTEXT, {
    hasChildren: hasChildren.asObservable(),
    getHostElement: () => host,
  } as never);
const folderScreen = (isEmpty: UmbBasicState<boolean | undefined>) => (host: ReturnType<typeof testHost>) =>
  host.provideContext(FOLDER_SCREEN_CONTEXT, { isEmpty: isEmpty.asObservable(), getHostElement: () => host } as never);

describe('the library conditions', () => {
  afterEach(() => document.body.replaceChildren());

  it('show Rename... in the tree, and never on a folder screen', async () => {
    expect(await heard(InTreeCondition, treeItem(new UmbBasicState<boolean | undefined>(false)))).toEqual([true]);
    expect(await heard(InTreeCondition, folderScreen(new UmbBasicState<boolean | undefined>(true)))).toEqual([]);
  });

  it('show Delete... on a tree item without children, and hide it again once it has some', async () => {
    const children = new UmbBasicState<boolean | undefined>(true);
    const said: boolean[] = [];
    const outer = testHost();
    treeItem(children)(outer);
    const inner = testHost();
    outer.append(inner);
    new EmptyFolderCondition(inner, {
      manifest: {} as never,
      config: { alias: 'Jiron.FormBuilder.Condition.Test' },
      onChange: (permitted: boolean) => said.push(permitted),
    } as never);
    await settle();
    expect(said, 'a folder with children').toEqual([]);
    children.setValue(false);
    await settle();
    children.setValue(true);
    await settle();
    expect(said).toEqual([true, false]);
  });

  it('show Delete... on a folder screen only once its list is read and empty', async () => {
    expect(await heard(EmptyFolderCondition, folderScreen(new UmbBasicState<boolean | undefined>(true)))).toEqual([
      true,
    ]);
    expect(await heard(EmptyFolderCondition, folderScreen(new UmbBasicState<boolean | undefined>(false)))).toEqual([]);
    expect(await heard(EmptyFolderCondition, folderScreen(new UmbBasicState<boolean | undefined>(undefined)))).toEqual(
      [],
    );
    expect(await heard(EmptyFolderCondition, treeItem(new UmbBasicState<boolean | undefined>(undefined)))).toEqual([]);
  });
});
