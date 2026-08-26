# Your Pals Persistence Contract

Status: **Current document version 1.** This is the contract implemented by the
save-scoped Your Pals document, source snapshot, resolver, and recovery writer.

This document is normative for new Your Pals persistence code. It describes
what values identify data, how references are resolved, which malformed values
remain visible, and the limits of explicit repair.

## Scope and storage

Your Pals owns one document per save. The document is stored at:

```text
<Storage.DataPath>/<SaveIdentity.StorageKey>/your-pals.json
```

The atomic writer keeps the previous primary document at
`your-pals.json.bak` where the storage platform supports it. The document
envelope contains:

- `documentType`: exactly `your-pals`;
- `documentVersion`: exactly `1` for the current writer;
- `ownerSaveIdentity`: the owning `userId` and `gameId`;
- `groups`: persisted group and member data;
- `manualDefinitions`: persisted raw manual Pal data.

Unknown JSON fields are retained through the DTO boundary. Unknown member
`kind` values are retained as strings and remain unresolved; they are not
converted to a known kind.

Your Pals does not import or synchronize Inspect custom-container data, and it
does not use that data as a persistence fallback.

## Save identity

`SaveIdentity` is the ownership key for a document, session, cache path, and
available-save set.

### Construction

`SaveIdentity.Create(userId, gameId)` applies these rules:

1. `userId` and `gameId` must each be non-null, non-empty, and not whitespace.
2. The original strings are preserved exactly. There is no trimming,
   case-folding, normalization, display-name substitution, or path separator
   conversion.
3. Equality uses the record's ordinal string equality. A case-only change is a
   different identity.
4. `SaveIdentity.From(save)` applies the same rules to `save.UserId` and
   `save.GameId`; a null save is invalid.

### Canonical and storage keys

The canonical identity key is the injective, length-prefixed string:

```text
<userId.Length>:<userId><gameId.Length>:<gameId>
```

`Length` is the .NET string length used by the implementation. For example,
`user` and `game` produce `4:user4:game`.

`StorageKey` is the uppercase hexadecimal UTF-8 encoding of `CanonicalKey`.
It is a filename-safe representation and must not be replaced with a display
label, a hash, or a concatenation without lengths.

The serialized owner identity contains the original `userId` and `gameId`.
Loading a document whose owner is not exactly equal to the expected
`SaveIdentity` returns `OwnerMismatchReadOnly`; it is not rebound implicitly.

## Source identity and imported references

An imported reference identifies a source Pal with both a source scope and a
non-empty source instance ID. Names are diagnostic metadata, not identity.

### Source identity forms

`SourceIdentity` is a `(Kind, Scope)` pair. Its stable key is
`<Kind>:<Scope>`.

- Save-owned data uses `Kind = Save` and `Scope = SaveIdentity.CanonicalKey`.
- Global Pal Storage uses `Kind = GlobalPalStorage` and a normalized parent
  location path as `Scope`:
  - resolve with `Path.GetFullPath`;
  - preserve the filesystem root without stripping it;
  - otherwise remove trailing directory separators;
  - replace the platform's primary directory separator with `/`.

Global Pal Storage is therefore scoped by its parent location, not by a save
display name or a coincidentally similar save ID.

### Reference lookup

An imported member is structurally valid only when it has:

- a non-whitespace `PalEntryKey`;
- a source identity with a non-whitespace scope;
- a non-whitespace `InstanceId`.

Resolution first matches `(SourceIdentity, InstanceId)` using ordinal instance
ID comparison. `SourceKey` is only a disambiguator when that match returns
more than one record; it is compared ordinally and is not a replacement for
`InstanceId`.

The resolver reports these outcomes without throwing or deleting the member:

- no matching source record: `Stale`;
- multiple matching records with different content: `Conflict`;
- matching record whose Pal is absent from the current catalog: `Unresolved`;
- matching record with no stable ID, unknown Pal, or non-male/female gender:
  `Invalid` (with the specific eligibility reason);
- one usable matching record: `Resolved`.

Missing or stale source data never removes the persisted member. It is removed
only by an explicit user command or an explicit recovery operation described
below.

### Source snapshot normalization

The active source snapshot excludes Inspect custom locations and excludes
records without a non-empty `InstanceId`. Remaining records are grouped by
`(SourceIdentity, InstanceId)`.

Within each group, records are ordered by:

1. `SourceKey`, ordinal;
2. content fingerprint, ordinal.

If every record after the first is equivalent to the first under the existing
model comparison (`AreEquivalentOwnedRecords`), the first record is retained
and the rest are deduplicated with an informational diagnostic. If any record
differs, all records remain visible as a conflict and none is solver-eligible.

This ordering makes equivalent duplicate selection deterministic while
preserving conflicting data for review.

## Persisted duplicate resolution

Persistence has separate keys for groups, member occurrences, and manual
definitions:

- `GroupId` identifies a group; the editable group name is not a key.
- `PalEntryKey` identifies a member occurrence and is unique across the
  document after repair.
- `ManualDefinitionId` identifies a manual definition.

During explicit recovered-document repair:

1. Non-null groups are ordered by `Order`, then by their original array index.
2. Missing or duplicate group IDs receive deterministic IDs derived from
   `SHA-256("your-pals-repair\\0group\\0<original-or-(missing)>\\0<index>")`,
   using the first 16 lowercase hexadecimal characters and a numeric suffix
   only if a collision still occurs.
3. Missing or duplicate member keys receive the same deterministic scheme with
   the `entry` prefix and the group ID/member key/index payload. Member keys
   are tracked across the whole document.
4. Within each group, imported members duplicate on
   `(SourceIdentity.StableKey, InstanceId)`. Manual members duplicate on
   `ManualDefinitionId`. The first member in group order is retained; later
   duplicates are removed.
5. Duplicate manual definitions retain the first definition in array order;
   later definitions with the same ID are removed. Missing manual IDs receive
   deterministic `manual` repair IDs.
6. Missing group names become `Recovered group <n>`, and group order is
   rewritten to the repaired sequence.

The explicit `RemoveDuplicateMembers` command uses the same first-wins rule
within each group. It also treats a repeated non-empty `PalEntryKey` as a
duplicate.

## Manual-field validation

Manual definitions persist `RawInternalName` and the raw `JToken` values in
`RawValues`. Validation happens when resolving a definition, not by replacing
the raw document with a partially normalized object.

### Required and catalog-backed values

- `ManualDefinitionId` must be non-whitespace for resolution.
- `RawInternalName` must be non-whitespace and must match a current catalog Pal
  name case-insensitively. An unknown name remains unresolved.
- `gender` accepts a string enum value case-insensitively. Missing or null
  defaults to `MALE`; only `MALE` and `FEMALE` are usable. Numeric enum tokens,
  unknown values, and other token types are invalid.
- Passive, active, and equipped-active names are resolved against the current
  catalog. Unknown skills leave the manual definition unresolved.

### Integer values

Missing or null integer fields use these defaults:

| Field | Accepted aliases | Default | Runtime normalization |
| --- | --- | ---: | --- |
| `level` | `level` | 1 | minimum 1 |
| `rank` | `rank` | 1 | clamp to 1–5 |
| `ivHp` | `ivHp`, `IV_HP` | 0 | clamp to 0–100 |
| `ivAttack` | `ivAttack`, `IV_Shot`, `ivShot` | 0 | clamp to 0–100 |
| `ivDefense` | `ivDefense`, `IV_Defense` | 0 | clamp to 0–100 |
| `ivMelee` | `ivMelee`, `IV_Melee` | 0 | clamp to 0–100 |

An integer may be a JSON integer or a string parsed with invariant-culture
integer rules. Invalid, overflowing, or otherwise unparseable integers do not
throw and do not fall back to the default; the definition remains unresolved
with a field-specific diagnostic.

### Other fields

- `ownerPlayerId` accepts a JSON string, null, or omission. Other token types
  make the definition unresolved.
- `isOnExpedition` accepts a JSON boolean or a parseable boolean string. Null
  or omission defaults to `false`.
- `passiveSkills`, `passives`, and `traits` are aliases. `activeSkills` and
  `active` are aliases. `equippedActiveSkills` and `equippedActives` are
  aliases. Each accepts one string or an array containing only strings.
  Invalid list shapes make the definition unresolved.
- `nickname` is read when it is a string; a missing, null, or wrong-type value
  contributes no runtime nickname. Its raw token remains in `RawValues`.

A resolved manual definition receives the ephemeral solver identity
`manual:<ManualDefinitionId>` and a custom location. That identity is never
persisted as a game `InstanceId` and must not overwrite a real source Pal.

## Recovery and repair boundary

### What the reader preserves

Reads are non-destructive. The recovery reader can retain valid groups and
members when neighboring records are malformed. Invalid scalar fields are
kept in recovery extension data where possible; unknown document/member fields
are retained. A missing source Pal or an invalid manual field is represented
as a status, not as a deletion.

### What explicit repair can do

`Repair recovered` is allowed only for a
`PartiallyRecoveredReadOnly` document with no whole-record loss marker. It can:

- normalize group order and fill missing group names;
- assign deterministic IDs for missing or duplicate group/member/manual IDs;
- remove null members and null manual definitions;
- remove later duplicate members or manual definitions under the rules above;
- initialize missing collections on recovered records;
- atomically write the repaired projection to the primary document.

When the recovered content came from `.bak`, repair restores the primary while
preserving the existing backup. The owner identity, document path, content
fingerprint, and external-change checks are still enforced.

Repair does not make an unresolved member resolved. Raw invalid manual fields,
unknown catalog names, unknown skills, stale source references, and source
conflicts remain visible after repair until the user edits or rebinds them.

### What repair cannot do

Repair is disabled when the recovery reader dropped a whole group, dropped a
whole manual definition, or could not read a required collection safely. The
original content cannot be reconstructed from the remaining projection, so it
must not be silently written back.

Repair also cannot recover:

- malformed document envelopes or invalid owner identities;
- owner-mismatched documents;
- unsupported future document versions;
- documents in migration-pending, corrupt, or otherwise non-partial states;
- deleted source Pals or missing source instance IDs;
- unknown catalog records, passives, or active skills;
- data already lost from both primary and backup files.

Those cases remain read-only and require the original file, a usable backup,
manual correction, or an explicitly designed future migration/rebind flow.

## Versioning and deferred decisions

The current writer emits document version `1`. The current implementation has
no decided migration route for older Your Pals versions and no legacy fallback
to Inspect or other customizations stores:

- a document version greater than `1` is `UnsupportedVersionReadOnly`;
- a document version less than `1` is `MigrationPending` and cannot be written;
- the application does not reinterpret another store as a Your Pals document;
- no implicit cross-save rebind is performed.

These are deliberate boundaries, not compatibility behavior to infer. A future
migration or import feature must be explicit, versioned, separately reviewed,
and must preserve the ownership rules in this contract.
