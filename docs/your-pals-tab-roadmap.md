# Your Pals Tab: Architecture and Roadmap

Status: **Phases 0–6 implemented; canonical save-scoped persistence, recovery, query state, save-scoped editing, solver-source projection, and Inspect isolation are active.**

This document defines the architecture for managing Pal collections and related user data for a selected save file. It deliberately puts persistence, identity, reload behavior, and recovery ahead of the tab UI so that a new load, missing Pal, stale reference, or partially corrupted document cannot silently destroy user data or break existing features.

The implemented persistence rules are specified in the [Your Pals Persistence Contract](your-pals-persistence-contract.md). That contract is normative where this roadmap is descriptive.

## Executive decisions

### 1. Your Pals is save-scoped

The canonical Your Pals workspace belongs to one selected save. Its persisted data must be keyed by a structured save identity, not by the current page instance or a convenient display name.

The first version should make the ownership boundary explicit:

- With a save selected, load and edit that save's Your Pals document.
- With no save selected, show a clear empty/no-save state. A temporary in-memory draft may be supported, but it must not be silently persisted as data for a future save.
- Persistent cross-save groups, templates, and presets are separate product features. They should not be introduced by weakening save ownership.

This avoids the most dangerous ambiguity: a user creates a group before selecting a save, selects a different save later, and unintentionally sees or overwrites data that belongs somewhere else.

### 2. Reuse the merged persistence foundation

The existing storage-format v3 manifest, DTO boundary, migration runner, migration route validation, backups, and atomic migration writes are the foundation for Your Pals. Your Pals should not introduce a second ad-hoc persistence framework.

The preferred first persisted artifact is a new save-scoped `your-pals.json` document under the existing save data directory. It has its own document version and DTO schema while remaining governed by the application storage format.

Runtime Your Pals writes must use the same safety properties already established for migrations:

- write a temporary file in the same directory;
- flush the file before replacement where practical;
- retain a recoverable backup of the last known-good document;
- replace atomically;
- keep the in-memory document dirty when a write fails;
- never replace a valid document with an empty fallback merely because a load or save operation failed.

### 3. Missing data is a visible state, not a deletion instruction

An imported Pal reference may become unavailable after a save reload, a database update, a deleted Pal, a changed instance identity, or a damaged source file. The persisted reference must remain in the group and be shown as unresolved, stale, conflicting, or invalid.

The resolver may exclude unusable entries from breeding calculations, but it must never silently remove them from the user's saved collection. Recovery and deletion are separate user actions.

### 4. One canonical writer per save

Your Pals is the sole writer for its save-scoped document. Inspect custom containers remain a separate existing feature; the two stores are not synchronized or used as fallback sources for each other.

## Product direction and vocabulary

The product has three related but different concepts:

### Breeding

The existing solver experience: choose compatible inputs, calculate breeding outcomes, inspect results, and manage targets.

### Your Pals

A save-owned workspace for organizing owned/imported Pal references and user-authored manual Pal definitions. It answers “what do I want to manage for this save?”

### Pal Catalog

The database-backed universe of known Pal species, passive skills, active skills, and other reference data. It answers “what can the application resolve or calculate?”

Your Pals should depend on catalog data for resolution, but it must not serialize live catalog objects or treat catalog availability as proof that a user-owned reference is still valid.

## Current architecture after the persistence merge

### Save and page lifecycle

The main window selects a save and creates the solver page view model after that selection. Save-scoped customizations are cached by `SaveIdentity`. This is useful for a save-owned session, but it means a persistent Your Pals store must not be owned only by a page view model that may not exist when no save is selected.

The session boundary should therefore be the selected save, with the page consuming the session rather than owning its persistence.

### Current sources of Pal data

At least three source kinds need distinct identity rules:

- The selected save's owned Pal list.
- Global Pal Storage, which is loaded through the parent `ISavesLocation` and is not intrinsically part of one save file.
- User-authored/custom entries, which may be full Pal snapshots and may not have a reliable game `InstanceId`.

Global Pal Storage must not be represented as if it were simply another save with a coincidentally similar ID. The source identity needs to include the storage scope/parent location, and it must remain stable across reloads.

### Existing model behavior to preserve

`PalInstance` equality is based on `InstanceId`, and empty IDs are not safe identity values. The model calculator already has useful behavior for filtering empty IDs, deduplicating equivalent non-empty IDs, and detecting conflicting records. Your Pals should reuse or centralize that policy instead of implementing a subtly different duplicate rule in the UI.

Existing solver, catalog, target, and Inspect behavior is outside the first Your Pals document's scope. New code should adapt to those paths at clear boundaries rather than changing their assumptions globally.

### Persistence baseline and current gaps

The merged persistence work provides:

- a storage-format manifest and current version;
- ordered migrations with route validation;
- DTO and serializer boundaries;
- migration backups and atomic migration writes;
- per-save data directories;
- target persistence that can preserve valid targets when individual targets are bad.

The following gaps must be closed or explicitly handled before Your Pals editing is enabled:

- several existing runtime save paths still write directly rather than through one atomic writer;
- current customizations loading converts Pal names eagerly and can fail the whole document when one Pal name is unknown;
- a failed customization load currently falls back to a new empty `SaveCustomizations`, which is safe for rendering but unsafe if a later edit saves that empty fallback over the original file;
- reload replaces lists in `CachedSaveGame`, so sessions must rebuild source snapshots and must not retain references to old mutable lists;
- `SaveIdentity` must be used consistently for identity, cache paths, and document ownership.

These are architecture requirements, not reasons to rewrite unrelated persistence immediately. Your Pals can introduce the safer document/writer path first, then converge existing paths incrementally.

## Domain boundary

### Save-scoped document

The runtime should load one immutable or copy-on-write `YourPalsDocument` for the active save. It contains stable user data and references, not resolved database objects.

Conceptually:

```text
YourPalsDocument
├── DocumentType
├── DocumentVersion
├── OwnerSaveIdentity
├── Groups[]
│   ├── GroupId
│   ├── Name / presentation metadata
│   ├── Ordering metadata
│   └── Members[]
│       ├── PalEntryKey
│       └── ImportedPalReference | ManualDefinitionReference
└── ManualDefinitions[]
```

The document should be independent of the current database object graph and resilient to fields added in a future version.

### Stable identities

Use distinct identifiers for distinct purposes:

- `SaveIdentity`: canonical structured identity of the owning save, including the fields required to distinguish saves in the application.
- `GroupId`: stable ID for a user group. Names are editable and are not keys.
- `PalEntryKey`: stable ID for a member occurrence in a group. This permits selection, ordering, and diagnostics even when its Pal cannot resolve.
- `ManualDefinitionId`: stable ID for a manual definition.
- `SourceIdentity`: stable identity of the originating save, global storage scope, or another future source.
- `InstanceId`: source-provided identity for an imported game Pal, used only when non-empty and verified.

Do not use list indexes, localized labels, Pal display names, or a missing/empty `InstanceId` as durable identity.

### Imported references

An imported reference should contain enough information to diagnose and recover it without depending on the current object graph:

```text
ImportedPalReference
├── SourceIdentity
├── SourceKey / source snapshot key
├── InstanceId (when available)
├── LastKnownInternalName
├── LastKnownDisplayName (optional)
└── LastKnownSnapshot / diagnostic metadata (optional and versioned)
```

The source key is important for sources such as Global Pal Storage. `SourceIdentity + InstanceId` is the preferred lookup key, but a reference without a non-empty instance ID must not be promoted to an imported reference merely because it has a familiar name.

### Manual definitions

Manual entries are user-authored data, not game instances. Persist their raw values and internal names directly:

- internal Pal name or unresolved raw name;
- gender, level, IVs, passives, active skills, expedition, owner, nickname, and other supported fields;
- unknown properties needed for forward compatibility;
- a stable `ManualDefinitionId`.

If a Pal, passive, or active skill is not known by the current catalog, retain its raw persisted value and expose an unresolved diagnostic. Never replace an unknown value with the first catalog match or silently discard it.

### Resolved entries

Resolution is a runtime projection over persisted data and current source snapshots. It should return an immutable `ResolvedPalEntry` with an explicit status, for example:

- `Resolved`: valid source/reference or valid manual definition;
- `Unresolved`: a referenced Pal, passive, or skill is unknown;
- `Stale`: the source no longer contains the referenced instance;
- `Conflict`: multiple source records claim the same identity with different content;
- `Invalid`: the persisted member itself cannot be interpreted safely.

The projection should include a human-readable reason and enough raw identity to repair the entry. Status should be available to the UI and diagnostics without throwing exceptions.

## Save reload and corruption contract

### Document load pipeline

Load `your-pals.json` in stages so failure is scoped as narrowly as possible:

1. Read bytes without modifying the source file.
2. Parse the envelope and document version.
3. Apply document migrations in memory, with the same route validation used by the storage framework.
4. Parse groups independently.
5. Parse members independently within each group.
6. Preserve unknown fields and unknown member kinds where possible.
7. Resolve source references against a fresh source snapshot.
8. Return the document plus recovery diagnostics and a `CanPersistSafely`/read-only decision.

Required behavior:

- One malformed member must not hide valid members in the same group.
- One malformed group must not hide valid groups.
- An unknown Pal name must produce an unresolved entry, not throw from the whole serializer.
- A parse failure must not be converted into an ordinary empty document that is eligible for autosave.
- The original file must be preserved. Quarantine a damaged file using a recoverable name such as `.corrupt-<timestamp>`, or keep the original and write a separate recovery copy according to the storage convention.
- A partial/recovery view must be read-only until the user explicitly confirms a repair or replacement.
- The UI must show what was recovered, what is unavailable, and whether saving is currently safe.

The existing all-or-nothing customizations serializer and its `InternalToPal` eager lookup should be treated as compatibility code. The new Your Pals reader should not inherit that failure mode.

### Save reload behavior

When `Storage.ReloadSave` replaces the cached save contents:

- keep the persisted Your Pals document unchanged;
- rebuild the source snapshot from the newly loaded save and Global Pal Storage;
- re-run resolution for every persisted member;
- preserve stale/missing/conflicting entries in their groups;
- refresh the view model from the new projection rather than keeping old list references;
- invalidate solver inputs that are no longer resolved;
- retain dirty state and diagnostics if the source reload itself fails.

If a Pal is missing after reload, that is not proof that the user deleted it from Your Pals. It is a source-resolution event. The user must be able to remove or repair it intentionally.

### Save identity changes and removal

The session must verify the active save identity before loading or writing. A save being removed from the cache must not automatically delete its Your Pals document. Preserve it as recoverable/orphaned data until explicit user deletion or a later orphan-management feature.

Save parsing failure and user-authored Your Pals corruption are different failures:

- save cache recovery may use the existing save reload/revert behavior;
- Your Pals recovery must preserve the user-authored document and expose its diagnostics.

## Ownership and source snapshot architecture

### Shared ownership/scope resolver

Extract the semantic ownership logic currently embedded in catalog view-model behavior into a reusable resolver used by Catalog and Your Pals. It should return a semantic scope, source identity, and display arguments rather than localized text.

The resolver must support:

- selected save ownership;
- Global Pal Storage ownership through its parent `ISavesLocation`;
- future source kinds without making view models understand storage paths;
- a stable source identity for diagnostics and persisted references.

### Source snapshot normalizer

Create one source snapshot boundary that:

- reads current game/storage lists;
- filters records that cannot be safely identified;
- applies the model's duplicate/conflict policy;
- records duplicate and conflict diagnostics;
- never mutates the original cached save lists;
- can be rebuilt after a save reload.

The snapshot is runtime state. It is not the persisted Your Pals document.

### Recommended responsibilities

Keep responsibilities narrow even if the first implementation lives in a small number of files:

- `SavePalsSession`: owns one active `SaveIdentity`, loaded document, source snapshot, resolver, recovery state, dirty state, and save coordination.
- `YourPalsDocumentStore`: reads/writes the versioned document and returns structured recovery results.
- `PalReferenceResolver`: maps stable persisted references to current source snapshots and produces statuses.
- `YourPalsQueryState`: in-memory filters, sorting, grouping, selection, and pagination/virtualization state.
- `YourPalsViewModel`: presentation commands and observable rows; it does not implement serialization or source lookup.
- `SolverSourceAdapter`: converts only valid resolved entries into the solver's existing input contract.

This prevents `SolverPageViewModel` from becoming a persistence, source-resolution, query, migration, and UI “god object”. It may consume the session or a page-facing facade, but it should not own global storage or the document writer.

### Concurrency and writer ownership

Initially, enforce one writer per `SaveIdentity` in process. A session should serialize edits and writes, reject stale writes from an old session, or reload/merge before committing. Avoid a second independent writer in Inspect.

If multi-window editing is ever required, add an explicit document revision/compare-and-swap protocol. Do not assume in-memory caching alone will protect against concurrent updates.

## Your Pals UI

The first tab should be useful even when some data is unavailable:

```text
Your Pals
Save: <selected save>     [Reload] [Save status / recovery details]

[Groups / collections]     [Search] [filters] [sort]
                           [resolved / stale / unresolved status]

Group: <name>              Pal rows
                           Name | source | status | key fields | actions

                           [details / repair / remove]
```

### Initial read-only inventory

Start with a read-only view that proves the source snapshot, stable identity, resolution statuses, and refresh behavior. It should show:

- source and save scope;
- resolved entries;
- stale, unresolved, conflict, and invalid entries;
- recovery diagnostics and the reason an entry is excluded from the solver;
- stable selection across refresh where possible.

Do not add destructive or editing commands until the document store, dirty-state handling, and recovery contract are covered by tests.

### Query behavior

Query state should be in memory per active save at first. Do not mix transient search/filter/sort state into the persisted document unless product requirements demand it.

Use stable secondary ordering by `PalEntryKey` after the visible sort field. Select rows by stable key, not by index. Locale-aware sorting should be refreshed when the application locale changes without changing persisted group membership.

Prefer a virtualized/table-style row projection so thousands of source entries or future groups do not require rebuilding the entire visual tree for every keystroke.

## Save-scoped group management

### Capabilities for the first editing milestone

The first write-enabled milestone may support:

- create, rename, reorder, and delete groups;
- add an existing resolved game Pal by stable source reference;
- add a manual Pal definition;
- remove a member from a group without deleting the source Pal;
- inspect and repair unresolved/stale entries;
- save and reload without losing diagnostics.

Import/export, cross-save copying, templates, drag-and-drop, and advanced rule-based collections should follow the stable document contract rather than shape it prematurely.

### Document DTO shape

The exact C# types can evolve, but the persisted shape should have explicit type/version and stable IDs. Conceptually:

```text
{
  "documentType": "your-pals",
  "documentVersion": 1,
  "ownerSaveIdentity": { ... },
  "groups": [
    {
      "groupId": "...",
      "name": "...",
      "order": 0,
      "members": [
        {
          "palEntryKey": "...",
          "kind": "imported-reference",
          "sourceIdentity": { ... },
          "sourceKey": "...",
          "instanceId": "...",
          "lastKnownInternalName": "..."
        },
        {
          "palEntryKey": "...",
          "kind": "manual-definition-reference",
          "manualDefinitionId": "..."
        }
      ]
    }
  ],
  "manualDefinitions": [ ... ]
}
```

Unknown properties should be retained where the JSON library and DTO boundary permit. Unknown member kinds should be preserved as opaque/unresolved records rather than discarded during a newer-version read.

### Clean-start persistence

Your Pals owns its save-scoped `your-pals.json` document from the first write. Existing Inspect custom-container data is not imported, mirrored, or used as a fallback. A malformed or missing Your Pals document remains a visible recovery state and is never replaced implicitly with an empty document.

### Solver integration

`SolverSourceAdapter` should include only:

- resolved game Pal instances with valid identity;
- valid manual definitions converted to the solver's existing input shape.

Unresolved, stale, conflicting, and invalid entries stay in Your Pals but are excluded from calculations with an explanatory status.

Manual definitions need an ephemeral stable solver identity such as `manual:<ManualDefinitionId>` while in memory. This identity must not be persisted as a game `InstanceId` or used to overwrite a real save Pal.

Reuse the existing model-level duplicate/conflict semantics. Add adapter tests rather than changing solver behavior globally for the tab.

## Lifecycle and failure states

### Active save session

The application should create or activate one `SavePalsSession` when a save is selected, load the save-owned document, build a fresh source snapshot, and publish a resolved projection. Save reload events refresh the same session or replace it atomically; they must not leave views attached to old cached lists.

When switching saves:

- flush or explicitly preserve dirty state for the old session;
- verify the new identity before loading its document;
- clear transient query/selection state unless the selection key still exists in the new save;
- never display the old document under the new save while loading.

### No-save state

The no-save view should explain that persistent Your Pals data requires a selected save. Commands that would create save-owned data are disabled or clearly labeled as temporary draft actions. A future “presets” feature can provide persistent no-save groups with its own global document and explicit copy/import semantics.

### Recovery state

The view should distinguish:

- healthy and writable;
- partially recovered and read-only;
- source entries unavailable but document writable;
- write failed and dirty;
- migration pending or failed;
- orphaned because the owning save is no longer available.

These states should be modeled, not inferred from a localized status string.

## Roadmap

### Phase 0 — Contract and safety decisions

- Finalize canonical `SaveIdentity` fields and serialization rules.
- Decide the save data path and document name for `your-pals.json`.
- Define `YourPalsDocument`/DTO versioning and unknown-field policy.
- Define source identities for save-owned data and Global Pal Storage.
- Define status/recovery enums and user-visible diagnostics.
- Document Your Pals as the sole writer for its save-scoped document.
- Add acceptance tests for “missing Pal does not delete the member” and “corrupt document is never overwritten by an empty fallback”.

### Phase 1 — Reusable save/session foundation

- Implement a save-scoped session boundary independent of page lifetime.
- Implement source snapshot construction and shared ownership/scope resolution.
- Centralize atomic runtime document writes and backup behavior for the new document.
- Make dirty, read-only, recovery, and write-failure states explicit.
- Subscribe to save reload/switch events through one refresh path.
- Preserve existing solver, Catalog, target, and Inspect behavior.

### Phase 2 — Tolerant read path

- Implement tolerant DTO parsing and per-group/per-member recovery.
- Preserve unknown fields and unknown member kinds through the DTO boundary.
- Keep malformed documents read-only until explicitly repaired.

### Phase 3 — Your Pals read-only tab

- Build the page over the session and resolved projection.
- Display groups, source scope, stable keys, and all resolution statuses.
- Add reload/refresh and recovery details.
- Add table/virtualized rendering and stable selection.
- Verify that a save reload refreshes entries and retains stale members.

Implemented in the current milestone: the solver page now hosts a read-only Your Pals tab backed by `SavePalsSession`. It exposes grouped persisted members, the current normalized source snapshot, recovery diagnostics, atomic refresh behavior, virtualized tables, and stable member selection. Historical application-wide storage migrations remain the persistence foundation; Your Pals starts with its own clean save-scoped document and does not import legacy Inspect containers.

### Phase 4 — Query and presentation state

- Add search, filters, sorting, status filters, and group selection.
- Keep query state in memory per active save.
- Add locale-safe display sorting with stable key tie-breakers.
- Measure rendering with large collections before adding richer presentation features.

Implemented in the current milestone: query state now lives on `SavePalsSession` and is reused when the tab view model is recreated for the same save. The read-only projection supports culture-aware search, group/status filters, selectable sort fields and direction, stable `PalEntryKey` tie-breakers, locale refresh, and a virtualized table path exercised with a 2,500-member projection test. Query state is not serialized into `your-pals.json`.

### Phase 5 — Save-scoped editing and solver integration

- Add group and member commands through the session's single writer.
- Add manual definition editing with raw-value preservation.
- Add repair/rebind flows for stale and unresolved references.
- Add `SolverSourceAdapter` and exclude unusable entries without removing them.
- Make save failures visible and retryable without clearing dirty state.

Implemented in the current milestone: `SavePalsSession` is the single mutation boundary for group creation/rename/reorder/delete, imported-member add/remove, manual-definition add/update, and stale imported-reference rebinding. The Your Pals tab exposes those commands plus an explicit Save action. Manual raw values remain in the versioned document while known definitions resolve to ephemeral `manual:<ManualDefinitionId>` solver identities; stale, unresolved, conflicting, and invalid members remain visible and are excluded by `SolverSourceAdapter`. Failed saves leave the session dirty and retryable.

### Phase 6 — Inspect integration

- Keep Inspect custom containers separate from the Your Pals document.
- Add explicit import/export only if it becomes a separate product feature.
- Keep the Your Pals writer single-owner and save-scoped.

Implemented in the current milestone: Inspect custom-container records remain outside the Your Pals source snapshot and document. The solver source switch is explicit: it uses either the existing Inspect-backed source or the save-scoped Your Pals projection, never an implicit merge. No legacy save-file import or compatibility path is added.

### Phase 7 — Future features built on the stable contract

These should be separate, explicit features:

- global presets/templates with copy-on-write into a selected save;
- intentional cross-save copy/import and rebinding;
- export/import with versioned package metadata;
- smart collections and advanced filters;
- drag-and-drop between groups;
- bulk repair and duplicate resolution;
- orphaned-document management;
- multi-window or external-edit conflict detection;
- schema evolution tooling and user-facing recovery reports.

## Data correctness requirements

- A save-scoped document cannot be loaded under a different save identity without an explicit migration/rebind action.
- Empty `InstanceId` values are never treated as stable imported identity.
- Duplicate equivalent non-empty source identities are deduplicated consistently with the model policy.
- Conflicting records are retained as conflict diagnostics and excluded from the solver.
- Missing catalog names, passives, and skills are retained as raw unresolved values.
- A missing source Pal does not remove the persisted group member.
- A malformed member does not remove valid members or groups.
- Deleting a group/member is an intentional command, not a side effect of resolution.
- Save reloads rebuild source projections and invalidate stale solver inputs.
- Save removal does not automatically delete user-authored Your Pals data.
- Every persisted document has a schema/version boundary and an explicit future migration path.

## Persistence and recovery requirements

- Reads are non-destructive.
- New runtime writes are atomic and backed up where feasible.
- Failed writes preserve the previous valid file and keep in-memory dirty state.
- Corrupt files are preserved/quarantined with enough information for recovery.
- Partial recovery is read-only until the user confirms replacement or repair.
- Unknown future fields and member kinds are not silently discarded.
- Recovery diagnostics are available to both logs and the UI.
- The document store does not return an ordinary empty document for an unsafe parse failure.

## Testing strategy

Keep the existing test suite green before and throughout the work. The persistence merge already has useful manifest, migration, path-safety, DTO round-trip, and atomic-write coverage. Extend it rather than bypassing it.

### Model and session tests

- stable identities and source scopes, including Global Pal Storage;
- duplicate equivalent records and conflicting records;
- save reload with replaced cached lists;
- missing instance after reload remains stale;
- unknown catalog/passive/skill values remain unresolved and raw;
- switching saves cannot leak groups or selection across sessions;
- no-save state cannot persist save-owned data;
- manual definitions receive ephemeral solver IDs only.

### Persistence tests

- healthy empty and populated documents round-trip;
- unknown Pal/internal names do not fail the entire document;
- one malformed member preserves the rest of a group;
- one malformed group preserves other groups;
- corrupt envelope is preserved/quarantined and opens read-only;
- a failed write does not replace the previous valid document;
- backups and atomic replacement work on the supported platforms;
- unknown fields/member kinds survive a read/write cycle where supported;
- target, settings, and existing customizations persistence behavior remains unchanged.

### UI and solver tests

- status badges and recovery messages are accurate;
- stale/unresolved entries remain visible and are not offered as solver inputs;
- valid entries still reach the existing solver unchanged;
- edits mark the correct save dirty and write only that save's document;
- save switching/reload does not show stale rows from another session;
- Inspect custom-container operations do not create a second Your Pals writer;
- large groups remain responsive enough for the chosen table/virtualized approach.

## First milestone acceptance criteria

The first implementation milestone is complete only when all of the following are true:

- A selected save loads a versioned, save-owned Your Pals document through the existing persistence framework.
- A missing Pal, missing source instance, unknown skill, or duplicate conflict is represented as a visible status and does not delete persisted data.
- A malformed entry cannot hide valid groups/members.
- An unsafe parse is preserved and cannot be silently overwritten by an empty fallback.
- Save reload rebuilds source resolution and retains stale entries.
- Global Pal Storage has a distinct, stable source identity.
- The read-only tab does not change existing solver, Catalog, target, or Inspect behavior.
- Runtime writes keep a recoverable backup of the previous known-good Your Pals document.
- The relevant persistence, model, session, and UI/solver tests cover the above behaviors.

## Non-goals for this roadmap revision

- Rewriting all existing persistence in one change.
- Changing the model's breeding rules for the new tab.
- Automatically repairing or deleting user data based only on current catalog contents.
- Making persistent no-save collections before save ownership is settled.
- Adding cross-save synchronization without an explicit import/rebind design.
- Importing or synchronizing existing Inspect custom-container data.

## Recommendation

Proceed with the save-scoped session and tolerant resolution layers first. Treat `your-pals.json` as a durable user document, not a cache of currently resolvable Pal objects. Add the read-only tab, then editing through the same canonical session writer.

## Future Feature PRD: Reuse and Portability

This section is a product proposal for features that build on the current
save-scoped contract. It is not a migration decision and does not change the
current persistence contract. In particular, these features must not introduce
an Inspect/customization fallback, implicit cross-save rebinding, or silent
deletion of unresolved data.

### Shared requirements

All four features must follow these rules:

- Save-owned data remains owned by exactly one `SaveIdentity`.
- The existing `SavePalsSession` remains the mutation boundary for a selected
  save. New global or package stores must have their own explicit writer.
- Operations that can change a save require a preview, an explicit user
  command, dirty-state tracking, atomic writes, and backup preservation.
- Missing sources, stale references, conflicts, unknown member kinds, and
  invalid manual values remain visible. They are never silently removed or
  converted into a different source identity.
- Imported IDs, group order, conflict handling, and duplicate handling must be
  deterministic.
- A failed operation must leave the destination document unchanged where
  possible, or leave it recoverable with a diagnostic and retry path.

### Global presets and templates

#### Goal

Let users reuse a prepared Your Pals layout or collection across multiple
saves without making that layout save-owned until it is applied.

#### Proposed behavior

- Store presets in a separate global document or store, never inside a selected
  save's `your-pals.json`.
- A preset has a stable preset ID, name, description, schema version, group
  definitions, ordering, and manual definitions.
- Applying a preset creates an independent copy in the selected save
  (copy-on-write). Later edits to either the preset or the save do not affect
  the other.
- Applying a preset must show a preview of new groups, copied manual
  definitions, skipped items, and unresolved imported references.
- Preset updates do not automatically update saves that previously used it.
  Live links can be considered later as a separate feature.

#### Source-reference rule

The first version should prefer group structure and manual definitions. A
save-owned imported reference cannot be assumed to resolve in another save.
Such a reference must either be excluded with an explanation, be copied as a
visible stale reference, or be handled through the explicit rebind flow below.
Global Pal Storage references may be copied only after the destination verifies
that the same source identity is available.

#### Identity and conflict rules

- Preset IDs are global and are not used as save-owned group or member IDs.
- New destination IDs are generated deterministically from the preset ID,
  destination save identity, and source occurrence; collisions use the same
  stable suffix rules as document repair.
- Applying the same preset again must offer an explicit mode such as
  `add as new`, `merge`, or `replace selected preset copy`; it must not guess.
- Deleting a preset never deletes any copies already applied to saves.

#### Acceptance criteria

- A preset can be created, renamed, duplicated, applied, and deleted without
  changing any save until the user applies it.
- Applying it to Save A and Save B produces independent save-owned documents.
- A preset containing save-owned references presents a clear unresolved/rebind
  choice rather than silently changing `SourceIdentity`.
- A failed preset apply leaves the destination document and backup valid.

### Explicit cross-save copy, import, and rebind

#### Goal

Let users intentionally move Your Pals data between saves while preserving
ownership and making source-identity changes visible.

#### Proposed behavior

The operation must have a source save, a destination save, a selected set of
groups/members, and an explicit operation mode:

- `Copy`: add independent destination-owned records and leave the source
  unchanged.
- `Import`: read a package or preset and apply it to the destination through
  the same preview and validation path.
- `Rebind`: keep the user's logical member but replace its source reference
  only after the user selects or confirms the destination source record.

#### Rebind safety

- Never rebind only because names match.
- Prefer an exact destination match by `(SourceIdentity, InstanceId)`.
- If the source identity changes, require an explicit mapping step. `SourceKey`
  and display names may help the user choose, but they are not proof of
  identity on their own.
- If more than one destination record matches, show the conflict and require a
  choice. If none matches, keep the member stale.
- A rebind must preserve the stable `PalEntryKey` when possible so UI
  selection and diagnostics remain useful.
- Rebinding must not alter the source save or any source Pal record.

#### Merge and conflict policy

The user must choose how destination collisions are handled: add as a new
group/member, merge into an existing group, skip, or replace selected data.
The default must be non-destructive. Duplicate removal is allowed only under
the documented deterministic first-wins rule or after explicit confirmation.

#### Acceptance criteria

- Copying from Save A to Save B never causes Save A to become dirty.
- Every source reference that cannot be proven valid in Save B remains visible
  and unresolved.
- Rebind previews show old identity, new identity, reason, and affected rows.
- Repeating the same operation produces the same result for the same selected
  inputs and conflict choice.
- The destination can be saved, retried, or cancelled without losing the
  source document.

### Versioned export and import packages

#### Goal

Provide a portable, inspectable artifact for backup, sharing, support, and
moving selected Your Pals data between machines or saves. This is separate
from copying a raw `your-pals.json` file.

#### Proposed package contents

A package should have an explicit package version and a manifest containing:

- package type and package version;
- export timestamp and application version;
- source save identity, when the export came from a save;
- selected groups and manual definitions;
- imported source references and last-known display metadata;
- optional diagnostics and content checksums.

The package format may be a JSON file or a bounded archive, but the choice
must be made before implementation. If an archive is used, extraction must
reject path traversal, absolute paths, duplicate entries, oversized files, and
unexpected content types.

#### Import behavior

- Inspect and validate the manifest before changing the destination.
- Show a preview with additions, collisions, stale references, unknown fields,
  and unsupported package-version warnings.
- Apply only through an explicit destination save session.
- Preserve unknown fields when the package version is understood. Reject an
  unsupported future package version without rewriting it.
- Treat package import as an explicit cross-save operation; it must not become
  a legacy fallback for a missing or corrupt `your-pals.json`.
- Keep the original package unchanged and preserve the destination backup.

#### Acceptance criteria

- Export then import round-trips groups, ordering, manual raw values, unknown
  fields, and unresolved references where supported.
- Importing into another save never silently changes the source identity of a
  reference.
- A malformed or unsupported package is rejected without modifying the
  destination document.
- The package can be inspected and diagnosed without executing arbitrary
  content.

### Smart collections and advanced filters

#### Goal

Allow dynamic collections based on rules instead of requiring users to move
members manually between static groups.

#### Proposed behavior

- Keep ordinary groups as explicit user-owned membership lists.
- Store a smart collection as a named rule definition, not as a second copied
  list of members.
- Evaluate it against the current resolved projection after source refresh,
  catalog changes, reloads, and document repair.
- Support a constrained typed rule set first: field comparisons, text search,
  status/source filters, `all`/`any`/`not`, and numeric ranges where the value
  is valid.
- Use internal names, enum values, and stable identities in rules. Never
  persist localized display text as the rule's meaning.
- Keep the current query state (search, sort, and temporary filters) separate
  from persisted smart-collection definitions.

#### Missing and invalid data

Rule evaluation must distinguish `false` from `unknown`. For example, an
unknown passive is not proof that a Pal does not have that passive. The UI
should let the user choose whether unknown values are excluded, included, or
shown separately. Invalid manual fields and stale references must remain
visible in the collection's diagnostics.

#### Performance and safety

- Evaluate rules against the normalized source/projection model, not raw UI
  controls or database objects.
- Use stable ordering by `PalEntryKey` after rule evaluation.
- Debounce interactive edits, preserve virtualization, and measure large
  collections before adding expensive derived predicates.
- Do not support arbitrary code, expressions, or user-provided queries.
- Unknown future rule types remain visible and disabled; they are not deleted
  during a normal save.

#### Acceptance criteria

- A smart collection updates after source refresh without rewriting its rule.
- The same document and source snapshot produce the same membership regardless
  of UI recreation or locale.
- Static group membership is unaffected by smart-collection evaluation.
- Unknown, stale, and invalid records are explainable rather than silently
  excluded as if they were valid non-matches.
- Large collections remain responsive under the existing virtualized table
  path.

### Decisions required before implementation

These features should not be implemented until the following choices are
approved:

- global preset storage location and whether presets may contain imported
  references;
- apply/merge/replace semantics and idempotency rules for presets and imports;
- the exact rebind mapping UI and allowed identity evidence;
- package container format, size limits, and checksum policy;
- whether smart-collection definitions live in `your-pals.json` or a separate
  save-owned document;
- the supported rule vocabulary and behavior for unknown values;
- how these features are localized, audited, and tested on Windows.
