# Solver: Pal Catalog, Breeding Availability, and Work Suitability

Status: **MVP code complete; Windows release validation remains.**

## Current delivery status

### Implemented

- A `Pal Catalog` tab in the main Solver page, next to the solver tab.
- A complete, PalDex-ordered catalog from `PalDB.Pals`, including variants and entries without breeding recipes.
- A virtualized icon grid with localized names, PalDex numbers, owned counts, readiness text, tooltips, keyboard selection, search, filters, and sorting.
- A selected-Pal detail pane containing owned instances, gender and location information, expedition warnings, every breeding recipe, per-recipe availability, and expandable matching instance pairs.
- Work suitability in the same selected-Pal detail pane, showing each positive work type and level without requiring a second Pal-selection workflow.
- A pure model-layer calculator that:
  - accepts only concrete `MALE`/`FEMALE` owned-parent genders;
  - requires opposite genders and distinct non-empty instance IDs;
  - handles explicit gender-specific recipes and interchangeable parent order;
  - deduplicates repeated instance references and canonicalizes instance pairs;
  - reports malformed, conflicting, or unknown save records as `Unknown`;
  - retains exact pair counts while limiting displayed pairs to the first 100 per recipe.
- Single-player scope across all parsed owned-Pal locations, including expeditions.
- Server scope using the selected player's guild and shared containers when resolvable, with a direct-player fallback when the guild is unavailable and an explicit unknown scope when the selected player cannot be resolved.
- In-memory, per-save state for search, filter, sort, and selection.
- English and Turkish localization entries, non-color status text, and automation names/help text.

### Verification baseline from 2026-08-12

- `PalCalc.Model.Tests`: **25/25 passed** in the previous verification run.
- `PalCalc.UI.Tests`: includes a regression test that verifies Work Suitability follows Pal Catalog selection; Windows x64 execution remains required.
- `PalCalc.UI` Windows-targeted cross-build: **succeeded with 0 errors and 0 warnings** on the final incremental build.
- XAML compiled successfully as part of the UI build.
- All localization codes used by the feature exist in `LocalizationCodes.Designer.cs` and `LocalizationCodes.resx`.
- English and Turkish contain every feature localization key.
- `git diff --check` passed.

For the current integration change on 2026-08-13, `PalCalc.Model.Tests` builds successfully and `git diff --check` passes. The Windows-targeted UI build and UI test execution still require Windows x64 validation; the UI build did not complete on this host.

The Windows UI test assembly builds, but its tests could not run on the Apple Silicon development host because the project forces an x64 Windows test host. This is an environment limitation, not a passing UI-test result.

## What is left before the MVP release

These are the remaining release-validation tasks. They are not missing feature code unless validation exposes a defect.

### Release gates

1. **Run the UI and UI tests on Windows x64.**
   - Execute `PalCalc.UI.Tests` on a supported Windows machine.
   - Open the main Solver page and confirm that the Pal Catalog tab and its Work Suitability detail section load without binding errors.
2. **Complete the manual visual matrix.**
   - Test minimum (`680px`) and normal window widths.
   - Test light and dark themes, especially ready/missing/unknown contrast.
   - Test long localized and variant names, fallback icons, keyboard navigation, selection, tooltips, scrolling, and container recycling.
3. **Validate representative saves.**
   - Empty, one-Pal, and large single-player saves.
   - Every supported location: party, Palbox, base, viewing cage, dimensional storage, global storage, and expedition.
   - A server save with multiple players/guilds, shared-base Pals, an unresolved guild, and an unresolved selected player.
   - Duplicate references, conflicting IDs, missing IDs, non-concrete genders, and missing locations.
4. **Profile a large real save.**
   - Measure inspector-open time, catalog calculation time, memory use, and first selection/scroll latency.
   - Confirm that exact pair counting does not make saves with many same-species instances unacceptably slow.

### Non-blocking MVP follow-ups

- Add dedicated UI/view-model regression tests for catalog completeness, PalId ordering, state restoration, search/filter behavior, selection details, and server-scope fallback.
- Translate the new localization keys for locales other than English and Turkish. They currently use the application's English fallback.
- Replace the feature's fixed hexadecimal status brushes with reviewed theme resources after Windows light/dark contrast testing.
- The existing Save Inspector remains focused on Search and Details. Pal Catalog is intentionally owned by the Solver page so it shares the solver's selected-save context.

## Recommended next implementation order

1. Run the Windows UI tests and manual release matrix; fix only reproduced defects.
2. Add focused UI/view-model regression tests for the behaviors verified manually.
3. Profile a large save. If needed, split compact readiness from exact pair enumeration and construct recipe pair details only after selection/expansion.
4. Finish theme-brush review and community translations.
5. After the MVP ships, choose one post-MVP workflow improvement from the backlog below; solver integration is the highest-value candidate.

The remainder of this document preserves the original requirements and design rationale. Where its wording says “add” or “suggested,” use the delivery-status sections above as the source of truth for what is already implemented.

## Requested feature

When a user opens the main Solver page for a selected save file, provide a Pal Catalog tab that:

- lists every Pal species/variant in the game's Pal database;
- shows a grid of Pal icons when possible;
- shows lightweight information on hover, such as Pal number and name;
- opens a detail view when a Pal is clicked;
- shows that Pal's breeding data;
- indicates whether the current save contains a usable matching breeding pair, using a clear positive/negative visual state such as green/red text.

The intended context is the selected-save Solver page, so the availability state must be calculated from the selected save rather than from the global database alone.

## Interpretation for an MVP

The catalog should show **every Pal returned by `PalDB.Pals` when possible**, including variants and special/non-catchable entries, with save ownership shown as supplemental data. (`Pals` is an `IEnumerable<Pal>` backed by the `PalsById` dictionary.) This gives the user a complete checklist and makes missing targets visible. Special entries that have no icon or breeding recipe should remain visible with the existing fallback icon and an `Unavailable`/`No breeding data` status rather than being filtered out.

For “matching breeds,” the MVP should mean:

> There is at least one pair of distinct, in-scope `PalInstance` objects in the selected save whose Pal types satisfy a recipe for the selected child and whose actual genders are a valid male/female pairing.

This is stronger and more useful than checking whether the save merely contains the child Pal. Same-species recipes still require two distinct instances of opposite actual genders.

> [!IMPORTANT]
> Do not use `BreedingResult.Matches(...)` as the only usability check. Most embedded recipes use `WILDCARD` for both recipe parents, and `Matches(...)` treats each wildcard independently; by itself it accepts male/male, female/female, and `NONE` inputs. For actual owned instances, first require both genders to be `MALE` or `FEMALE` and require them to differ, then use `Matches(...)` (or an equivalent type-and-explicit-gender recipe check). `WILDCARD` and `OPPOSITE_WILDCARD` are solver/recipe concepts, not valid concrete owned-parent genders.

The UI should distinguish at least these states:

- **Ready / green:** a valid pair exists in the save.
- **Missing pair / red or neutral warning:** no valid pair exists, with a short explanation if possible (for example, missing species or gender).
- **Unavailable / neutral:** the Pal has no recipe in the breeding database (for example, a special/non-breedable entry).
- **Unknown / neutral:** the save data required for the calculation is missing or malformed. A failed save load normally prevents the inspector from opening, so this should be exceptional rather than a routine state.

Green/red should not be the only signal. Include text, an icon, or a tooltip so the state remains understandable for users with color-vision differences.

## Existing code and likely focus areas

### Solver shell

- [`PalCalc.UI/View/SolverPage.xaml`](../PalCalc.UI/View/SolverPage.xaml) owns the Solver and Pal Catalog tabs.
- [`PalCalc.UI/ViewModel/SolverPageViewModel.cs`](../PalCalc.UI/ViewModel/SolverPageViewModel.cs) constructs the catalog from the selected save, Pal database, breeding database, and game settings.
- [`PalCalc.UI/View/Inspector/PalBreedingCatalogView.xaml`](../PalCalc.UI/View/Inspector/PalBreedingCatalogView.xaml) contains the catalog grid and the selected-Pal detail pane, including Work Suitability.
- [`PalCalc.UI/ViewModel/Inspector/PalBreedingCatalogViewModel.cs`](../PalCalc.UI/ViewModel/Inspector/PalBreedingCatalogViewModel.cs) owns catalog selection and the shared Work Suitability detail state.

### Save-specific data

- [`PalCalc.UI/Model/CachedSaveGame.cs`](../PalCalc.UI/Model/CachedSaveGame.cs) exposes `OwnedPals`, which is the primary source for the save's Pal instances.
- `CachedSaveGame.OwnedPals` includes instance-level information such as `Gender`, `Level`, `NickName`, `Location`, passive skills, and IVs (`IV_HP`, `IV_Shot`/`IV_Attack`, `IV_Defense`).
- `PalInstance.Pal` identifies the database Pal; a non-empty `InstanceId` must be used to keep two actual instances separate.
- Location/container data is already parsed, but the new feature should not need to rebuild the container tree just to calculate breeding availability.
- `OwnedPals` can contain duplicate references to the same instance (the existing Details view explicitly diagnoses this). Deduplicate by non-empty `InstanceId` before computing owned counts or pairs. Treat conflicting records for the same ID as malformed/unknown rather than silently counting both.
- Server saves may contain Pals owned by multiple players or guilds. When it is straightforward to resolve the selected player and guild, availability should use the selected player's guild, including shared-base Pals. Single-player saves are the primary use case and should include all owned Pals in that save. If server ownership cannot be resolved reliably, the UI must disclose the fallback scope rather than silently mixing guilds.
- Include owned Pals from every parsed location when possible: party, Palbox, base, viewing cage, dimensional storage, global storage, and expeditions. Show each matched instance's location and `IsOnExpedition` state so the user can judge whether it is convenient to use. The initial calculation should not exclude an instance only because it is on an expedition.

### Pal catalog and icons

- [`PalCalc.Model/PalDB.cs`](../PalCalc.Model/PalDB.cs) exposes the complete catalog through `Pals` (`IEnumerable<Pal>`) and `PalsById` (`Dictionary<PalId, Pal>`), plus metadata such as `Id`, `Name`, `InternalName`, and breeding gender probabilities.
- [`PalCalc.UI/ViewModel/Mapped/PalViewModel.cs`](../PalCalc.UI/ViewModel/Mapped/PalViewModel.cs) wraps catalog Pals, supplies localized `Name`/`Label`, and caches instances via `PalViewModel.Make(...)`.
  > [!IMPORTANT]
  > Note that `PalViewModel.All` orders items by localized `Name.Value` alphabetically. For the catalog tab, items should be explicitly ordered by `ModelObject.Id` (`PalId`), which implements `IComparable<PalId>` to sort numerically by Paldex number with non-variants preceding variants.
- [`PalCalc.UI/Model/PalIcon.cs`](../PalCalc.UI/Model/PalIcon.cs) maps every embedded Pal to an `ImageSource`, with a fallback icon for missing resources.
- [`VirtualizingWrapPanel`](../PalCalc.UI/PalCalc.UI.csproj) (v2.1.1) is already included in `PalCalc.UI.csproj` and can be used for virtualized icon grid presentation. Follow the namespace and usage in `SaveDetailsView.xaml`, and verify virtualization with the chosen `ItemsControl`/scroll configuration; an existing Search-view comment notes that this package did not improve every layout.
- [`PalCalc.UI/View/PalCheckListWindow.xaml`](../PalCalc.UI/View/PalCheckListWindow.xaml) and [`PalCalc.UI/ViewModel/PalCheckListViewModel.cs`](../PalCalc.UI/ViewModel/PalCheckListViewModel.cs) are useful references for Pal list ordering and search behavior.

### Breeding data

- [`PalCalc.Model/PalBreedingDB.cs`](../PalCalc.Model/PalBreedingDB.cs) loads the embedded breeding data and exposes:
  - `BreedingByChild`: `Dictionary<Pal, Dictionary<GenderedPal, List<GenderedPal>>>` (child `Pal` -> `GenderedPal` parent 1 -> list of valid `GenderedPal` parent 2s);
  - `BreedingByParent`: `IReadOnlyDictionary<Pal, IReadOnlyDictionary<Pal, BreedingResult[]>>` (parent 1 `Pal` -> parent 2 `Pal` -> array of matching `BreedingResult` objects).
- [`PalCalc.Model/BreedingResult.cs`](../PalCalc.Model/BreedingResult.cs) contains `Matches(...)` and parent/child relationships. Its wildcard behavior is intentionally permissive for solver references and is insufficient by itself for validating two concrete owned instances.
- [`PalCalc.Model.Tests/PalBreedingDBTests.cs`](../PalCalc.Model.Tests/PalBreedingDBTests.cs) confirms parent-order commutativity and provides examples of known breeding results; it does not currently test concrete owned-pair availability.
- The embedded Pal and breeding databases begin loading during app startup in [`PalCalc.UI/App.xaml.cs`](../PalCalc.UI/App.xaml.cs). Resolve `var palDb = PalDB.LoadEmbedded()` once and pass that same instance to `PalBreedingDB.LoadEmbedded(palDb)`. `PalBreedingDB` has one process-wide static cache and is not keyed by `PalDB`, so do not mix arbitrary `PalDB` instances with it.

## Suggested UI design

### Tab name

Suggested label: `Pal Catalog` or `Breeding`. `Pal Catalog` better describes the complete list; the selected detail panel can be titled `Breeding`.

### Layout

Use a two-pane layout inside the tab:

1. **Left/main pane:** searchable, virtualized wrap/grid list of all database Pals.
2. **Right/detail pane:** details for the selected Pal.

At the inspector's current `MinWidth="480"`, two fixed panes will be cramped. Either raise the minimum width for this feature or switch to a responsive layout that stacks/collapses the detail pane at narrow widths.

Each grid item should contain:

- icon;
- localized name;
- Pal number, including variant notation (from `Pal.Id.ToString()`);
- a compact availability marker such as `Ready`, `Missing`, or `No breeding data`.

The hover tooltip can show the same data plus:

- number and name;
- owned count in the current save;
- number of distinct usable instance pairs;
- possible parent count or a short parent summary.

The click detail view should show:

- selected Pal icon, name, and number;
- owned count and owned instances, including gender and location where useful;
- `Can breed now` status;
- every database parent recipe that can produce the selected Pal;
- a clear availability annotation on each recipe: both parents owned as a valid pair, only one suitable parent owned, or missing;
- the actual owned parent instances for matching recipes, including gender and location (cap, group, expand, or page this list for large saves).

In other words, selecting a Pal should answer both “Which parent combinations breed this Pal?” and “Which of those combinations can I make with Pals in this save?” Showing only recipes involving owned parents would hide useful ways to obtain the Pal, so the detail view should show all recipes and highlight the matches.

Avoid displaying every raw field by default. The existing raw save inspector can remain the place for low-level character data.

### Filtering and sorting

Recommended first-pass controls:

- text search by localized name, model `Name` (currently English), internal name, or Pal number;
- `All`, `Owned`, and `Breedable now` filters;
- sort by Pal number (`Pal.Id`) by default, with localized name as a secondary option.

Do not make the user open each Pal to discover whether it is breedable. The grid marker should summarize that state.

## Suggested data flow

Create a dedicated inspector feature rather than putting breeding calculations in the XAML or in `SaveDetailsViewModel`.

Suggested shape:

- `PalCatalogView.xaml`: grid, search/filter controls, and selected-detail content;
- `PalCatalogViewModel`: owns the catalog collection and selected item;
- `PalCatalogEntryViewModel`: one database Pal plus owned count, readiness state, and icon;
- `PalBreedingDetailsViewModel`: selected Pal's recipes, owned instances, and valid pairs.

The constructor should receive the existing `CachedSaveGame`, `PalDB`, and matching `PalBreedingDB` (or a small calculator service containing those dependencies). It can precompute normalized lookups:

```text
ownedById: InstanceId -> PalInstance
ownedByPalAndGender: (Pal, MALE|FEMALE) -> List<PalInstance>
recipesByChild: Pal -> IReadOnlyList<BreedingResult>
```

For single-player, build these lookups from all parsed owned-Pal locations. For server saves, use the selected player's guild and shared bases when that mapping is easy and reliable. Use non-empty unique IDs and concrete `MALE`/`FEMALE` genders for pairing. Keep a separate deduplicated owned-count lookup so malformed-gender Pals can still contribute to `Owned` counts. Do not remove expedition Pals from the initial lookup; retain their location/status metadata for display.

For each child Pal:

1. read canonical recipes from a lookup built once with `breedingDb.Breeding.GroupBy(r => r.Child)` (or deduplicate the symmetric `BreedingByChild` representation);
2. find candidate owned instances by parent Pal type and concrete gender;
3. require two distinct IDs, opposite actual genders, and a matching recipe;
4. canonicalize each instance pair by ID before deduplicating it;
5. expose a compact summary to the grid and build/page full detail results when selected.

Indexing by Pal and gender is simple and prevents scanning all owned instances for every child. The Cartesian product for a popular same-species recipe can still be large, so readiness should short-circuit after the first valid pair and exact pair lists should be created lazily and capped/paged.

Important correctness details:

- Do not count the same `PalInstance` as both parents; the pair must use two non-empty, distinct `InstanceId` values.
- Treat parent order as interchangeable. `BreedingResult.Matches(...)` already handles both orders.
- Treat concrete owned genders separately from recipe/solver pseudo-genders.
  > [!IMPORTANT]
  > For owned instances, accept only `MALE` and `FEMALE`, and require one of each. Merely checking `instance.Gender != PalGender.NONE` is not sufficient because it would admit `WILDCARD` and `OPPOSITE_WILDCARD`. Then apply the selected recipe's explicit gender constraints through `Matches(...)`.
- Deduplicate source instances before calculating counts. Deduplicate display pairs by canonicalized instance-ID pair and child; include the recipe identity only when two gender-specific recipes for the same parent types must remain distinguishable.
- `BreedingByChild` deliberately indexes each breeding result from both parent sides. Do not render its nested entries directly without canonicalization, or ordinary A+B recipes will appear twice.
- A valid recipe means the pair can produce the child; it does not mean the child will inherit any requested passive skills or IVs. Those should be separate future indicators.
- A Pal can be owned but not currently breedable, and a Pal can be breedable without being owned. These are different states.

## Localization and accessibility

The UI is localized through [`PalCalc.UI/Localization`](../PalCalc.UI/Localization). New labels, state text, filters, and tooltip text should be added as localization entries in [`LocalizationCodes.resx`](../PalCalc.UI/Localization/LocalizationCodes.resx) and [`en.resx`](../PalCalc.UI/Localization/Localizations/en.resx), and [`LocalizationCodes.Designer.cs`](../PalCalc.UI/Localization/LocalizationCodes.Designer.cs) regenerated/updated using the custom T4 workflow described in the [localization README](../PalCalc.UI/Localization/README.md).

Do not rely on green/red alone. Provisional wording is `Matching pair owned`, `No matching pair`, `No breeding data`, and `Unknown save data`. This wording is more accurate than `Ready` when all locations, including expeditions, count as owned matches. Each state should also have an icon and a tooltip or accessible automation name. Exact AdonisUI theme brushes can be chosen during implementation after checking contrast in light and dark themes.

The grid should remain usable with keyboard navigation. Selecting an item should update the detail pane without requiring hover.

If the app supports changing locale without restart, refresh filtering/sorting when localized names change; a collection sorted once by `Name.Value` will otherwise retain the old language's order.

## Testing plan

### Model-level tests

Status: **Implemented and passing.** The checklist remains useful when extending the calculator; add a focused regression whenever validation exposes a new malformed-save or recipe edge case.

Add tests around a small synthetic set of `PalInstance` objects or a test helper that verifies:

- male/female pairs match the expected recipe;
- reversed parent order still matches;
- ordinary wildcard/wildcard recipes reject same-gender concrete pairs;
- ordinary wildcard/wildcard recipes accept male/female pairs in either order;
- explicit gender-specific recipes select the correct result in either parent order;
- instances with `NONE`, `WILDCARD`, or `OPPOSITE_WILDCARD` are rejected as concrete owned parents;
- same-instance reuse is rejected;
- duplicate source records with one `InstanceId` do not inflate counts or create pairs;
- a child with multiple recipes is ready if any recipe has a valid owned pair;
- owned child without a valid parent pair is not marked breedable;
- missing or unknown Pal data produces a safe neutral state.

### UI/view-model tests

Status: **Still to add/run on Windows x64.** The UI project and test assembly compile, but this development host cannot execute the forced x64 Windows test host.

Extend the existing UI test project where practical to verify:

- every `PalDB.Pals` entry appears exactly once, including variants;
- default list sorting follows `PalId` (Paldex number and variant order);
- owned counts are correct;
- readiness states are correct for the supplied cached-save snapshot and selected scope;
- selecting an entry exposes the right child recipes and matching pairs;
- search/filter behavior does not change the underlying readiness calculation.

### Manual visual checks

Status: **Pending on Windows.** This is a release gate.

- grid layout at the inspector's minimum and normal window sizes;
- icon fallback behavior;
- long localized names and variant names;
- a save with no Pals, a save with one Pal, and a save with many Pals;
- a server save with multiple players/guilds and duplicate instance references;
- keyboard selection and tooltip/accessible state;
- dark/light AdonisUI theme contrast for positive, negative, and neutral states.

## Performance and lifecycle notes

The inspector currently receives a snapshot-like `CachedSaveGame` when it opens. The new tab can follow the existing Search/Details pattern and use that snapshot. If save reload events need to update an already-open inspector, that should be an explicit follow-up; do not assume the current inspector automatically refreshes.

Selection, search text, filters, and sorting must survive tab changes. They should also survive closing and reopening the inspector for the same save while the app remains running. Keep this as in-memory, per-save UI state keyed by the save identity; persistence across app restarts is not required. Avoid disk writes for this state unless that requirement is added later.

The catalog may contain hundreds of entries, so prefer a virtualizing items panel (`VirtualizingWrapPanel`) and verify that container recycling/scrolling works in the final XAML. Avoid creating a new `PalViewModel` or loading icons repeatedly for every refresh; existing `PalViewModel.Make` and `PalIcon.Images` cache these resources. Do not use `PalViewModel.All` directly because it is name-sorted and does not represent the active catalog filter.

`InspectSave()` constructs `SaveInspectorWindowViewModel` inside `LoadingSaveFileModal.ShowDialogDuring(...)`, which runs the constructor on a worker thread. Keep pure catalog calculations there, but do not create unfrozen WPF dispatcher-bound objects or mutate bound collections from that worker after the window opens. In particular, confirm that `PalIcon.Images`/`BitmapImage` is initialized safely (initialize on the UI thread or freeze images before cross-thread use).

If breeding calculations become expensive, calculate compact readiness once when constructing the tab view model and expose immutable/read-only results to XAML. Recompute only when the cached save snapshot changes; construct detailed pair lists lazily.

Current implementation note: displayed pairs are capped at 100 per recipe and recipe view models are created for the selected Pal, but exact pair counts and capped pair samples are still calculated eagerly for the whole catalog. Profile this before expanding pair-related features.

## Scope recommendation

### MVP

- third inspector tab;
- all database Pals in an icon grid;
- search and basic filters;
- hover summary;
- click detail pane;
- owned count;
- valid-pair readiness using existing gender-aware breeding recipes;
- all parsed locations for single-player, including expedition Pals, with location/status shown;
- selected-player guild and shared-base scope for server saves when it can be resolved reliably;
- in-memory per-save selection/filter/sort state that survives tab changes and inspector reopenings;
- localization and non-color status text;
- model tests for matching logic.

### Defer

- passive-skill inheritance optimization;
- IV inheritance probabilities in the catalog status;
- multi-step breeding chains from currently owned Pals;
- “which exact pair should I capture next?” recommendations;
- editing or moving Pals from this tab;
- live updates while the save is being changed elsewhere;
- a graph visualization of the entire breeding network.

## Confirmed product decisions

1. Show every database Pal when possible, including variants and special/non-catchable entries.
2. Optimize first for single-player. For server saves, use the selected player's guild and shared bases when the relationship can be resolved without a large new ownership system.
3. Include Pals from every parsed location when possible, including expeditions, and show location/status information for matched instances.
4. Show every breeding recipe for the selected child and visually distinguish recipes for which the save has both, one, or neither suitable parent.
5. Keep selection, filter, search, and sort state during tab changes and inspector reopenings for the same save while the app is running. Resetting on app restart is acceptable.
6. Use explicit non-color text for all statuses. Provisional labels are `Matching pair owned`, `No matching pair`, `No breeding data`, and `Unknown save data`; exact localized wording and accessible theme brushes can be refined during implementation.
7. Show one row per species-level breeding recipe and collapse concrete owned instance pairs into an expandable list by default. This prevents large saves from producing an overwhelming initial list.
8. If a server save cannot resolve the selected player's guild reliably, fall back to that player's directly owned Pals rather than every Pal in the save, and disclose the active scope in the UI.
9. An expedition Pal still counts toward a matching owned pair, but the match should include a visible `On expedition` location warning rather than being marked missing.

`PalGender.NONE` is not an open product decision for readiness: it cannot prove that an instance completes a usable male/female pair, so it must not count as a ready parent. It may still count toward the general owned total and can be surfaced as malformed/unknown instance data.

## Completed implementation sequence

1. Pure calculator, save scope, deduplicated counts, canonical pairs, malformed-data handling, and focused model tests.
2. Catalog entry/detail/recipe/pair view models and Save Inspector wiring.
3. Virtualized XAML catalog and detail pane.
4. Search, filters, sorting, tooltips, state restoration, localization, accessibility text, and expedition/location display.
5. Model regression tests, Windows-targeted UI compilation, and static localization validation.

Windows execution and the manual visual checks remain in the release gates above.

## Architectural and UX suggestions for future phases

To enhance user experience and seamlessly integrate this feature into PalCalc's ecosystem, consider the following suggestions for post-MVP iterations:

### 1. UX & Visual Enhancements

- **More specific missing-parent explanations**:
  - Gender totals are already shown. Add concise explanations such as `Missing female Foxparks` or `Need a second opposite-gender Lamball` so users do not have to infer the missing requirement from recipe rows.
- **High-Quality Parent Indicators**:
  - Add a visual badge/icon on grid cards when owned matching parents satisfy user-selected passive/IV goals. Avoid hard-coding a “best traits” list because desirable traits vary by target and game version.
- **Location-Aware Availability Filter**:
  - Provide a toggle filter (`Exclude Expeditions / Viewing Cages`) so users can restrict valid breeding pairs to Pals currently in the Palbox or active Bases.

### 2. Workflow & Solver Integration

- **"Send to Solver / Set as Target" Action**:
  - Include a button in the Detail Pane ("Breed in Solver") that pre-fills the selected Pal directly into PalCalc's main Solver tab as a target, bridging save inspection with solver pathing.
- **Expose "One Parent Away" in the catalog**:
  - Recipe rows already distinguish `OneParentOwned`. Add an optional catalog badge/filter that summarizes when at least one recipe needs only one suitable parent.
- **CSV & Checklist Export Integration**:
  - Add a catalog-specific exporter using the generic CSV infrastructure. [`PalCalc.UI/Model/CSV/PalCsvExporter.cs`](../PalCalc.UI/Model/CSV/PalCsvExporter.cs) is only a structural reference: it exports owned `PalInstanceViewModel` rows and cannot directly export one row per catalog Pal/readiness state.

### 3. Performance & Memory Optimization

- **Deferred Recipe Detail Construction**:
  - Calculate high-level grid readiness summaries (`Ready` / `Missing`) upon tab initialization, but defer full parent instance pairing lists and detailed recipe view models until the user clicks on a specific Pal entry.

### 4. Maintainability

- **Move scope resolution behind a tested model/service boundary**:
  - The breeding calculation is UI-free, but server player/guild scoping currently lives in `PalBreedingCatalogViewModel`. Extract it if server-save behavior grows or needs more fixtures.
- **Separate compact readiness from pair enumeration**:
  - The UI creates detailed recipe view models only for the selected Pal, but the calculator still computes exact pair counts and capped pair samples for the whole catalog. Split these paths if profiling shows meaningful startup cost.

## Pal Catalog as the Pal information hub

The Pal Catalog/Breeding UI is the single entry point for Pal-focused workflows on the Solver page. Keep raw save inspection in `Inspect`; the Solver page owns the catalog and its selected-Pal details.

### Proposed navigation

Use the Solver page with these tabs:

1. **Catalog**
   - Complete PalDex-ordered catalog, variants, search, filters, sorting, icons, ownership counts, and readiness markers.
2. **Selected-Pal details**
   - Breeding details and Work Suitability use the same selected Pal and detail pane.
   - Show all recipes, owned-parent availability, exact matching pairs, locations, expedition warnings, and the future `Send to Solver` action.
   - Show Work Suitability levels for the selected Pal; a filterable comparison grid remains a follow-up.
   - Support filtering by work type and minimum level, such as `Mining >= 3` or `Transporting >= 4`.
   - Show the work-type name and numeric level as text; icons and color may supplement but must not be the only signal.
4. **Owned / locations**
   - Optional follow-up tab for save-owned instances, locations, gender, levels, expedition state, and filters. This can reuse the existing Search/Details view models where practical rather than duplicating raw save inspection.

The selected Pal, search text, filter, sort, and save scope are shared by the catalog grid and the selected-Pal detail pane. Selecting a Pal in the grid updates both breeding and Work Suitability sections. The hub operates against the selected save for ownership and breeding state, while static Pal data remains available even when no save is loaded.

### Existing work-suitability data

The model already contains static work-suitability data on [`Pal.WorkSuitability`](../PalCalc.Model/Pal.cs), represented as `Dictionary<WorkType, int>`. The current `WorkType` enum includes:

- Kindling
- Watering
- Planting
- Generate Electricity
- Handiwork
- Gathering
- Lumbering
- Mining
- Medicine Production
- Cooling
- Transporting
- Farming

The embedded `PalCalc.Model/db.json` contains these values under each Pal's `WorkSuitability` object. This is enough for the selected-Pal detail section showing database capability and level. Some entries may have a null or incomplete dictionary, so the UI should display `Unknown`/`No data` rather than treating missing values as level zero without disclosure.

Static suitability is not the same as current save availability. The first version should distinguish:

- **Capability:** what the database says the Pal can do and at what level.
- **Owned availability:** whether the selected save contains an instance of that Pal.
- **Current assignment/state:** whether an owned instance is currently working, idle, boxed, or otherwise available; add this only when the save model exposes it reliably.

### Recommended implementation order

1. Preserve the catalog calculator and shared selected-Pal view models.
2. Keep breeding and Work Suitability in one detail pane with shared selected-Pal state.
3. Add work-type/minimum-level filters and a compact comparison view; defer complex ranking until the data and UX are validated.
4. Add save-owned counts and locations to the Work suitability view, clearly labeling them as save-specific.

### Acceptance criteria for the hub

- Users can reach Catalog, breeding details, and Work Suitability from one Pal-focused Solver entry point.
- A Pal selected in the catalog remains selected in both detail sections.
- Breeding availability still uses the selected save and its resolved scope.
- Work suitability renders static database levels without requiring a save.
- Missing work-suitability data is explicit and does not crash XAML binding.
- Existing Search and Details inspection workflows remain available from Inspect.

## MVP Implementation Notes

The MVP feature has been implemented as specified in this document:

1. **Pure Domain Engine (`PalBreedingCatalogCalculator.cs`)**:
   - Resides in `PalCalc.Model` without WPF dependencies.
   - Deduplicates owned `PalInstance` entries by non-empty `InstanceId`.
   - Computes owned gender breakdowns (Total, Male ♂, Female ♀, Other).
   - Validates concrete opposite-gender pairs (`MALE` + `FEMALE`) with distinct `InstanceId`s.
   - Categorizes recipe availability into `BothParentsOwned`, `IncompatibleParentsOwned`, `OneParentOwned`, `NeitherParentOwned`, and `Unknown`.
   - Distinguishes species-level readiness (`Ready`, `MissingPair`, `Unavailable`, `Unknown`).

2. **Same-Gender Feedback ("Gender Switch" / Alternative Gender Note)**:
   - Recipes for which both suitable parents are owned but do not form a valid concrete pair explicitly display the status `Parents owned, but no compatible pair`.
   - Tooltips and detail headers display gender breakdowns `Owned: N (M ♂, F ♀)` so users can immediately spot missing genders.

3. **UI Integration**:
   - New Solver tab `Pal Catalog` added to `SolverPage.xaml` and `SolverPageViewModel.cs`.
   - High-performance virtualized grid view using `VirtualizingWrapPanel` in `PalBreedingCatalogView.xaml`.
   - Work Suitability is rendered in the same selected-Pal detail pane and follows catalog selection.
   - Comprehensive filtering (`All`, `Owned`, `Breedable now`), sorting (`PalDex #`, `Name`), search bar, tooltips, and detail pane.

4. **Localization & Testing**:
   - Localization codes and English/Turkish translations added to `LocalizationCodes.resx`, `LocalizationCodes.Designer.cs`, `en.resx`, and `tr.resx`; other locales use the existing English fallback until translated.
   - 25/25 model tests passed in the previous verification run, and `PalCalc.UI.Tests/PalCatalogViewModelTests.cs` covers Work Suitability selection wiring.
