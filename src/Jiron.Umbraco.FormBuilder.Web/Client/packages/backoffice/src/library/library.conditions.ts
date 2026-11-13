// The library's conditions (docs/ux/information-architecture.md, section 3; ADR-0166). In the tree: Rename... shows only in the tree's
// "...", since a folder's screen renames in its header. Empty folder: Delete... shows only for a folder that holds nothing the user
// sees, as its tree item or its screen says; the server still refuses one that holds anything else.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type {
  UmbConditionConfigBase,
  UmbConditionControllerArguments,
  UmbExtensionCondition,
} from '@umbraco-cms/backoffice/extension-api';
import { UmbConditionBase } from '@umbraco-cms/backoffice/extension-registry';
import { UMB_TREE_ITEM_BASE_CONTEXT } from '@umbraco-cms/backoffice/tree';
import { FOLDER_SCREEN_CONTEXT } from './folder-screen.context';

export class InTreeCondition extends UmbConditionBase<UmbConditionConfigBase> implements UmbExtensionCondition {
  constructor(host: UmbControllerHost, args: UmbConditionControllerArguments<UmbConditionConfigBase>) {
    super(host, args);
    this.consumeContext(UMB_TREE_ITEM_BASE_CONTEXT, (context) => {
      this.permitted = context !== undefined;
    });
  }
}

export class EmptyFolderCondition extends UmbConditionBase<UmbConditionConfigBase> implements UmbExtensionCondition {
  constructor(host: UmbControllerHost, args: UmbConditionControllerArguments<UmbConditionConfigBase>) {
    super(host, args);
    this.consumeContext(UMB_TREE_ITEM_BASE_CONTEXT, (context) => {
      this.observe(context?.hasChildren, (hasChildren) => (this.permitted = hasChildren === false), 'treeItem');
    });
    this.consumeContext(FOLDER_SCREEN_CONTEXT, (context) => {
      this.observe(context?.isEmpty, (isEmpty) => (this.permitted = isEmpty === true), 'screen');
    });
  }
}
