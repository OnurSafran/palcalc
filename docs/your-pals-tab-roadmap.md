# Your Pals Tab: Architecture and Roadmap

Status: **Architecture proposal. No code changes are included in this document.**

## Purpose

Create a first-class **Your Pals** tab in the main save workspace. It should provide the useful parts of the current Inspect workflow—Pals from different game locations, their data, and source context—inside the normal tabbed UI.

The feature should also become the home for user-managed Pal groups/containers. A user should be able to create a named collection and add Pal definitions or references from the UI, including before a game save is loaded.

The first implementation should establish the data and lifecycle architecture. Search, filtering, and sorting should fit the design from the start, but can be delivered incrementally after the source model and tab shell are stable.

## Product direction

The main save workspace should converge on these tabs:

1. **Breeding** — the existing solver workflow.
2. **Your Pals** — owned Pals, game locations, user groups, and Pal details.
3. **Pal Catalog** — the complete PalDex and breeding/work-suitability catalog.

Your Pals and Pal Catalog are different views:

- **Your Pals** is instance/source-oriented. Two owned Cattiva instances may appear as two rows with different levels, genders, nicknames, locations, and IDs.
- **Pal Catalog** is species/variant-oriented. It shows every database Pal, including Pals that are not owned.

The existing Inspect menu/window should be retired after Your Pals reaches parity for the supported user workflows. Raw save diagnostics are not part of the normal Your Pals experience.

## Current architecture

### Existing main tabs

- [`PalCalc.UI/View/SolverPage.xaml`](../PalCalc.UI/View/SolverPage.xaml) owns the main `Breeding` and `Pal Catalog` tabs.
- [`PalCalc.UI/ViewModel/SolverPageViewModel.cs`](../PalCalc.UI/ViewModel/SolverPageViewModel.cs) owns the selected save, solver state, Pal targets, and asynchronous Pal Catalog loading.
- [`PalCalc.UI/View/Inspector/PalBreedingCatalogView.xaml`](../PalCalc.UI/View/Inspector/PalBreedingCatalogView.xaml) already provides a two-pane catalog/detail pattern.

### Existing Inspect workflow

- [`PalCalc.UI/View/CommonSaveControlsButton.xaml`](../PalCalc.UI/View/CommonSaveControlsButton.xaml) exposes the Inspect command from the save menu.
- [`PalCalc.UI/ViewModel/CommonSaveOperationsViewModel.cs`](../PalCalc.UI/ViewModel/CommonSaveOperationsViewModel.cs) opens the modeless Inspect window.
- [`PalCalc.UI/View/Inspector/SaveInspectorWindow.xaml`](../PalCalc.UI/View/Inspector/SaveInspectorWindow.xaml) contains Search and Details tabs.
- [`PalCalc.UI/View/Inspector/SearchView.xaml`](../PalCalc.UI/View/Inspector/SearchView.xaml) is organized around a container/source tree and container grids. Its search criteria highlight matching slots; it is not a flat, sortable Pal inventory.
- [`PalCalc.UI/View/Inspector/SaveDetailsView.xaml`](../PalCalc.UI/View/Inspector/SaveDetailsView.xaml) exposes container slots and low-level detected/raw properties.

The Inspect window receives a cached-save snapshot when opened. It is not the right lifecycle boundary for a first-class tab because save reload and workspace state belong to the main save page.

### Existing Pal and customization data

- [`PalCalc.UI/Model/CachedSaveGame.cs`](../PalCalc.UI/Model/CachedSaveGame.cs) exposes `OwnedPals`, the primary parsed source of Pal instances.
- `PalInstance` already contains the useful instance data: species, nickname, level, gender, location, expedition status, passives, active skills, IVs, owner ID, and instance ID.
- [`PalCalc.UI/ViewModel/Inspector/PalBreedingOwnedInstanceViewModel.cs`](../PalCalc.UI/ViewModel/Inspector/PalBreedingOwnedInstanceViewModel.cs) already formats Pal instance name, gender, level, location, and expedition state for the breeding detail pane.
- [`PalCalc.UI/ViewModel/SaveCustomizationsViewModel.cs`](../PalCalc.UI/ViewModel/SaveCustomizationsViewModel.cs) manages named custom containers and debounced persistence.
- [`PalCalc.UI/Model/CustomContainer.cs`](../PalCalc.UI/Model/CustomContainer.cs) currently stores a label and a list of serialized `PalInstance` values.
- Customizations currently require a save identity. This does not satisfy the requirement to create/manage groups before a game is loaded.

## Recommended domain boundary

Do not make the new tab read directly from `CachedSaveGame`, `CustomContainer`, and the solver's source classes in XAML. Introduce a small source/collection boundary between stored data and the UI.

### Source types

The tab should present a common read model for these sources:

1. **Imported game sources**
   - Party
   - Palbox
   - Base work areas
   - Viewing cages
   - Dimensional storage
   - Global storage
   - Expedition state
2. **User groups**
   - Named collections created in the UI.
   - May contain references to loaded game Pals, manually defined Pals, or both.
3. **Optional future sources**
   - Imported external lists
   - Saved breeding sets
   - Shared/server scopes

Game sources should be read-only projections of the parsed save. User groups should be editable. The UI must make that distinction explicit so an edit to a group cannot be mistaken for an edit to the actual save file.

### Stable Pal references

The current custom-container model stores full `PalInstance` values. That is convenient for serialization but too narrow for pre-save management and can duplicate game data.

The new architecture should distinguish:

- **ImportedPalReference** — identifies a game/save source and an `InstanceId`; resolves to a live `PalInstance` when that save is loaded.
- **ManualPalDefinition** — a user-created Pal with species, optional nickname, gender, level, IVs, passives, and other planning fields; it does not require a save.
- **ResolvedPalEntry** — the UI read model combining the reference/definition with source, location, availability, and display data.

Unresolved imported references should remain visible in a group with an explanatory state. They should not silently disappear and should not be passed to the solver as if they were real owned instances.

### Group/container terminology

Use one clear distinction:

- **Pal source**: where a Pal comes from, such as Palbox or a user group.
- **Group**: a user-managed named collection.
- **Game container**: a parsed in-game location/container.

The existing `CustomContainer` can be supported through a compatibility adapter and migrated later. Avoid introducing a second unrelated container abstraction that has the same label-plus-contents shape.

## Your Pals tab design

The tab should be expandable over time without replacing its core data model.

### Initial layout

```text
Your Pals
[search] [source filter] [status filter] [sort] [create group]

┌─ Sources / Groups ───────┬─ Pal list or cards ──────────┬─ Selected Pal ──────┐
│ Save                      │ icon  #  name  gender level │ name / nickname    │
│  Party                    │ ...                          │ location           │
│  Palbox                   │                              │ level / gender     │
│  Bases                    │                              │ passives / IVs     │
│  Storage                  │                              │ source / ID        │
│ Groups                    │                              │                    │
│  Breeding team            │                              │                    │
└──────────────────────────┴──────────────────────────────┴────────────────────┘
```

The source tree and selected detail pane should be collapsible. At narrow widths, the detail pane can move below the list or open as a selected-item panel.

### Initial read-only inventory behavior

The first usable version should show one row per resolved Pal instance and include:

- localized Pal name and PalDex number;
- nickname, when present;
- icon;
- gender and level;
- game source and location;
- expedition indicator;
- selected Pal details such as passives, active skills, IVs, rank, owner, and instance ID.

The list should use a virtualizing `ListView`/`GridView` or equivalent. A table is a better default than an icon-only grid because the feature is about instance data and source context.

### Search, filtering, and sorting

These controls can be delivered in the first UI pass or added immediately after the shell. The architecture must not hard-code the list to one ordering.

Recommended query fields:

- localized name;
- model/internal name;
- nickname;
- PalDex number, including variant notation;
- source/group name;
- location text;
- instance ID when debugging is enabled.

Recommended filters:

- all Pals;
- selected source/group;
- owned game Pals vs manual Pals;
- on expedition;
- gender;
- location type;
- valid/resolved vs unresolved.

Recommended sorts:

- PalDex number;
- localized name;
- nickname;
- level;
- gender;
- location/source;
- expedition status.

Search/filter/sort state should belong to the Your Pals feature state, not to the underlying source objects.

## Group and Pal container management

### User capabilities

The UI should eventually support:

- create a group;
- rename a group;
- delete a group with confirmation;
- add a loaded game Pal to a group;
- remove a Pal from a group without deleting the Pal;
- add a manual Pal definition before a save is loaded;
- edit manual Pal fields;
- show unresolved references after changing or unloading saves;
- duplicate a group or save it as a reusable preset;
- use a group as a solver source.

### Persistence model

The current save-scoped customizations are a useful compatibility starting point, but pre-save management requires a workspace-level store.

Recommended persistence layers:

1. **Application/workspace groups**
   - Available before any save is loaded.
   - Persist named groups and manual Pal definitions.
   - Store imported references using save identity plus instance ID.
2. **Save-specific compatibility data**
   - Continue reading existing `custom-containers.json` during migration.
   - Convert old full `PalInstance` entries into imported references when their save and instance ID are known.
   - Preserve data that cannot be resolved as an explicit unresolved/manual entry instead of dropping it.

Persistence should be versioned and written atomically. A group edit must not mutate the game save, and a game reload must not erase workspace groups.

### Solver integration boundary

The solver currently consumes Pal source selections and `PalInstance`-based references. Your Pals should expose a source adapter that returns only solver-usable resolved entries.

Manual or unresolved entries can remain visible in Your Pals but must be marked as unavailable to the solver until they have enough data to become valid solver inputs.

## Architecture changes

### Shared ownership/scope resolver

The server/single-player scope logic currently lives inside [`PalBreedingCatalogViewModel`](../PalCalc.UI/ViewModel/Inspector/PalBreedingCatalogViewModel.cs). Extract it so Pal Catalog and Your Pals use the same scope.

The resolver should return:

- active scope kind: single player, player, guild, or unresolved;
- human-readable scope description;
- deduplicated game Pal instances;
- source/container metadata;
- diagnostics for unresolved or malformed records.

This prevents the Catalog and Your Pals tabs from disagreeing about which Pals belong to the current user/server scope.

### Shared Pal collection/query layer

Proposed responsibilities:

- source tree construction;
- source selection;
- flattening selected sources into Pal entries;
- deduplication policy;
- resolution of imported references;
- query state and collection view;
- selected-entry identity and restoration;
- capability flags for read-only game sources vs editable groups.

The UI should bind to this layer through `YourPalsViewModel`. XAML should not know how server ownership, save identity, or custom-group persistence works.

### Save/workspace lifecycle

The main `SolverPageViewModel` should own the active Your Pals session alongside the existing Catalog session.

Required lifecycle behavior:

- construct the feature when a save workspace opens;
- load workspace groups even when no save is available;
- resolve imported references when a save becomes available;
- refresh game-source projections after a save reload;
- retain workspace groups and query state across tab changes;
- mark references unresolved when a save is unloaded or no longer contains an instance;
- cancel background work when the solver page is disposed.

Do not build Your Pals as a modeless snapshot window. The existing Inspect snapshot behavior is one of the reasons the feature needs a new boundary.

## Roadmap

### Phase 0 — Contract and migration design

- [ ] Confirm that “Your Pals” is instance/source-oriented and “Pal Catalog” remains species-oriented.
- [ ] Define `PalSource`, `PalGroup`, imported reference, manual definition, and resolved entry contracts.
- [ ] Define save identity and instance ID behavior for imported references.
- [ ] Decide workspace persistence location and schema versioning.
- [ ] Decide whether existing custom containers become groups, game-source projections, or a compatibility-only format.
- [ ] Document unresolved-reference behavior.
- [ ] Document which low-level Inspect diagnostics are intentionally removed.

### Phase 1 — Shared data/session architecture

- [ ] Extract ownership/scope resolution from `PalBreedingCatalogViewModel`.
- [ ] Add a shared source projection for game containers and locations.
- [ ] Add a workspace group store independent of an active save.
- [ ] Add adapters for existing `CustomContainer` and `SaveCustomizationsViewModel` data.
- [ ] Add a resolver from imported references/manual definitions to UI entries.
- [ ] Add deduplication and conflict diagnostics for repeated `InstanceId` records.
- [ ] Add a feature session owned by `SolverPageViewModel`.
- [ ] Add cancellation and refresh behavior for save reload and page disposal.

### Phase 2 — Your Pals tab shell

- [ ] Add the Your Pals tab beside Breeding and Pal Catalog.
- [ ] Add a collapsible source/group tree.
- [ ] Add the virtualized Pal list.
- [ ] Add selected-Pal details.
- [ ] Show source, location, scope, expedition, and unresolved state.
- [ ] Reuse existing Pal detail formatting where it fits, without coupling the new tab to SearchViewModel.
- [ ] Add keyboard selection and accessible labels.

### Phase 3 — Query controls

- [ ] Add text search.
- [ ] Add source/location/status filters.
- [ ] Add sorting with stable secondary ordering.
- [ ] Add empty-state and no-match-state messages.
- [ ] Restore query/selection state per save/workspace.
- [ ] Reapply sorting when the locale changes.

### Phase 4 — User groups and manual Pals

- [ ] Create, rename, and delete groups.
- [ ] Add/remove resolved game Pal references.
- [ ] Add manual Pal definitions before a save is loaded.
- [ ] Edit manual Pal fields.
- [ ] Show unresolved imported references without dropping them.
- [ ] Add group-level actions such as duplicate, clear, and select all.
- [ ] Expose groups as optional solver sources.

### Phase 5 — Inspect migration and removal

- [ ] Compare Your Pals against the user-facing workflows in SearchView.
- [ ] Migrate custom-container editing before removing SearchView.
- [ ] Decide whether raw Details data has a supported replacement or is intentionally retired.
- [ ] Remove the Inspect menu entry.
- [ ] Remove `InspectSaveCommand` and the modeless window path.
- [ ] Remove `SaveInspectorWindowManager` lifecycle hooks.
- [ ] Delete orphaned Inspect views/view models only after reference search and build validation.
- [ ] Remove obsolete localization entries after checking every caller.
- [ ] Update documentation and release notes.

### Phase 6 — Later enhancements

- [ ] Preset groups shared across saves.
- [ ] Group templates for breeding teams, workers, or expedition parties.
- [ ] Drag-and-drop between groups and sources.
- [ ] Bulk editing and multi-selection.
- [ ] Advanced passive/IV/stat filters.
- [ ] Export/import groups.
- [ ] “Send to Solver” and “Set as target” actions.
- [ ] Location-aware availability filters such as excluding expeditions or viewing cages.
- [ ] Optional raw/debug diagnostics for support builds.

## Detailed TODO list

### Data correctness

- [ ] Treat non-empty `InstanceId` as the identity of a game Pal.
- [ ] Deduplicate duplicate records before displaying or counting them.
- [ ] Detect conflicting records with the same ID and show a diagnostic state.
- [ ] Keep manual definitions distinct from imported game instances.
- [ ] Never let unresolved references silently enter solver inputs.
- [ ] Preserve expedition status and source location.
- [ ] Keep server scope consistent with Pal Catalog.

### UI behavior

- [ ] Make game sources read-only in the first version.
- [ ] Make groups visibly editable.
- [ ] Show the active save/scope in the header.
- [ ] Keep source tree, list, and detail selection synchronized.
- [ ] Provide a clear “no save loaded” state where manual groups remain usable.
- [ ] Provide loading/error states for save resolution.
- [ ] Support narrow-window layout by collapsing or stacking the detail pane.
- [ ] Keep the list virtualized for large saves.

### Persistence and compatibility

- [ ] Version the workspace/group schema.
- [ ] Write changes with a recoverable/atomic save path.
- [ ] Migrate existing save-scoped custom containers.
- [ ] Preserve unknown or unresolvable entries during migration.
- [ ] Keep save removal from deleting unrelated workspace groups.
- [ ] Define behavior when a save identity changes or a Pal instance is gone.

### Testing

- [ ] Scope resolver tests for single-player, player fallback, guild, and unresolved server saves.
- [ ] Source projection tests for every parsed location type.
- [ ] Duplicate/conflicting instance tests.
- [ ] Imported-reference resolution tests before and after save load.
- [ ] Manual group persistence tests with no active save.
- [ ] Query tests for search, filter, sort, and locale changes.
- [ ] Selection restoration tests after refresh and tab changes.
- [ ] Solver adapter tests rejecting unresolved/manual entries without required data.
- [ ] Migration tests for existing `custom-containers.json` data.
- [ ] Windows x64 UI build and manual visual checks.

## Acceptance criteria for the first architecture milestone

The architecture milestone is complete when:

1. A Your Pals session can exist without an active save.
2. Game locations and user groups are represented through one source abstraction.
3. Imported game references resolve by save identity and instance ID.
4. Manual Pal definitions can exist without a save.
5. Pal Catalog and Your Pals use the same server/single-player ownership scope.
6. Save reload refreshes game projections without deleting user groups.
7. The UI can later add search, filters, and sorting without changing source persistence.
8. Existing custom-container data has a migration path.
9. Inspect can be removed without losing custom-group management or required user workflows.

## Non-goals for the first implementation

- Editing the actual Palworld save file from the Your Pals tab.
- Moving a Pal between real in-game containers.
- Replacing the breeding solver.
- Reimplementing Pal breeding calculations in the inventory feature.
- Showing every raw save-reader field by default.
- Supporting multi-save synchronization beyond unresolved reference states.

## Open decisions

- [ ] Should workspace groups be global to the application, tied to a selected save, or support both scopes?
- [ ] Should a group contain only references, or also full manual Pal definitions?
- [ ] Should manual Pals be solver-usable immediately, or only after required fields are complete?
- [ ] Should game-source rows be deduplicated into one row per instance or preserve duplicate raw records with a warning row?
- [ ] Should the initial Your Pals tab include query controls, or land first with source grouping and a stable query-state API?
- [ ] Which raw Inspect diagnostics, if any, need a hidden/debug replacement?

## Recommendation

Implement the shared source/reference/session layer before removing Inspect. Then build Your Pals on that layer and migrate custom-container editing into it. Remove the modeless Inspect window only after the new tab can manage groups and expose the Pal data users rely on.

This keeps the feature open for search, filters, sorting, presets, and solver integration without forcing those features into the current container-grid implementation.
