---
Status: Implemented
Last updated: 2026-06-28
---

# Generic file links

## Problem

Files module already owns upload, list, download, rename and delete of user files.
Product modules also need a repeatable way to say: this uploaded file belongs to this domain object.

Examples:

- a contract file belongs to a CRM deal;
- an invoice file belongs to a billing import;
- a source spreadsheet belongs to a marketing pull;
- a screenshot belongs to an operation report.

The product module must not copy blob storage, expose storage keys, or join directly to `file_objects`.

## Decision

Files module provides a generic user-owned link table:

`file_links(UserId, OwnerType, OwnerId, FileObjectId, CreatedAt)`

`OwnerType` is a small stable string owned by the product module, for example:

- `crm.deal`
- `marketing.pull`
- `operations.report`

`OwnerId` is the product module entity id.

The Files module verifies that the linked file belongs to the current user. It does not verify that the owner entity
exists, because that would require a cross-module dependency. A product module endpoint that exposes this action to
users must first verify access to the owner entity and then call the Files link endpoint or command.

## API contract

### Link file

`POST /v1/files/{fileId}/links`

Request:

```json
{
  "ownerType": "crm.deal",
  "ownerId": "018f4b35-..."
}
```

Response: `201 Created`

```json
{
  "id": "018f4b36-...",
  "fileObjectId": "018f4b35-...",
  "ownerType": "crm.deal",
  "ownerId": "018f4b35-...",
  "fileName": "contract.pdf",
  "contentType": "application/pdf",
  "size": 12345,
  "createdAt": "2026-06-28T10:00:00Z"
}
```

Duplicate link is idempotent: the existing link is returned.

### List links for owner

`GET /v1/files/links?ownerType=crm.deal&ownerId={id}`

Returns `PagedResponse<FileLinkItem>`.

### Unlink file

`DELETE /v1/files/links/{linkId}`

Deletes only the link, not the file object and not the blob.

## Validation

- `ownerType` is required.
- `ownerType` max length is 128.
- `ownerType` accepts lowercase letters, numbers, dot, hyphen and underscore.
- `ownerId` must not be empty.
- `fileId` must belong to the current user.

## Edge cases

- Foreign file id returns `file.not_found`.
- Duplicate link returns the existing link and does not create a duplicate row.
- Foreign link id returns `file.link_not_found`.
- Unlink removes only the relation. The file remains downloadable/listed under Files.
- GDPR erasure deletes both file metadata/blobs and the user's link rows.
- Product module must still verify owner access before linking. Files cannot know if `ownerId` is a real CRM deal.

## Acceptance criteria

- A user can link their own uploaded file to an owner type/id.
- A user can list links for an owner type/id.
- A user can unlink a link without deleting the file.
- A different user cannot link or list another user's file.
- Duplicate link is idempotent.
- GDPR export includes link rows.
- GDPR erasure removes link rows.

## Verification

- `FilesUploadTests.File_can_be_linked_listed_idempotently_and_unlinked_without_deleting_the_file`
- `FilesUploadTests.File_links_are_owner_scoped_and_validate_owner_type`
- `FilesUploadTests.Gdpr_erasure_deletes_the_users_files_and_metadata`
