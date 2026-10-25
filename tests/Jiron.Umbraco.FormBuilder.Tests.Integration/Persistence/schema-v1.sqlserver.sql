-- Schema v1 on SQL Server: the statements of step 1 (CreateSchemaV1) on an empty database, as Umbraco's
-- SQL Server syntax provider renders them (ROADMAP 4.1, ADR-0033). Written by SchemaV1DdlTests with
-- JFB_UPDATE_SNAPSHOTS=1; review every change.

CREATE TABLE [jfbKeyValue] ([key] NVARCHAR(255) NOT NULL,
[value] NVARCHAR(MAX) NULL,
[updatedUtc] DATETIME NOT NULL);

ALTER TABLE [jfbKeyValue] ADD CONSTRAINT [PK_jfbKeyValue] PRIMARY KEY CLUSTERED ([key]);

CREATE TABLE [jfbFolder] ([id] INTEGER NOT NULL IDENTITY(1,1),
[key] UniqueIdentifier NOT NULL,
[parentId] INTEGER NULL,
[name] NVARCHAR(255) NOT NULL,
[sortOrder] INTEGER NOT NULL,
[isRestricted] BIT NOT NULL,
[trashedUtc] DATETIME NULL,
[trashKey] UniqueIdentifier NULL,
[createdUtc] DATETIME NOT NULL,
[updatedUtc] DATETIME NOT NULL,
[createdByUserKey] UniqueIdentifier NULL,
[updatedByUserKey] UniqueIdentifier NULL);

ALTER TABLE [jfbFolder] ADD CONSTRAINT [PK_jfbFolder] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbFolder_key] ON [jfbFolder] ([key]);

CREATE NONCLUSTERED INDEX [IX_jfbFolder_parentId] ON [jfbFolder] ([parentId]);

ALTER TABLE [jfbFolder] WITH CHECK ADD CONSTRAINT [FK_jfbFolder_jfbFolder] FOREIGN KEY ([parentId]) REFERENCES [jfbFolder] ([id]);

CREATE TABLE [jfbForm] ([id] INTEGER NOT NULL IDENTITY(1,1),
[key] UniqueIdentifier NOT NULL,
[folderId] INTEGER NULL,
[alias] NVARCHAR(64) NOT NULL,
[name] NVARCHAR(255) NOT NULL,
[draftVersionId] INTEGER NULL,
[publishedVersionId] INTEGER NULL,
[publicRevision] INTEGER NOT NULL,
[concurrencyVersion] INTEGER NOT NULL,
[lastEntryNumber] INTEGER NOT NULL,
[themeAlias] NVARCHAR(64) NULL,
[isRestricted] BIT NOT NULL,
[trashedUtc] DATETIME NULL,
[trashKey] UniqueIdentifier NULL,
[createdUtc] DATETIME NOT NULL,
[updatedUtc] DATETIME NOT NULL,
[createdByUserKey] UniqueIdentifier NULL,
[updatedByUserKey] UniqueIdentifier NULL);

ALTER TABLE [jfbForm] ADD CONSTRAINT [PK_jfbForm] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbForm_key] ON [jfbForm] ([key]);

CREATE NONCLUSTERED INDEX [IX_jfbForm_folderId] ON [jfbForm] ([folderId]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbForm_alias] ON [jfbForm] ([alias]);

CREATE NONCLUSTERED INDEX [IX_jfbForm_trashedUtc] ON [jfbForm] ([trashedUtc]);

ALTER TABLE [jfbForm] WITH CHECK ADD CONSTRAINT [FK_jfbForm_jfbFolder] FOREIGN KEY ([folderId]) REFERENCES [jfbFolder] ([id]);

CREATE TABLE [jfbFormVersion] ([id] INTEGER NOT NULL IDENTITY(1,1),
[formId] INTEGER NOT NULL,
[number] INTEGER NOT NULL,
[kind] NVARCHAR(16) NOT NULL,
[schemaVersion] INTEGER NOT NULL,
[definition] NVARCHAR(MAX) NOT NULL,
[definitionHash] NVARCHAR(64) NOT NULL,
[publicHash] NVARCHAR(64) NOT NULL,
[publicRevision] INTEGER NULL,
[comment] NVARCHAR(500) NULL,
[isPinned] BIT NOT NULL,
[createdUtc] DATETIME NOT NULL,
[createdByUserKey] UniqueIdentifier NULL,
[publishedUtc] DATETIME NULL,
[publishedByUserKey] UniqueIdentifier NULL);

ALTER TABLE [jfbFormVersion] ADD CONSTRAINT [PK_jfbFormVersion] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbFormVersion_formId_number] ON [jfbFormVersion] ([formId],[number]);

ALTER TABLE [jfbFormVersion] WITH CHECK ADD CONSTRAINT [FK_jfbFormVersion_jfbForm] FOREIGN KEY ([formId]) REFERENCES [jfbForm] ([id]) ON DELETE CASCADE;

CREATE TABLE [jfbFormField] ([id] INTEGER NOT NULL IDENTITY(1,1),
[formId] INTEGER NOT NULL,
[fieldKey] UniqueIdentifier NOT NULL,
[alias] NVARCHAR(64) NOT NULL,
[label] NVARCHAR(500) NOT NULL,
[fieldType] NVARCHAR(128) NOT NULL,
[storageKind] NVARCHAR(16) NOT NULL,
[isSensitive] BIT NOT NULL,
[sortOrder] INTEGER NOT NULL,
[firstVersionNumber] INTEGER NULL,
[lastVersionNumber] INTEGER NULL,
[parentFieldKey] UniqueIdentifier NULL);

ALTER TABLE [jfbFormField] ADD CONSTRAINT [PK_jfbFormField] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbFormField_formId_fieldKey] ON [jfbFormField] ([formId],[fieldKey]);

ALTER TABLE [jfbFormField] WITH CHECK ADD CONSTRAINT [FK_jfbFormField_jfbForm] FOREIGN KEY ([formId]) REFERENCES [jfbForm] ([id]) ON DELETE CASCADE;

CREATE TABLE [jfbEntry] ([id] BIGINT NOT NULL IDENTITY(1,1),
[key] UniqueIdentifier NOT NULL,
[formId] INTEGER NOT NULL,
[formVersionId] INTEGER NOT NULL,
[number] INTEGER NOT NULL,
[state] NVARCHAR(16) NOT NULL,
[isTest] BIT NOT NULL,
[submissionId] UniqueIdentifier NOT NULL,
[submissionHash] NVARCHAR(64) NOT NULL,
[culture] NVARCHAR(32) NOT NULL,
[contentKey] UniqueIdentifier NULL,
[pageUrl] NVARCHAR(2048) NULL,
[memberKey] UniqueIdentifier NULL,
[ipAddress] NVARCHAR(64) NULL,
[spamScore] INTEGER NOT NULL,
[spamReasons] NVARCHAR(1000) NOT NULL,
[searchText] NVARCHAR(MAX) NOT NULL,
[context] NVARCHAR(MAX) NOT NULL,
[durationSeconds] INTEGER NULL,
[submittedUtc] DATETIME NOT NULL,
[updatedUtc] DATETIME NOT NULL,
[stateChangedUtc] DATETIME NOT NULL,
[stateChangedByUserKey] UniqueIdentifier NULL,
[firstSeenUtc] DATETIME NULL,
[verifiedUtc] DATETIME NULL,
[resumeTokenHash] NVARCHAR(64) NULL,
[resumeExpiresUtc] DATETIME NULL,
[currentPageKey] UniqueIdentifier NULL,
[challengeId] NVARCHAR(64) NULL,
[holdUntilUtc] DATETIME NULL,
[holdReason] NVARCHAR(500) NULL,
[purgeAfterUtc] DATETIME NULL);

ALTER TABLE [jfbEntry] ADD CONSTRAINT [PK_jfbEntry] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbEntry_key] ON [jfbEntry] ([key]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbEntry_formId_number] ON [jfbEntry] ([formId],[number]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbEntry_formId_submissionId] ON [jfbEntry] ([formId],[submissionId]);

CREATE NONCLUSTERED INDEX [IX_jfbEntry_formId_submittedUtc] ON [jfbEntry] ([formId],[submittedUtc]);

CREATE NONCLUSTERED INDEX [IX_jfbEntry_formId_state_submittedUtc] ON [jfbEntry] ([formId],[state],[submittedUtc]);

CREATE NONCLUSTERED INDEX [IX_jfbEntry_formVersionId] ON [jfbEntry] ([formVersionId]);

CREATE NONCLUSTERED INDEX [IX_jfbEntry_memberKey] ON [jfbEntry] ([memberKey]);

CREATE NONCLUSTERED INDEX [IX_jfbEntry_purgeAfterUtc] ON [jfbEntry] ([purgeAfterUtc]);

ALTER TABLE [jfbEntry] WITH CHECK ADD CONSTRAINT [FK_jfbEntry_jfbForm] FOREIGN KEY ([formId]) REFERENCES [jfbForm] ([id]);

ALTER TABLE [jfbEntry] WITH CHECK ADD CONSTRAINT [FK_jfbEntry_jfbFormVersion] FOREIGN KEY ([formVersionId]) REFERENCES [jfbFormVersion] ([id]);

CREATE TABLE [jfbEntryFile] ([id] INTEGER NOT NULL IDENTITY(1,1),
[key] UniqueIdentifier NOT NULL,
[entryId] BIGINT NULL,
[formId] INTEGER NOT NULL,
[fieldKey] UniqueIdentifier NOT NULL,
[originalName] NVARCHAR(255) NOT NULL,
[storedName] NVARCHAR(64) NOT NULL,
[contentType] NVARCHAR(255) NOT NULL,
[sizeBytes] BIGINT NOT NULL,
[fileHash] NVARCHAR(64) NOT NULL,
[scanState] NVARCHAR(16) NOT NULL,
[createdUtc] DATETIME NOT NULL);

ALTER TABLE [jfbEntryFile] ADD CONSTRAINT [PK_jfbEntryFile] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbEntryFile_key] ON [jfbEntryFile] ([key]);

CREATE NONCLUSTERED INDEX [IX_jfbEntryFile_entryId_createdUtc] ON [jfbEntryFile] ([entryId],[createdUtc]);

ALTER TABLE [jfbEntryFile] WITH CHECK ADD CONSTRAINT [FK_jfbEntryFile_jfbEntry] FOREIGN KEY ([entryId]) REFERENCES [jfbEntry] ([id]) ON DELETE CASCADE;

CREATE TABLE [jfbEntryValue] ([id] BIGINT NOT NULL IDENTITY(1,1),
[entryId] BIGINT NOT NULL,
[fieldId] INTEGER NOT NULL,
[rowIndex] INTEGER NOT NULL,
[valueIndex] INTEGER NOT NULL,
[text] NVARCHAR(MAX) NULL,
[textKey] NVARCHAR(256) NULL,
[number] FLOAT NULL,
[date] DATETIME NULL,
[bool] BIT NULL,
[fileId] INTEGER NULL);

ALTER TABLE [jfbEntryValue] ADD CONSTRAINT [PK_jfbEntryValue] PRIMARY KEY CLUSTERED ([id]);

CREATE NONCLUSTERED INDEX [IX_jfbEntryValue_entryId_fieldId] ON [jfbEntryValue] ([entryId],[fieldId]);

CREATE NONCLUSTERED INDEX [IX_jfbEntryValue_fieldId_textKey_entryId] ON [jfbEntryValue] ([fieldId],[textKey],[entryId]);

CREATE NONCLUSTERED INDEX [IX_jfbEntryValue_fieldId_number_entryId] ON [jfbEntryValue] ([fieldId],[number],[entryId]);

CREATE NONCLUSTERED INDEX [IX_jfbEntryValue_fieldId_date_entryId] ON [jfbEntryValue] ([fieldId],[date],[entryId]);

ALTER TABLE [jfbEntryValue] WITH CHECK ADD CONSTRAINT [FK_jfbEntryValue_jfbEntry] FOREIGN KEY ([entryId]) REFERENCES [jfbEntry] ([id]) ON DELETE CASCADE;

ALTER TABLE [jfbEntryValue] WITH CHECK ADD CONSTRAINT [FK_jfbEntryValue_jfbFormField] FOREIGN KEY ([fieldId]) REFERENCES [jfbFormField] ([id]);

ALTER TABLE [jfbEntryValue] WITH CHECK ADD CONSTRAINT [FK_jfbEntryValue_jfbEntryFile] FOREIGN KEY ([fileId]) REFERENCES [jfbEntryFile] ([id]);

CREATE TABLE [jfbConsentText] ([textHash] NVARCHAR(64) NOT NULL,
[culture] NVARCHAR(32) NOT NULL,
[text] NVARCHAR(MAX) NOT NULL,
[firstSeenUtc] DATETIME NOT NULL);

ALTER TABLE [jfbConsentText] ADD CONSTRAINT [PK_jfbConsentText] PRIMARY KEY CLUSTERED ([textHash]);

CREATE TABLE [jfbEntryEvent] ([id] BIGINT NOT NULL IDENTITY(1,1),
[entryId] BIGINT NOT NULL,
[occurredUtc] DATETIME NOT NULL,
[kind] NVARCHAR(64) NOT NULL,
[userKey] UniqueIdentifier NULL,
[changeKey] UniqueIdentifier NULL,
[details] NVARCHAR(MAX) NOT NULL);

ALTER TABLE [jfbEntryEvent] ADD CONSTRAINT [PK_jfbEntryEvent] PRIMARY KEY CLUSTERED ([id]);

CREATE NONCLUSTERED INDEX [IX_jfbEntryEvent_entryId_occurredUtc] ON [jfbEntryEvent] ([entryId],[occurredUtc]);

CREATE NONCLUSTERED INDEX [IX_jfbEntryEvent_changeKey] ON [jfbEntryEvent] ([changeKey]);

ALTER TABLE [jfbEntryEvent] WITH CHECK ADD CONSTRAINT [FK_jfbEntryEvent_jfbEntry] FOREIGN KEY ([entryId]) REFERENCES [jfbEntry] ([id]) ON DELETE CASCADE;

CREATE TABLE [jfbEntryNote] ([id] INTEGER NOT NULL IDENTITY(1,1),
[entryId] BIGINT NOT NULL,
[userKey] UniqueIdentifier NOT NULL,
[body] NVARCHAR(MAX) NOT NULL,
[createdUtc] DATETIME NOT NULL,
[updatedUtc] DATETIME NOT NULL);

ALTER TABLE [jfbEntryNote] ADD CONSTRAINT [PK_jfbEntryNote] PRIMARY KEY CLUSTERED ([id]);

CREATE NONCLUSTERED INDEX [IX_jfbEntryNote_entryId] ON [jfbEntryNote] ([entryId]);

ALTER TABLE [jfbEntryNote] WITH CHECK ADD CONSTRAINT [FK_jfbEntryNote_jfbEntry] FOREIGN KEY ([entryId]) REFERENCES [jfbEntry] ([id]) ON DELETE CASCADE;

CREATE TABLE [jfbActionRun] ([id] BIGINT NOT NULL IDENTITY(1,1),
[entryId] BIGINT NOT NULL,
[formVersionId] INTEGER NOT NULL,
[actionKey] UniqueIdentifier NOT NULL,
[actionType] NVARCHAR(128) NOT NULL,
[triggerKind] NVARCHAR(16) NOT NULL,
[sortOrder] INTEGER NOT NULL,
[stopOnFailure] BIT NOT NULL,
[state] NVARCHAR(16) NOT NULL,
[attempts] INTEGER NOT NULL,
[nextAttemptUtc] DATETIME NULL,
[changeKey] UniqueIdentifier NULL,
[lockedUntilUtc] DATETIME NULL,
[lockedBy] NVARCHAR(255) NULL,
[lastErrorCode] NVARCHAR(64) NULL,
[lastErrorMessage] NVARCHAR(1000) NULL,
[createdUtc] DATETIME NOT NULL,
[startedUtc] DATETIME NULL,
[finishedUtc] DATETIME NULL);

ALTER TABLE [jfbActionRun] ADD CONSTRAINT [PK_jfbActionRun] PRIMARY KEY CLUSTERED ([id]);

CREATE NONCLUSTERED INDEX [IX_jfbActionRun_entryId_sortOrder] ON [jfbActionRun] ([entryId],[sortOrder]);

CREATE NONCLUSTERED INDEX [IX_jfbActionRun_state_nextAttemptUtc] ON [jfbActionRun] ([state],[nextAttemptUtc]);

CREATE NONCLUSTERED INDEX [IX_jfbActionRun_changeKey] ON [jfbActionRun] ([changeKey]);

ALTER TABLE [jfbActionRun] WITH CHECK ADD CONSTRAINT [FK_jfbActionRun_jfbEntry] FOREIGN KEY ([entryId]) REFERENCES [jfbEntry] ([id]) ON DELETE CASCADE;

CREATE TABLE [jfbEmail] ([id] BIGINT NOT NULL IDENTITY(1,1),
[kind] NVARCHAR(16) NOT NULL,
[entryId] BIGINT NULL,
[actionRunId] BIGINT NULL,
[toAddresses] NVARCHAR(4000) NOT NULL,
[ccAddresses] NVARCHAR(4000) NULL,
[bccAddresses] NVARCHAR(4000) NULL,
[replyToAddress] NVARCHAR(500) NULL,
[fromAddress] NVARCHAR(500) NOT NULL,
[subject] NVARCHAR(1000) NOT NULL,
[htmlBody] NVARCHAR(MAX) NULL,
[textBody] NVARCHAR(MAX) NOT NULL,
[attachments] NVARCHAR(MAX) NOT NULL,
[culture] NVARCHAR(32) NOT NULL,
[recipientKey] NVARCHAR(320) NULL,
[state] NVARCHAR(16) NOT NULL,
[attempts] INTEGER NOT NULL,
[nextAttemptUtc] DATETIME NULL,
[lockedUntilUtc] DATETIME NULL,
[lockedBy] NVARCHAR(255) NULL,
[lastErrorCode] NVARCHAR(64) NULL,
[lastErrorMessage] NVARCHAR(1000) NULL,
[createdUtc] DATETIME NOT NULL,
[sentUtc] DATETIME NULL,
[purgeAfterUtc] DATETIME NULL);

ALTER TABLE [jfbEmail] ADD CONSTRAINT [PK_jfbEmail] PRIMARY KEY CLUSTERED ([id]);

CREATE NONCLUSTERED INDEX [IX_jfbEmail_entryId] ON [jfbEmail] ([entryId]);

CREATE NONCLUSTERED INDEX [IX_jfbEmail_recipientKey_createdUtc] ON [jfbEmail] ([recipientKey],[createdUtc]);

CREATE NONCLUSTERED INDEX [IX_jfbEmail_state_nextAttemptUtc] ON [jfbEmail] ([state],[nextAttemptUtc]);

CREATE NONCLUSTERED INDEX [IX_jfbEmail_purgeAfterUtc] ON [jfbEmail] ([purgeAfterUtc]);

ALTER TABLE [jfbEmail] WITH CHECK ADD CONSTRAINT [FK_jfbEmail_jfbEntry] FOREIGN KEY ([entryId]) REFERENCES [jfbEntry] ([id]) ON DELETE SET NULL;

CREATE TABLE [jfbOptionCapacity] ([formId] INTEGER NOT NULL,
[fieldKey] UniqueIdentifier NOT NULL,
[valueHash] NVARCHAR(64) NOT NULL,
[optionValue] NVARCHAR(200) NOT NULL,
[taken] INTEGER NOT NULL,
[updatedUtc] DATETIME NOT NULL);

ALTER TABLE [jfbOptionCapacity] ADD CONSTRAINT [PK_jfbOptionCapacity] PRIMARY KEY CLUSTERED ([formId], [fieldKey], [valueHash]);

ALTER TABLE [jfbOptionCapacity] WITH CHECK ADD CONSTRAINT [FK_jfbOptionCapacity_jfbForm] FOREIGN KEY ([formId]) REFERENCES [jfbForm] ([id]) ON DELETE CASCADE;

CREATE TABLE [jfbOptionList] ([id] INTEGER NOT NULL IDENTITY(1,1),
[key] UniqueIdentifier NOT NULL,
[alias] NVARCHAR(64) NOT NULL,
[name] NVARCHAR(255) NOT NULL,
[kind] NVARCHAR(16) NOT NULL,
[config] NVARCHAR(MAX) NOT NULL,
[items] NVARCHAR(MAX) NOT NULL,
[cacheSeconds] INTEGER NOT NULL,
[concurrencyVersion] INTEGER NOT NULL,
[createdUtc] DATETIME NOT NULL,
[updatedUtc] DATETIME NOT NULL);

ALTER TABLE [jfbOptionList] ADD CONSTRAINT [PK_jfbOptionList] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbOptionList_key] ON [jfbOptionList] ([key]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbOptionList_alias] ON [jfbOptionList] ([alias]);

CREATE TABLE [jfbTheme] ([id] INTEGER NOT NULL IDENTITY(1,1),
[key] UniqueIdentifier NOT NULL,
[alias] NVARCHAR(64) NOT NULL,
[name] NVARCHAR(255) NOT NULL,
[baseTheme] NVARCHAR(64) NOT NULL,
[tokens] NVARCHAR(MAX) NOT NULL,
[customCss] NVARCHAR(MAX) NULL,
[concurrencyVersion] INTEGER NOT NULL,
[createdUtc] DATETIME NOT NULL,
[updatedUtc] DATETIME NOT NULL);

ALTER TABLE [jfbTheme] ADD CONSTRAINT [PK_jfbTheme] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbTheme_key] ON [jfbTheme] ([key]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbTheme_alias] ON [jfbTheme] ([alias]);

CREATE TABLE [jfbEmailLayout] ([id] INTEGER NOT NULL IDENTITY(1,1),
[key] UniqueIdentifier NOT NULL,
[alias] NVARCHAR(64) NOT NULL,
[name] NVARCHAR(255) NOT NULL,
[layout] NVARCHAR(MAX) NOT NULL,
[concurrencyVersion] INTEGER NOT NULL,
[createdUtc] DATETIME NOT NULL,
[updatedUtc] DATETIME NOT NULL);

ALTER TABLE [jfbEmailLayout] ADD CONSTRAINT [PK_jfbEmailLayout] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbEmailLayout_key] ON [jfbEmailLayout] ([key]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbEmailLayout_alias] ON [jfbEmailLayout] ([alias]);

CREATE TABLE [jfbFormTemplate] ([id] INTEGER NOT NULL IDENTITY(1,1),
[key] UniqueIdentifier NOT NULL,
[kind] NVARCHAR(16) NOT NULL,
[name] NVARCHAR(255) NOT NULL,
[description] NVARCHAR(2000) NULL,
[category] NVARCHAR(64) NULL,
[definition] NVARCHAR(MAX) NOT NULL,
[createdUtc] DATETIME NOT NULL,
[updatedUtc] DATETIME NOT NULL,
[createdByUserKey] UniqueIdentifier NULL,
[updatedByUserKey] UniqueIdentifier NULL);

ALTER TABLE [jfbFormTemplate] ADD CONSTRAINT [PK_jfbFormTemplate] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbFormTemplate_key] ON [jfbFormTemplate] ([key]);

CREATE TABLE [jfbUserSetting] ([userKey] UniqueIdentifier NOT NULL,
[name] NVARCHAR(64) NOT NULL,
[json] NVARCHAR(MAX) NOT NULL,
[updatedUtc] DATETIME NOT NULL);

ALTER TABLE [jfbUserSetting] ADD CONSTRAINT [PK_jfbUserSetting] PRIMARY KEY CLUSTERED ([userKey], [name]);

CREATE TABLE [jfbPermission] ([id] INTEGER NOT NULL IDENTITY(1,1),
[userGroupKey] UniqueIdentifier NOT NULL,
[targetType] NVARCHAR(16) NOT NULL,
[targetKey] UniqueIdentifier NOT NULL,
[permissions] INTEGER NOT NULL);

ALTER TABLE [jfbPermission] ADD CONSTRAINT [PK_jfbPermission] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbPermission_userGroupKey_targetType_targetKey] ON [jfbPermission] ([userGroupKey],[targetType],[targetKey]);

CREATE TABLE [jfbSetting] ([id] INTEGER NOT NULL IDENTITY(1,1),
[scope] NVARCHAR(64) NOT NULL,
[section] NVARCHAR(64) NOT NULL,
[schemaVersion] INTEGER NOT NULL,
[version] INTEGER NOT NULL,
[json] NVARCHAR(MAX) NOT NULL,
[updatedUtc] DATETIME NOT NULL,
[updatedByUserKey] UniqueIdentifier NULL);

ALTER TABLE [jfbSetting] ADD CONSTRAINT [PK_jfbSetting] PRIMARY KEY CLUSTERED ([id]);

CREATE UNIQUE NONCLUSTERED INDEX [IX_jfbSetting_scope_section] ON [jfbSetting] ([scope],[section]);

CREATE TABLE [jfbReference] ([formId] INTEGER NOT NULL,
[contentKey] UniqueIdentifier NOT NULL,
[propertyAlias] NVARCHAR(255) NOT NULL,
[culture] NVARCHAR(32) NOT NULL,
[updatedUtc] DATETIME NOT NULL);

ALTER TABLE [jfbReference] ADD CONSTRAINT [PK_jfbReference] PRIMARY KEY CLUSTERED ([formId], [contentKey], [propertyAlias], [culture]);

CREATE NONCLUSTERED INDEX [IX_jfbReference_contentKey] ON [jfbReference] ([contentKey]);

CREATE TABLE [jfbAuditEntry] ([id] BIGINT NOT NULL IDENTITY(1,1),
[occurredUtc] DATETIME NOT NULL,
[userKey] UniqueIdentifier NULL,
[action] NVARCHAR(64) NOT NULL,
[entityType] NVARCHAR(64) NOT NULL,
[entityKey] UniqueIdentifier NULL,
[details] NVARCHAR(MAX) NOT NULL);

ALTER TABLE [jfbAuditEntry] ADD CONSTRAINT [PK_jfbAuditEntry] PRIMARY KEY CLUSTERED ([id]);

CREATE NONCLUSTERED INDEX [IX_jfbAuditEntry_occurredUtc] ON [jfbAuditEntry] ([occurredUtc]);

CREATE NONCLUSTERED INDEX [IX_jfbAuditEntry_entityKey] ON [jfbAuditEntry] ([entityKey]);

CREATE TABLE [jfbDailyStat] ([formId] INTEGER NOT NULL,
[day] DATETIME NOT NULL,
[metric] NVARCHAR(64) NOT NULL,
[dimension] NVARCHAR(128) NOT NULL,
[value] BIGINT NOT NULL);

ALTER TABLE [jfbDailyStat] ADD CONSTRAINT [PK_jfbDailyStat] PRIMARY KEY CLUSTERED ([formId], [day], [metric], [dimension]);

CREATE NONCLUSTERED INDEX [IX_jfbDailyStat_day_formId] ON [jfbDailyStat] ([day],[formId]);

ALTER TABLE [jfbForm] WITH CHECK ADD CONSTRAINT [FK_jfbForm_jfbFormVersion_draftVersionId] FOREIGN KEY ([draftVersionId]) REFERENCES [jfbFormVersion] ([id]);

ALTER TABLE [jfbForm] WITH CHECK ADD CONSTRAINT [FK_jfbForm_jfbFormVersion_publishedVersionId] FOREIGN KEY ([publishedVersionId]) REFERENCES [jfbFormVersion] ([id]);

CREATE UNIQUE INDEX [IX_jfbEntry_formId_challengeId] ON [jfbEntry] ([formId], [challengeId]) WHERE [challengeId] IS NOT NULL;

CREATE UNIQUE INDEX [IX_jfbEntry_resumeTokenHash] ON [jfbEntry] ([resumeTokenHash]) WHERE [resumeTokenHash] IS NOT NULL;

CREATE INDEX [IX_jfbEntryValue_fileId] ON [jfbEntryValue] ([fileId]) WHERE [fileId] IS NOT NULL;
