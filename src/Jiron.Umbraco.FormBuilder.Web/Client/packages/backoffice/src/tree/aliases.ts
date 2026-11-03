// The trees' aliases, which the manifests register and the pickers open, without importing the manifests.

/** The forms tree. */
export const LIBRARY_TREE_ALIAS = 'Jiron.FormBuilder.Tree.Library';
export const LIBRARY_TREE_REPOSITORY_ALIAS = 'Jiron.FormBuilder.Repository.LibraryTree';

/** The Recycle Bin's tree. */
export const RECYCLE_BIN_TREE_ALIAS = 'Jiron.FormBuilder.Tree.RecycleBin';
export const RECYCLE_BIN_TREE_REPOSITORY_ALIAS = 'Jiron.FormBuilder.Repository.RecycleBinTree';

/** The entity flag of a restricted folder or form, which a lock sign shows. */
export const RESTRICTED_FLAG = 'Jiron.FormBuilder.EntityFlag.Restricted';

/** The entity flag of a form with entries nobody has opened, which a dot sign shows. */
export const UNSEEN_FLAG = 'Jiron.FormBuilder.EntityFlag.Unseen';

/** The library's conditions: an action only in the tree's "...", and one only for a folder that holds nothing the user sees. */
export const IN_TREE_CONDITION = 'Jiron.FormBuilder.Condition.InTree';
export const EMPTY_FOLDER_CONDITION = 'Jiron.FormBuilder.Condition.EmptyFolder';
