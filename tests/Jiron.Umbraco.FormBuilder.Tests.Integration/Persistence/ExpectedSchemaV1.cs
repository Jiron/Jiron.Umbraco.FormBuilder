namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;

/// <summary>
/// Schema v1 as section 4.5, ADR-0003 and ADR-0033 describe it, written out by hand so the tests compare the database (and
/// docs/database.md) with it rather than with the DTOs that create it.
/// </summary>
internal static class ExpectedSchemaV1
{
    /// <summary>Every table with its columns in order; <c>?</c> marks a nullable column. In creation order.</summary>
    public static readonly IReadOnlyDictionary<string, string> Columns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["jfbKeyValue"] = "key, value?, updatedUtc",
        ["jfbFolder"] = "id, key, parentId?, name, sortOrder, isRestricted, trashedUtc?, createdUtc, updatedUtc, createdByUserKey?, updatedByUserKey?",
        ["jfbForm"] = "id, key, folderId?, alias, name, draftVersionId?, publishedVersionId?, publicRevision, concurrencyVersion, lastEntryNumber, themeAlias?, isRestricted, trashedUtc?, createdUtc, updatedUtc, createdByUserKey?, updatedByUserKey?",
        ["jfbFormVersion"] = "id, formId, number, kind, schemaVersion, definition, definitionHash, publicHash, publicRevision?, comment?, isPinned, createdUtc, createdByUserKey?, publishedUtc?, publishedByUserKey?",
        ["jfbFormField"] = "id, formId, fieldKey, alias, label, type, storageKind, isSensitive, sortOrder, firstVersionNumber?, lastVersionNumber?, parentFieldKey?",
        ["jfbEntry"] = "id, key, formId, formVersionId, number, state, isTest, submissionId, digest, culture, contentKey?, pageUrl?, memberKey?, ipAddress?, spamScore, spamReasons, searchText, context, durationSeconds?, submittedUtc, updatedUtc, stateChangedUtc, stateChangedByUserKey?, firstSeenUtc?, verifiedUtc?, resumeTokenHash?, resumeExpiresUtc?, currentPageKey?, challengeId?, holdUntilUtc?, holdReason?, purgeAfterUtc?",
        ["jfbEntryFile"] = "id, key, entryId?, formId, fieldKey, originalName, storedName, contentType, sizeBytes, sha256, scanState, createdUtc",
        ["jfbEntryValue"] = "id, entryId, fieldId, rowIndex, valueIndex, text?, textKey?, number?, date?, bool?, fileId?",
        ["jfbConsentText"] = "hash, culture, text, firstSeenUtc",
        ["jfbEntryEvent"] = "id, entryId, occurredUtc, kind, userKey?, changeKey?, details",
        ["jfbEntryNote"] = "id, entryId, userKey, body, createdUtc, updatedUtc",
        ["jfbActionRun"] = "id, entryId, formVersionId, actionKey, actionType, triggerKind, seq, stopOnFailure, state, attempts, nextAttemptUtc?, changeKey?, lockedUntilUtc?, lockedBy?, lastErrorCode?, lastErrorMessage?, createdUtc, startedUtc?, finishedUtc?",
        ["jfbEmail"] = "id, kind, entryId?, actionRunId?, toAddresses, ccAddresses?, bccAddresses?, replyTo?, fromAddress, subject, htmlBody?, textBody, attachments, culture, recipientKey?, state, attempts, nextAttemptUtc?, lockedUntilUtc?, lockedBy?, lastError?, createdUtc, sentUtc?, purgeAfterUtc?",
        ["jfbOptionCapacity"] = "formId, fieldKey, valueHash, optionValue, taken, updatedUtc",
        ["jfbOptionList"] = "id, key, alias, name, kind, config, items, cacheSeconds, concurrencyVersion, createdUtc, updatedUtc",
        ["jfbTheme"] = "id, key, alias, name, baseTheme, tokens, customCss?, concurrencyVersion, createdUtc, updatedUtc",
        ["jfbEmailLayout"] = "id, key, alias, name, layout, concurrencyVersion, createdUtc, updatedUtc",
        ["jfbFormTemplate"] = "id, key, kind, name, description?, category?, definition, createdUtc, createdByUserKey?",
        ["jfbUserSetting"] = "userKey, name, json, updatedUtc",
        ["jfbPermission"] = "id, userGroupKey, targetType, targetKey, permissions",
        ["jfbSetting"] = "id, scope, section, schemaVersion, version, json, updatedUtc, updatedByUserKey?",
        ["jfbReference"] = "formId, contentKey, propertyAlias, culture, updatedUtc",
        ["jfbAuditEntry"] = "id, occurredUtc, userKey?, action, entityType, entityKey?, details",
        ["jfbDailyStat"] = "day, formId, metric, dimension, value",
    };

    /// <summary>Every table's primary key columns in key order: <c>id</c> unless named here.</summary>
    public static string PrimaryKey(string table) => table switch
    {
        "jfbKeyValue" => "key",
        "jfbConsentText" => "hash",
        "jfbOptionCapacity" => "formId,fieldKey,valueHash",
        "jfbUserSetting" => "userKey,name",
        "jfbReference" => "formId,contentKey,propertyAlias,culture",
        "jfbDailyStat" => "day,formId,metric,dimension",
        _ => "id",
    };

    /// <summary>Every index but the primary keys, as <see cref="SchemaInspector"/> reports it, in ordinal order.</summary>
    public static readonly IReadOnlyList<string> Indexes = new[]
    {
        "jfbActionRun.IX_jfbActionRun_changeKey(changeKey)",
        "jfbActionRun.IX_jfbActionRun_entryId_seq(entryId,seq)",
        "jfbActionRun.IX_jfbActionRun_state_nextAttemptUtc(state,nextAttemptUtc)",
        "jfbAuditEntry.IX_jfbAuditEntry_entityKey(entityKey)",
        "jfbAuditEntry.IX_jfbAuditEntry_occurredUtc(occurredUtc)",
        "jfbEmail.IX_jfbEmail_entryId(entryId)",
        "jfbEmail.IX_jfbEmail_purgeAfterUtc(purgeAfterUtc)",
        "jfbEmail.IX_jfbEmail_recipientKey_createdUtc(recipientKey,createdUtc)",
        "jfbEmail.IX_jfbEmail_state_nextAttemptUtc(state,nextAttemptUtc)",
        "jfbEmailLayout.IX_jfbEmailLayout_alias(alias) unique",
        "jfbEmailLayout.IX_jfbEmailLayout_key(key) unique",
        "jfbEntry.IX_jfbEntry_formId_challengeId(formId,challengeId) unique where challengeId is not null",
        "jfbEntry.IX_jfbEntry_formId_number(formId,number) unique",
        "jfbEntry.IX_jfbEntry_formId_state_submittedUtc(formId,state,submittedUtc)",
        "jfbEntry.IX_jfbEntry_formId_submissionId(formId,submissionId) unique",
        "jfbEntry.IX_jfbEntry_formId_submittedUtc(formId,submittedUtc)",
        "jfbEntry.IX_jfbEntry_formVersionId(formVersionId)",
        "jfbEntry.IX_jfbEntry_key(key) unique",
        "jfbEntry.IX_jfbEntry_memberKey(memberKey)",
        "jfbEntry.IX_jfbEntry_purgeAfterUtc(purgeAfterUtc)",
        "jfbEntry.IX_jfbEntry_resumeTokenHash(resumeTokenHash) unique where resumeTokenHash is not null",
        "jfbEntryEvent.IX_jfbEntryEvent_changeKey(changeKey)",
        "jfbEntryEvent.IX_jfbEntryEvent_entryId_occurredUtc(entryId,occurredUtc)",
        "jfbEntryFile.IX_jfbEntryFile_createdUtc(createdUtc)",
        "jfbEntryFile.IX_jfbEntryFile_entryId(entryId)",
        "jfbEntryFile.IX_jfbEntryFile_key(key) unique",
        "jfbEntryNote.IX_jfbEntryNote_entryId(entryId)",
        "jfbEntryValue.IX_jfbEntryValue_entryId_fieldId(entryId,fieldId)",
        "jfbEntryValue.IX_jfbEntryValue_fieldId_date_entryId(fieldId,date,entryId)",
        "jfbEntryValue.IX_jfbEntryValue_fieldId_number_entryId(fieldId,number,entryId)",
        "jfbEntryValue.IX_jfbEntryValue_fieldId_textKey_entryId(fieldId,textKey,entryId)",
        "jfbEntryValue.IX_jfbEntryValue_fileId(fileId) where fileId is not null",
        "jfbFolder.IX_jfbFolder_key(key) unique",
        "jfbFolder.IX_jfbFolder_parentId(parentId)",
        "jfbForm.IX_jfbForm_alias(alias) unique",
        "jfbForm.IX_jfbForm_folderId(folderId)",
        "jfbForm.IX_jfbForm_key(key) unique",
        "jfbForm.IX_jfbForm_trashedUtc(trashedUtc)",
        "jfbFormField.IX_jfbFormField_formId_fieldKey(formId,fieldKey) unique",
        "jfbFormTemplate.IX_jfbFormTemplate_key(key) unique",
        "jfbFormVersion.IX_jfbFormVersion_formId_number(formId,number) unique",
        "jfbOptionList.IX_jfbOptionList_alias(alias) unique",
        "jfbOptionList.IX_jfbOptionList_key(key) unique",
        "jfbPermission.IX_jfbPermission_userGroupKey_targetType_targetKey(userGroupKey,targetType,targetKey) unique",
        "jfbSetting.IX_jfbSetting_scope_section(scope,section) unique",
        "jfbTheme.IX_jfbTheme_alias(alias) unique",
        "jfbTheme.IX_jfbTheme_key(key) unique",
    }.Order(StringComparer.Ordinal).ToList();

    /// <summary>Every foreign key with its delete action, in ordinal order.</summary>
    public static readonly IReadOnlyList<string> ForeignKeys = new[]
    {
        "jfbActionRun.entryId -> jfbEntry on delete cascade",
        "jfbEmail.entryId -> jfbEntry on delete set null",
        "jfbEntry.formId -> jfbForm on delete no action",
        "jfbEntry.formVersionId -> jfbFormVersion on delete no action",
        "jfbEntryEvent.entryId -> jfbEntry on delete cascade",
        "jfbEntryFile.entryId -> jfbEntry on delete cascade",
        "jfbEntryNote.entryId -> jfbEntry on delete cascade",
        "jfbEntryValue.entryId -> jfbEntry on delete cascade",
        "jfbEntryValue.fieldId -> jfbFormField on delete no action",
        "jfbEntryValue.fileId -> jfbEntryFile on delete no action",
        "jfbFolder.parentId -> jfbFolder on delete no action",
        "jfbForm.draftVersionId -> jfbFormVersion on delete no action",
        "jfbForm.folderId -> jfbFolder on delete no action",
        "jfbForm.publishedVersionId -> jfbFormVersion on delete no action",
        "jfbFormField.formId -> jfbForm on delete cascade",
        "jfbFormVersion.formId -> jfbForm on delete cascade",
        "jfbOptionCapacity.formId -> jfbForm on delete cascade",
    }.Order(StringComparer.Ordinal).ToList();

    /// <summary>The indexes of the named tables, for example those of section 4.5 and ADR-0003 on entries and values.</summary>
    public static IReadOnlyList<string> IndexesOf(params string[] tables) =>
        Indexes.Where(index => tables.Contains(index[..index.IndexOf('.', StringComparison.Ordinal)], StringComparer.Ordinal)).ToList();
}
