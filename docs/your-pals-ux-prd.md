# Product Requirements Document: My Pals UX Rework

**Status:** Draft
**Date:** 2026-08-26
**Product area:** Pal Calc — Your Pals tab
**Primary audience:** Pal Calc users who organize owned Pals for breeding, teams, and tracking
**Related architecture:** [Your Pals tab roadmap](your-pals-tab-roadmap.md), [Your Pals persistence contract](your-pals-persistence-contract.md)

## 1. Executive summary

The current Your Pals tab exposes too much implementation detail in the main
workflow. It combines collection management, filtering, source inspection,
document recovery, orphan management, solver configuration, and persistence
commands in one dense screen.

The result is a technically capable but difficult-to-understand interface. A
user who wants to add a Pal must infer a multi-selection workflow. A user who
sees a stale or unresolved Pal is shown a status name, but not a clear
explanation or next action. Advanced recovery tools compete with the everyday
collection workflow.

This PRD proposes a user-centered reorganization around four common jobs:

1. Choose a group.
2. View and search the Pals in that group.
3. Add, edit, or remove a Pal.
4. Resolve problems when a saved reference no longer matches the current save.

The redesign preserves the existing save-scoped persistence, stable identity,
recovery, and solver rules. It changes the presentation and interaction model,
not the data-safety contract.

## 2. Problem statement

### User problem

Users cannot quickly answer these questions when they open the tab:

- Which save am I managing?
- What does this tab do for me?
- Which group is active?
- How do I add a Pal?
- Which Pals are ready to use?
- Which Pals need attention, and what should I do next?
- What will happen if I enable solver integration?
- Are my changes saved?

The current interface makes users answer these questions by reading control
labels, understanding internal data relationships, and discovering that two
separate tables must be selected before certain commands become available.

### Product problem

The current UI is shaped by the implementation milestones that built the
feature. Persistence diagnostics and source resolution are valuable system
capabilities, but they have become first-class controls in the everyday
collection surface.

The design needs an explicit separation between:

- **Collection management:** what users do most of the time.
- **Attention and repair:** what users do when a source reference changes.
- **Technical administration:** recovery, orphaned documents, raw source data,
  duplicate cleanup, and document lifecycle operations.

## 3. Current-state diagnosis

This diagnosis is based on the current implementation in
[YourPalsView.xaml](../PalCalc.UI/View/YourPalsView.xaml) and
[YourPalsViewModel.cs](../PalCalc.UI/ViewModel/YourPalsViewModel.cs).

### 3.1 Information architecture is flat

The tab presents all of these layers at the same level:

- Save scope and session/source state.
- Solver source configuration.
- Refresh and discard/reload actions.
- Search, status filter, group filter, sort field, sort direction, and clear.
- Group creation, renaming, ordering, and deletion.
- Manual definition creation and editing.
- Source import, rebinding, bulk resolution, duplicate removal, and member removal.
- Document creation, recovery repair, and save.
- Recovery diagnostics.
- Orphaned documents.
- Saved group members.
- Current source snapshot.

The screen has no strong primary task. The presence of a button does not tell
the user whether it is part of the normal workflow or a recovery operation.

### 3.2 The add flow is hidden and selection-dependent

The current add operation requires the user to:

1. Select a group in the left list.
2. Select a source Pal in the lower source snapshot.
3. Click `Add source` in the upper action row.

This dependency is not explained in the UI. The user must understand that the
group selection and source-row selection are separate state variables. The
same applies to rebind operations.

The target interaction should instead be a single `+ Add Pal` flow that makes
the destination group and source choice explicit.

### 3.3 Technical vocabulary leaks into the main experience

Examples include:

- `Save scope`
- `Session`
- `Source`
- `Stale`
- `Conflict`
- `Invalid`
- `Rebind`
- `Resolve matching`
- `Entry key`
- `Instance`
- `Source snapshot`
- `Manual Pal internal name`

These concepts may remain available, but they need a user-facing translation
and should not dominate the default view.

### 3.4 The main collection is a raw data table

The current member table emphasizes group, Pal, status, source, instance, key,
and details. This makes it useful for debugging and diagnostics, but not for
recognizing a collection of Pals.

Pal artwork already exists in the application. The primary row should use the
Pal image, name, nickname, location, and useful gameplay details. Stable keys
and source identities belong in a details panel.

### 3.5 Statuses do not provide a decision

The resolver correctly preserves important states such as stale, unresolved,
conflict, and invalid. The problem is the presentation:

- The enum name is exposed directly.
- The reason may be buried in a wide `Details` column.
- There is no contextual repair action next to the affected row.
- Group summaries display raw status counts such as `Resolved: 6, Stale: 2`.

The UI should turn each status into a clear explanation and next action.

### 3.6 Technical and recovery states consume normal-workflow space

Recovery details, orphaned documents, and the current source snapshot appear in
the same screen as ordinary collection management. The source snapshot is
expanded by default, even though most users do not need to inspect it while
organizing their Pals.

These surfaces should be available under Advanced or dedicated recovery
navigation and should be surfaced inline only when attention is required.

### 3.7 Save state is too subtle

The current edit flow requires explicit Save, which is consistent with the
existing persistence contract. However, the user receives messages such as
`Group created. Save to persist changes.` rather than a persistent, obvious
dirty indicator.

The redesign should keep explicit saving for this phase, but make the state
visible in the header and close/navigation guardrails.

### 3.8 Responsive behavior is structurally unsafe

The application window has a minimum width of 480px, while the main Your Pals
content declares minimum columns of 180px and 360px plus a splitter. The core
layout therefore requires more horizontal space than the window promises.

The multiple `WrapPanel` action rows can also reflow into unpredictable groups
of controls. The redesign must use deliberate responsive breakpoints rather
than relying on incidental wrapping.

### 3.9 Empty and filtered states are conflated

The empty message is controlled by the number of filtered entries. Therefore a
group with Pals can display `No saved group members` when a search or filter
simply returns no matches.

The interface needs separate states:

- No groups exist.
- The selected group has no members.
- The query has no matches.
- The source has no available Pals.
- The document is unavailable or read-only.

### 3.10 Localization is incomplete in the view-model layer

Several filter and sort labels are hard-coded in English in the view model,
including `All statuses`, `Resolved`, `Group`, and `Ascending`. Status strings
and group summaries also use enum names directly.

All visible user-facing text introduced by this redesign must use the existing
localization system, including status labels, explanations, empty states,
action labels, and confirmation text.

## 4. Why we are doing this

This work is needed because the current design makes the feature's strongest
capabilities hard to discover and makes simple actions feel risky.

The redesign should:

- Reduce the number of concepts users must understand before taking action.
- Make the add/remove/group workflow discoverable without documentation.
- Preserve visibility of missing or conflicting user data without making the
  whole UI feel like a repair console.
- Make the connection between Your Pals and the solver understandable.
- Make save ownership and dirty state obvious.
- Create a scalable structure for future features such as favorites, team
  presets, and explicit cross-save import.

This is not cosmetic cleanup. It is a correction to the product model exposed
by the screen.

## 5. Goals

### Primary goals

1. A first-time user can understand the purpose of the tab within five seconds.
2. A user can add a Pal to a selected group without knowing source snapshot
   implementation details.
3. A user can identify which entries are ready, missing, or need repair.
4. A user can resolve a common stale-reference problem from the affected entry.
5. A user can see which save owns the current collection.
6. A user can see whether changes are saved, dirty, or blocked from saving.
7. Advanced diagnostics remain available without overwhelming the default view.
8. The redesigned UI works at the supported minimum window size.

### Secondary goals

- Make groups useful as recognizable collections rather than only filters.
- Make the solver-source setting understandable and reversible.
- Use Pal artwork and gameplay information to improve scanability.
- Preserve stable selection and query behavior during refresh.
- Keep the design compatible with the current persistence and recovery model.

## 6. Non-goals

This PRD does not include:

- Changing breeding calculations or solver rules.
- Changing the save-scoped persistence contract.
- Automatic cross-save synchronization.
- Importing Inspect custom-container data into Your Pals.
- Automatic deletion of stale, unresolved, or conflicting members.
- Persistent global groups that are not owned by a save.
- A full mobile or web redesign.
- Replacing explicit save with autosave in this milestone.
- Building smart collections, complex rule builders, or drag-and-drop
  organization before the basic workflow is clear.

## 7. Target users and jobs to be done

### Breeding-focused player

> When I am preparing a breeding plan, I want to collect useful owned Pals in
> groups so I can find them quickly and use them as solver inputs.

Needs:

- Fast search and grouping.
- Clear readiness/status.
- Visible level, gender, location, and nickname.
- Clear solver integration.

### Collection manager

> When I am organizing my save, I want to create and maintain groups without
> dealing with file or identity details.

Needs:

- Simple group creation and rename.
- Easy add/remove actions.
- Counts and attention indicators.
- Predictable save behavior.

### Recovery user

> When a saved Pal can no longer be found, I want to know what happened and
> choose whether to repair, replace, keep, or remove it.

Needs:

- A clear explanation of the problem.
- A contextual repair action.
- The ability to preserve the unresolved record.
- Access to diagnostics without losing normal collection context.

## 8. Product principles

### Organize first, diagnose second

The default screen should help users manage Pals. Diagnostics should be
revealed by attention state or an Advanced section.

### Every status should answer “what now?”

A status is incomplete until the user can understand the reason and see the
next safe action.

### Do not hide user data because it cannot resolve

Stale, unresolved, conflicting, and invalid entries remain visible. The UI
should make them understandable rather than silently filtering them away.

### Make selection context explicit

Actions should show their target, for example `Add to Breeding Team` or
`Fix Anubis`, instead of relying on two hidden list selections.

### Technical identity is detail, not primary content

Stable keys and source identities remain available for support and recovery,
but they should not be the first information users scan.

### Preserve the safe persistence boundary

The UX may simplify the workflow, but it must not weaken save ownership,
atomic writes, recovery, or conflict handling.

## 9. Proposed experience

### 9.1 Naming and context

The user-facing name for both the tab and the page heading is **My Pals**. The
underlying code, persistence contract, and architecture may continue to use
`Your Pals` until a separate code-level rename is planned. The page must always
include the active save name and a short explanation:

```text
My Pals
Organize owned Pals for breeding and tracking
Save: Player 315 · Palpagos Islands
```

The canonical data remains save-scoped. The friendly heading must not imply
that the collection is global across saves.

### 9.2 Header and summary

The header contains:

- Page title.
- One-line purpose statement.
- Active save display name.
- Collection summary.
- Save state.
- Primary Save action when dirty.
- A `More` menu for advanced actions.

Example:

```text
My Pals                                      [Save] [More ▾]
Organize owned Pals for breeding and tracking
Player 315 · Palpagos Islands
24 pals · 4 groups · 3 need attention       Changes not saved
```

Save state presentation:

- `Saved` — quiet success indicator.
- `Changes not saved` — visible warning with Save enabled.
- `Saving…` — temporary progress state.
- `Save failed` — persistent error with retry and diagnostic access.
- `Read-only recovery` — clear banner explaining why editing is blocked.

### 9.3 Solver integration callout

Replace the unexplained checkbox with a compact setting card:

```text
Use My Pals in breeding calculations
Ready Pals in this collection can be used as solver inputs.
[ ] Include My Pals in solver
```

When enabled:

```text
✓ My Pals are included in the solver
12 ready Pals available
```

Only resolved, solver-eligible entries are included. The callout should link
to a short explanation of why some entries are excluded.

### 9.4 Group navigation

The left navigation is a collection list, not a command panel.

Each row contains:

- Group name.
- Total member count.
- Attention count, when nonzero.
- Selected state.
- Context menu for rename, reorder, and delete.

Recommended default entries:

- `All Pals` — a query view over all saved group members, not a persisted
  group unless the product later chooses to add one.
- User-created groups.

The bottom of the group list contains a single `+ New group` action.

Example:

```text
Groups

All Pals                         24
Breeding Team                     8   2 need attention
Base Workers                     10
Favorites                         6

+ New group
```

Group management should use a small dialog or context menu. Persistent text
boxes for create and rename should not occupy the main layout.

### 9.5 Collection toolbar

The selected group view contains:

- Group name and member count.
- `+ Add Pal` as the primary action.
- Search field with visible placeholder.
- Status filter.
- Sort control.
- Optional clear button shown only when a query is active.

Example:

```text
Breeding Team · 8 pals                         [+ Add Pal]
Search this group…       [All statuses ▾] [Sort: Name ▾]
```

The default view should not show separate group filter and sort-direction
buttons when a group is already selected. A combined sort menu can offer
ascending/descending options.

### 9.6 Pal row design

Each row should prioritize recognition and action:

```text
┌──────────────────────────────────────────────────────────┐
│ [Pal image]  Anubis                                       │
│              Nickname · Male · Level 42 · Base             │
│              ✓ Ready                         [•••]         │
└──────────────────────────────────────────────────────────┘
```

For a problem state:

```text
│ [Pal image]  Anubis                                       │
│              This Pal is no longer in the selected save   │
│              ⚠ Needs attention                 [Fix]      │
```

The row may retain a compact table layout for large collections, but the
default columns should be:

- Pal image and name.
- Nickname, gender, and level.
- Location or source label.
- Human-readable status.
- Contextual action.

Move the following into a details panel:

- `PalEntryKey`.
- `InstanceId`.
- Stable source identity.
- Source key.
- Content fingerprint.
- Raw recovery diagnostics.

### 9.7 Add Pal flow

Clicking `+ Add Pal` opens a picker for the selected group.

The picker has two clear paths:

#### Add from current save

- Search by Pal name or nickname.
- Show image, level, gender, location, and source scope.
- Indicate whether the Pal is already in the selected group.
- Primary action is `Add to [group name]`.

The first version adds one Pal at a time. Multi-select is intentionally deferred
until the single-entry flow is validated and there is evidence that batch
collection setup is a frequent task.

#### Add manual Pal

Use a catalog-backed Pal selector and the currently supported manual definition
fields rather than exposing only the internal name field. The form should
explain that a manual Pal is a user-authored definition, not a live save
instance. Raw internal values remain implementation and recovery details; they
must not be the primary input users see in the toolbar.

### 9.8 Selected Pal details

Selecting a row opens an overlay details panel. The overlay keeps the main
collection layout stable and avoids requiring a permanently reserved details
column. It should be dismissible without losing the selected row.

The panel shows:

- Pal image, name, nickname, and key gameplay fields.
- Group membership.
- Source and location in friendly language.
- Resolution status and explanation.
- Solver eligibility.
- Actions appropriate to the entry.

Normal actions:

- Remove from group.
- Edit manual Pal.
- Move to another group, if supported by the existing session model.

Problem actions:

- Find replacement.
- Rebind to selected source Pal.
- Keep unresolved.
- Remove from group.

### 9.9 Status and repair design

Use a localized status label, explanation, and action:

| Internal status | User-facing label | Explanation | Primary action |
| --- | --- | --- | --- |
| Resolved | Ready | This Pal is available from the current save. | None |
| Unresolved | Cannot identify | The Pal or one of its values is not recognized by the current catalog. | Review |
| Stale | No longer in save | The saved reference no longer matches a Pal in this save. | Find replacement |
| Conflict | Conflicting copies | More than one source record matches this reference. | Choose copy |
| Invalid | Needs repair | The saved entry is missing required information. | Repair |

The group sidebar should show only the count, for example `2 need attention`.
The details panel and attention view provide the full explanation.

The `Review attention` view should be a filtered collection view, not a
separate data model. It must preserve the underlying member records.

### 9.10 Empty, loading, and unavailable states

The UI must distinguish these states:

#### No save selected

```text
Select a save to manage your Pals
Your Pals collections belong to a specific save.
[Back to save selection]
```

#### No groups

```text
Create your first group
Use groups for breeding teams, workers, favorites, or any collection you want.
[Create group]
```

#### Empty selected group

```text
This group has no Pals yet
[+ Add Pal]
```

#### No query matches

```text
No Pals match your search or filters
[Clear filters]
```

#### No source entries

```text
No source Pals are currently available
Refresh the save source or inspect the save loading status.
[Refresh source]
```

#### Recovery/read-only state

Show a prominent but concise banner with:

- Why the document is read-only.
- Whether the original or backup is preserved.
- What the user can do next.
- A `View recovery details` action.

### 9.11 Advanced and recovery surfaces

The `More` menu may contain:

- Refresh source.
- Discard changes and reload.
- Resolve matching entries.
- Remove duplicates.
- Create document.
- Repair recovered document.
- View raw source snapshot.
- Manage orphaned documents.

Rules:

- Hide unavailable commands rather than presenting a long disabled command
  row, unless the disabled state itself explains a required prerequisite.
- Show destructive actions in a confirmation dialog with the exact scope.
- Keep orphaned document management in a dedicated page or clearly separate
  recovery section.
- Keep the raw source snapshot collapsed by default.

## 10. Functional requirements

### FR-1: Save context

The page must display the active save in a human-readable form. The underlying
session must continue to enforce the canonical save identity and must never
display one save's document under another save.

### FR-2: Collection summary

The header must show total group count, total member count, and the number of
members requiring attention. Counts must update after add, remove, refresh,
repair, and save reload.

### FR-3: Group navigation

Users must be able to create, select, rename, reorder, and delete groups. The
selected group must be visually obvious. Group actions must be discoverable
from the selected group and must not require permanent toolbar text boxes.

### FR-4: Add Pal

Users must be able to add a resolved source Pal to the selected group through a
single add flow. The destination group must be visible in the picker and in
the confirmation action.

### FR-5: Manual Pal

Users must be able to create and edit manual definitions through a labeled
form. Raw internal values must be preserved according to the persistence
contract, and unknown values must remain visible as unresolved rather than
being silently replaced.

### FR-6: Search and filter

Users must be able to search the selected collection by friendly fields and
filter by user-facing status. The UI must distinguish zero matches from an
empty collection. Query state remains transient and save-scoped as currently
defined by the roadmap.

### FR-7: Status explanations

Every non-ready entry must expose a localized reason and at least one safe
next action where one exists. Status labels must not display raw enum names in
the primary collection surface.

### FR-8: Repair actions

Users must be able to review and repair common stale, unresolved, conflicting,
and invalid entries without deleting them implicitly. Existing repair and
rebind operations must remain subject to the current session and persistence
rules.

### FR-9: Source inspection

Users must be able to inspect the current source snapshot, but it must be
collapsed by default and treated as an advanced diagnostic surface.

### FR-10: Solver source

Users must be able to enable or disable the use of ready Your Pals entries as
solver inputs. The UI must explain the setting and display the count of ready
entries available to the solver.

### FR-11: Save state

When edits are pending, the page must show a persistent dirty state. Save must
remain explicit in this milestone. Navigation or save switching with dirty
data must provide a clear guardrail consistent with the existing session
behavior.

### FR-12: Recovery safety

The redesign must not:

- Delete stale or unresolved entries during refresh.
- Replace a damaged document with an empty document.
- Save a read-only recovery projection without explicit repair.
- Mix data between save identities.
- Create a second writer or fallback store for Your Pals.

### FR-13: Responsive layout

At the supported minimum size, the page must remain usable. The layout must
switch to a stacked layout or details dialog when the group sidebar and
collection content cannot coexist horizontally. Action rows must not rely on
unbounded wrapping.

### FR-14: Localization

All visible labels, statuses, descriptions, empty states, confirmations,
filter options, sort options, and recovery guidance must use the localization
system.

## 11. Data and architecture constraints

The UI redesign must preserve the following existing decisions:

- Your Pals remains save-scoped.
- `SaveIdentity` remains the ownership boundary.
- Group IDs, member keys, manual definition IDs, source identities, and
  instance IDs retain their existing meanings.
- A missing source Pal remains a visible stale member.
- Conflicts remain visible and excluded from solver inputs.
- Manual definitions preserve raw values and use ephemeral solver identity.
- Inspect custom-container data is not imported or synchronized implicitly.
- Recovery remains non-destructive and read-only until explicit repair.
- Writes continue through the canonical atomic writer.

The presentation layer may add friendly display properties and localized
status descriptions, but it must not reinterpret durable identity fields.

## 12. Accessibility and interaction requirements

- Every textbox must have a visible label or an accessible automation name;
  tooltips alone are insufficient.
- Every icon-only action must have a tooltip and accessible name.
- Status must not be communicated by color alone; include text and, where
  appropriate, an icon.
- Keyboard focus must move predictably through group navigation, toolbar,
  collection rows, and details actions.
- Selected group and selected Pal must have a non-color visual treatment.
- Destructive actions require confirmation and identify the affected group or
  member.
- Text must remain readable at the supported theme contrast levels.
- Details and Advanced surfaces must be usable without requiring horizontal
  scrolling at the minimum supported width.

## 13. Success metrics and validation

### Product success metrics

The following should be measured after release or evaluated in usability tests:

- Time for a new user to identify the page purpose.
- Time to create a group.
- Time to add a Pal to a group.
- Percentage of users who complete the add flow without assistance.
- Percentage of users who correctly explain what `Needs attention` means.
- Percentage of users who can enable solver integration and describe its
  effect.
- Number of accidental or confused uses of `Discard and reload`.
- Number of support issues caused by unresolved/stale entries being
  misunderstood.

### Usability acceptance targets

Before release, a small moderated or unmoderated test should verify that:

1. At least 80% of participants can describe the page purpose after viewing
   the initial screen for five seconds.
2. At least 80% can add an existing Pal to a named group without being told
   about source snapshots or stable identities.
3. At least 80% can find the reason an entry needs attention.
4. No participant assumes that a stale entry was automatically deleted.
5. Participants can distinguish `No matches` from `No Pals in this group`.

### Technical verification

The implementation should extend the existing UI/session test coverage for:

- Empty group versus zero filtered matches.
- Status label and explanation mapping.
- Selection stability across refresh.
- Add flow targeting the selected group.
- Save dirty-state visibility.
- Save switching and recovery-state rendering.
- Localization of all new user-facing strings.
- Responsive layout at the minimum supported width.

## 14. Delivery plan

### Phase 1: Information architecture and clarity

- Add the new header, save context, summary, and dirty-state treatment.
- Simplify the default toolbar.
- Move Advanced and recovery actions out of the primary action row.
- Add proper empty and zero-match states.
- Replace raw status text with localized user-facing labels.
- Collapse source snapshot by default.

**Exit condition:** Users can understand the screen and distinguish normal
collection actions from technical administration.

### Phase 2: Collection workflow

- Implement `+ Add Pal` picker.
- Add Pal images and friendly metadata to rows.
- Add group context menu/dialogs.
- Add selected Pal details panel.
- Add contextual remove and manual edit actions.

**Exit condition:** A user can create a group, add a Pal, inspect it, and remove
it without interacting with technical source keys.

### Phase 3: Attention and solver integration

- Add attention counts and review view.
- Add contextual stale/unresolved/conflict/invalid actions.
- Add explanatory solver-source callout and ready-entry count.

**Exit condition:** Users can understand and act on common data problems, and
they understand which entries reach the solver.

### Phase 4: Responsive, accessibility, and localization hardening

- Implement responsive breakpoint/stacked layout.
- Add keyboard and automation-name coverage.
- Complete localization for labels, statuses, descriptions, and dialogs.
- Validate themes and contrast.

**Exit condition:** The experience remains usable at minimum window size,
across supported locales, themes, and keyboard interaction.

## 15. Acceptance criteria

The redesign is complete when all of the following are true:

- The initial screen clearly identifies the active save and the purpose of the
  page.
- The normal screen contains one obvious primary action for adding a Pal.
- A user can add a source Pal without selecting a row in a raw source table.
- The default collection view prioritizes Pal identity and useful gameplay
  details over stable keys and source diagnostics.
- Groups show counts and attention indicators.
- Non-ready statuses are localized, explained, and actionable.
- The UI distinguishes empty, filtered, unavailable, and recovery states.
- Recovery and orphaned-document operations are available but do not dominate
  the normal workflow.
- The solver-source setting explains its effect and shows its current state.
- Dirty, saved, saving, failed, and read-only states are persistent and clear.
- No refresh, resolution, or UI projection operation deletes persisted members
  implicitly.
- The page remains usable at the supported minimum window size.
- All new user-facing strings are localized.
- Existing persistence, recovery, source identity, and solver tests remain
  green, with new tests covering the redesigned interactions.

## 16. Resolved product decisions

The following decisions are confirmed for this PRD:

1. **Use `My Pals` for both the tab and page heading.** This is clearer and
   more personal for users. Existing `Your Pals` names in code and persistence
   remain implementation details until a separate rename is scheduled.
2. **Make `All Pals` a virtual view.** It should query all saved group members
   without becoming a user-editable persisted group.
3. **Use single-add for the first version.** The add picker adds one Pal to the
   selected group at a time. Multi-select can be evaluated after usage data or
   user feedback shows a strong need for batch collection setup.
4. **Use the recommended manual Pal editor.** Start with a catalog-backed Pal
   selector and the currently supported fields. Do not expose every persisted
   field in the first redesign. Preserve raw unknown values internally under
   the existing persistence contract.
5. **Use an overlay details panel.** This keeps the collection layout stable,
   avoids a permanently reserved column, and works better at smaller widths.
6. **Keep the source snapshot under a collapsed Advanced section.** It remains
   available for diagnostics and repair, but does not compete with normal
   collection management. A separate page can be considered later if the
   diagnostic surface grows substantially.
7. **Keep explicit Save.** Add a persistent dirty-state indicator and clear
   navigation guardrails, but do not introduce autosave in this redesign.

## 17. Current-to-target mapping

| Current surface | Target treatment |
| --- | --- |
| Save scope text | Friendly active-save context in header |
| Session/source state | Compact status indicator or Advanced details |
| Use as solver source checkbox | Explained solver integration callout |
| Refresh | More menu plus contextual source-unavailable action |
| Discard & reload | Advanced action with explicit dirty-state guardrail |
| Search/filter/sort row | Selected-group collection toolbar |
| Group textboxes and buttons | Group dialog/context menu |
| Manual internal name textbox | Catalog-backed picker plus currently supported manual fields |
| Add source | `+ Add Pal` picker |
| Rebind/resolve | Contextual repair actions and Advanced bulk action |
| Remove duplicates | Advanced maintenance action |
| Raw member table | Friendly Pal rows/cards plus details panel |
| Recovery details expander | Attention banner with details link |
| Orphaned documents expander | Dedicated recovery surface or Advanced section |
| Expanded source snapshot | Collapsed Advanced diagnostic surface |
| Raw enum status names | Localized status label, explanation, and action |
| `No saved group members` | Separate empty-group and no-match states |

## 18. Recommendation

Implement the redesign in the order of user comprehension first, daily
collection workflow second, and advanced repair polish third. The current
architecture already contains the safety guarantees needed for this work. The
main product change is to stop making users understand those guarantees before
they can manage their Pals.
