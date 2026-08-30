using PalCalc.SaveReader;
using PalCalc.UI.Persistence;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PalCalc.UI.Model
{
    internal sealed class SavePalsSession : IDisposable
    {
        private readonly object gate = new();
        private readonly YourPalsDocumentStore documentStore;
        private YourPalsDocumentLoadResult loadedDocument;
        private ISavesLocation sourceLocation;
        private List<YourPalsDiagnostic> diagnostics = [];
        private YourPalsDiagnostic sourceLoadFailureDiagnostic;
        private bool isDirty;
        private bool isOrphaned;
        private bool hasExternalConflict;
        private bool disposed;

        public SavePalsSession(
            ISaveGame save,
            ISavesLocation sourceLocation,
            CachedSaveGame cachedSave,
            YourPalsDocumentStore documentStore)
        {
            Save = save ?? throw new ArgumentNullException(nameof(save));
            Identity = SaveIdentity.From(save);
            this.sourceLocation = sourceLocation;
            this.documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
            CachedSave = cachedSave;
            ValidateCachedSaveIdentity(cachedSave);

            loadedDocument = documentStore.Load(Identity);
            Document = loadedDocument.Document;
            RebuildProjection();
            State = DetermineState();
        }

        public ISaveGame Save { get; private set; }
        public SaveIdentity Identity { get; }
        public YourPalsQueryState QueryState { get; } = new();
        public CachedSaveGame CachedSave { get; private set; }
        public YourPalsDocument Document { get; private set; }
        public YourPalsSourceSnapshot SourceSnapshot { get; private set; }
        public IReadOnlyList<YourPalsResolvedGroup> ResolvedGroups { get; private set; } = [];
        public IReadOnlyList<YourPalsResolvedMember> ResolvedMembers { get; private set; } = [];
        public IReadOnlyList<YourPalsDiagnostic> Diagnostics => diagnostics.AsReadOnly();
        public SavePalsSessionState State { get; private set; }
        public bool IsDirty => isDirty;
        public bool IsSourceAvailable => sourceLoadFailureDiagnostic == null && SourceSnapshot?.IsAvailable == true;
        public bool IsRecoveredFromBackup => loadedDocument?.ContentPath != null &&
            !string.Equals(loadedDocument.ContentPath, loadedDocument.DocumentPath, StringComparison.Ordinal);
        public bool HasUnrecoverableRecoveryData => Document?.HasUnrecoverableRecoveryData == true;
        public bool IsReadOnly => State is SavePalsSessionState.ReadOnly or SavePalsSessionState.Recovery or
            SavePalsSessionState.ExternalConflict or SavePalsSessionState.Orphaned;
        public bool CanEdit => Document != null && loadedDocument?.CanPersistSafely == true &&
            !isOrphaned && !hasExternalConflict;
        public bool CanCreateDocument => loadedDocument?.RecoveryState == YourPalsRecoveryState.MissingReadOnly &&
            Document == null &&
            !isOrphaned;
        public bool CanDiscardChangesAndReload => loadedDocument != null && !isOrphaned &&
            (isDirty || hasExternalConflict);

        public bool CanRepairRecoveredDocument =>
            loadedDocument?.RecoveryState == YourPalsRecoveryState.PartiallyRecoveredReadOnly &&
            Document != null &&
            !Document.HasUnrecoverableRecoveryData &&
            !isOrphaned &&
            !hasExternalConflict;

        public bool CanUseSourceEntry(YourPalsSourceEntry sourceEntry) =>
            IsSourceAvailable && SourceSnapshot?.IsSelectableForImport(sourceEntry) == true;

        public bool TryCreateDocument(out string error)
        {
            EventHandler refreshed;
            lock (gate)
            {
                ThrowIfDisposed();
                if (!CanCreateDocument)
                {
                    error = "A missing document can be created only from its explicit missing-document state.";
                    return false;
                }

                loadedDocument = documentStore.CreateNew(Identity);
                Document = loadedDocument.Document;
                isDirty = true;
                hasExternalConflict = false;
                RebuildProjection();
                State = SavePalsSessionState.Dirty;
                refreshed = Refreshed;
                error = null;
            }

            refreshed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void MarkDirty()
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (IsReadOnly)
                    throw new InvalidOperationException("This Your Pals session is read-only.");

                isDirty = true;
                State = SavePalsSessionState.Dirty;
            }
        }

        public bool TrySave()
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (Document == null || loadedDocument == null || !loadedDocument.CanPersistSafely ||
                    isOrphaned)
                {
                    State = IsReadOnly ? State : SavePalsSessionState.ReadOnly;
                    return false;
                }
                if (hasExternalConflict)
                {
                    State = SavePalsSessionState.ExternalConflict;
                    return false;
                }

                try
                {
                    documentStore.Save(loadedDocument, Document);
                    isDirty = false;
                    hasExternalConflict = false;
                    State = DetermineState();
                    return true;
                }
                catch (YourPalsDocumentWriteException ex) when (ex.IsExternalConflict)
                {
                    isDirty = true;
                    hasExternalConflict = true;
                    AddDiagnostic(new YourPalsDiagnostic(
                        YourPalsDiagnosticCode.ExternalConflict,
                        YourPalsDiagnosticSeverity.Error,
                        ex.Message));
                    State = SavePalsSessionState.ExternalConflict;
                    return false;
                }
                catch (Exception ex)
                {
                    isDirty = true;
                    AddDiagnostic(new YourPalsDiagnostic(
                        YourPalsDiagnosticCode.WriteFailed,
                        YourPalsDiagnosticSeverity.Error,
                        $"The Your Pals document could not be saved: {ex.Message}"));
                    State = SavePalsSessionState.WriteFailed;
                    return false;
                }
            }
        }

        public bool TryCreateGroup(string name, out string groupId, out string error)
        {
            var createdGroupId = (string)null;
            var normalizedName = name?.Trim();
            var succeeded = TryMutate(() =>
            {
                if (string.IsNullOrWhiteSpace(normalizedName))
                    throw new ArgumentException("A group name is required.", nameof(name));

                createdGroupId = NewId("group");
                Document.Groups ??= [];
                NormalizeGroupOrder();
                Document.Groups.Add(new YourPalsGroup
                {
                    GroupId = createdGroupId,
                    Name = normalizedName,
                    Order = Document.Groups.Count,
                    Members = [],
                });
            }, out error);
            groupId = createdGroupId;
            return succeeded;
        }

        public bool TryRenameGroup(string groupId, string name, out string error)
        {
            var normalizedName = name?.Trim();
            return TryMutate(() =>
            {
                if (string.IsNullOrWhiteSpace(groupId))
                    throw new ArgumentException("A group must be selected.", nameof(groupId));
                if (string.IsNullOrWhiteSpace(normalizedName))
                    throw new ArgumentException("A group name is required.", nameof(name));

                var group = FindGroup(groupId);
                if (group == null)
                    throw new InvalidOperationException("The selected group no longer exists.");
                group.Name = normalizedName;
            }, out error);
        }

        public bool TryDeleteGroup(string groupId, out string error)
        {
            return TryMutate(() =>
            {
                if (string.IsNullOrWhiteSpace(groupId))
                    throw new ArgumentException("A group must be selected.", nameof(groupId));
                var group = FindGroup(groupId);
                if (group == null)
                    throw new InvalidOperationException("The selected group no longer exists.");
                Document.Groups.Remove(group);
                NormalizeGroupOrder();
            }, out error);
        }

        public bool TryMoveGroup(string groupId, int offset, out string error)
        {
            return TryMutate(() =>
            {
                if (string.IsNullOrWhiteSpace(groupId))
                    throw new ArgumentException("A group must be selected.", nameof(groupId));
                if (offset == 0)
                    throw new ArgumentException("A non-zero group movement is required.", nameof(offset));

                var ordered = (Document.Groups ?? [])
                    .Where(group => group != null)
                    .OrderBy(group => group.Order)
                    .ThenBy(group => group.GroupId, StringComparer.Ordinal)
                    .ToList();
                var index = ordered.FindIndex(group => string.Equals(group.GroupId, groupId, StringComparison.Ordinal));
                var destination = index + offset;
                if (index < 0 || destination < 0 || destination >= ordered.Count)
                    throw new InvalidOperationException("The selected group cannot move in that direction.");

                (ordered[index], ordered[destination]) = (ordered[destination], ordered[index]);
                for (var i = 0; i < ordered.Count; i++)
                    ordered[i].Order = i;
                Document.Groups = ordered;
            }, out error);
        }

        public bool TryAddImportedMember(
            string groupId,
            YourPalsSourceEntry sourceEntry,
            out string palEntryKey,
            out string error)
        {
            var createdPalEntryKey = (string)null;
            var succeeded = TryMutate(() =>
            {
                var group = RequireGroup(groupId);
                ValidateImportedSource(group, sourceEntry);

                createdPalEntryKey = NewMemberKey();
                group.Members ??= [];
                group.Members.Add(YourPalsMember.Imported(
                    new ImportedPalReference
                    {
                        SourceIdentity = sourceEntry.SourceIdentity,
                        SourceKey = sourceEntry.SourceKey,
                        SourceContentFingerprint = sourceEntry.ContentFingerprint,
                        InstanceId = sourceEntry.InstanceId,
                        LastKnownInternalName = sourceEntry.Record?.Pal?.InternalName,
                        LastKnownDisplayName = sourceEntry.Record?.Pal?.Name,
                    },
                    createdPalEntryKey));
            }, out error);
            palEntryKey = createdPalEntryKey;
            return succeeded;
        }

        public bool TryAddManualDefinition(
            string groupId,
            string rawInternalName,
            IDictionary<string, JToken> rawValues,
            out string manualDefinitionId,
            out string palEntryKey,
            out string error)
        {
            var createdManualDefinitionId = (string)null;
            var createdPalEntryKey = (string)null;
            var normalizedName = rawInternalName?.Trim();
            var succeeded = TryMutate(() =>
            {
                var group = RequireGroup(groupId);
                if (string.IsNullOrWhiteSpace(normalizedName))
                    throw new ArgumentException("A manual Pal internal name is required.", nameof(rawInternalName));

                createdManualDefinitionId = NewId("manual");
                createdPalEntryKey = NewMemberKey();
                Document.ManualDefinitions ??= [];
                Document.ManualDefinitions.Add(new YourPalsManualDefinition
                {
                    ManualDefinitionId = createdManualDefinitionId,
                    RawInternalName = normalizedName,
                    RawValues = CloneTokens(rawValues),
                });
                group.Members ??= [];
                group.Members.Add(YourPalsMember.Manual(createdManualDefinitionId, createdPalEntryKey));
            }, out error);
            manualDefinitionId = createdManualDefinitionId;
            palEntryKey = createdPalEntryKey;
            return succeeded;
        }

        public bool TryUpdateManualDefinition(
            string manualDefinitionId,
            string rawInternalName,
            IDictionary<string, JToken> rawValues,
            out string error)
        {
            var normalizedName = rawInternalName?.Trim();
            return TryMutate(() =>
            {
                if (string.IsNullOrWhiteSpace(manualDefinitionId))
                    throw new ArgumentException("A manual definition must be selected.", nameof(manualDefinitionId));
                if (string.IsNullOrWhiteSpace(normalizedName))
                    throw new ArgumentException("A manual Pal internal name is required.", nameof(rawInternalName));

                var definition = (Document.ManualDefinitions ?? [])
                    .FirstOrDefault(candidate => string.Equals(
                        candidate?.ManualDefinitionId,
                        manualDefinitionId,
                        StringComparison.Ordinal));
                if (definition == null)
                {
                    // Keep the member's stable reference and recreate the
                    // missing definition in place when the user explicitly
                    // repairs it through the manual editor.
                    Document.ManualDefinitions ??= [];
                    definition = new YourPalsManualDefinition
                    {
                        ManualDefinitionId = manualDefinitionId,
                    };
                    Document.ManualDefinitions.Add(definition);
                }

                definition.RawInternalName = normalizedName;
                if (rawValues != null)
                {
                    // Keep fields written by newer versions or by the original
                    // document while updating only the fields this editor owns.
                    var mergedValues = CloneTokens(definition.RawValues);
                    foreach (var pair in rawValues)
                        mergedValues[pair.Key] = pair.Value?.DeepClone();
                    definition.RawValues = mergedValues;
                }
            }, out error);
        }

        public bool TryRemoveMember(string groupId, string palEntryKey, out string error)
        {
            return TryMutate(() =>
            {
                var group = RequireGroup(groupId);
                var member = (group.Members ?? [])
                    .FirstOrDefault(candidate => string.Equals(
                        candidate?.PalEntryKey,
                        palEntryKey,
                        StringComparison.Ordinal));
                if (member == null)
                    throw new InvalidOperationException("The selected member no longer exists.");

                group.Members.Remove(member);
            }, out error);
        }

        public bool TryRebindImportedMember(
            string groupId,
            string palEntryKey,
            YourPalsSourceEntry sourceEntry,
            out string error)
        {
            return TryMutate(() =>
            {
                var group = RequireGroup(groupId);
                var member = (group.Members ?? [])
                    .FirstOrDefault(candidate => string.Equals(
                        candidate?.PalEntryKey,
                        palEntryKey,
                        StringComparison.Ordinal));
                if (member == null)
                    throw new InvalidOperationException("The selected member no longer exists.");
                if (member.KnownKind != YourPalsMemberKind.ImportedReference)
                    throw new InvalidOperationException("Only imported references can be rebound to a source Pal.");
                ValidateImportedSource(group, sourceEntry, member);

                member.SourceIdentity = sourceEntry.SourceIdentity;
                member.SourceKey = sourceEntry.SourceKey;
                member.SourceContentFingerprint = sourceEntry.ContentFingerprint;
                member.InstanceId = sourceEntry.InstanceId;
                member.LastKnownInternalName = sourceEntry.Record?.Pal?.InternalName;
                member.LastKnownDisplayName = sourceEntry.Record?.Pal?.Name;
            }, out error);
        }

        public bool TryBulkRebindMatchingMembers(
            YourPalsSourceEntry sourceEntry,
            out int repairedCount,
            out string error)
        {
            var repaired = 0;
            var succeeded = TryMutate(() =>
            {
                ValidateSourceEntry(sourceEntry);

                foreach (var member in (Document.Groups ?? [])
                    .Where(group => group != null)
                    .SelectMany(group => group.Members ?? []))
                {
                    if (member?.KnownKind != YourPalsMemberKind.ImportedReference ||
                        member.SourceIdentity != sourceEntry.SourceIdentity ||
                        !string.Equals(member.InstanceId, sourceEntry.InstanceId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var internalName = sourceEntry.Record?.Pal?.InternalName;
                    var displayName = sourceEntry.Record?.Pal?.Name;
                    if (string.Equals(member.SourceKey, sourceEntry.SourceKey, StringComparison.Ordinal) &&
                        string.Equals(member.LastKnownInternalName, internalName, StringComparison.Ordinal) &&
                        string.Equals(member.LastKnownDisplayName, displayName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    member.SourceKey = sourceEntry.SourceKey;
                    member.SourceContentFingerprint = sourceEntry.ContentFingerprint;
                    member.LastKnownInternalName = internalName;
                    member.LastKnownDisplayName = displayName;
                    repaired++;
                }

                if (repaired == 0)
                    throw new InvalidOperationException(
                        "No saved imported members matched the selected source identity and instance.");
            }, out error);
            repairedCount = repaired;
            return succeeded;
        }

        public bool TryRemoveMissingMembers(out int removedCount, out string error)
        {
            var removed = 0;
            var succeeded = TryMutate(() =>
            {
                // Every imported reference resolves as Stale when the save itself
                // could not be read, so requiring a live source is what makes this
                // bulk removal safe: it only ever runs against a save we can see.
                if (!IsSourceAvailable)
                {
                    throw new InvalidOperationException(
                        "Missing Pals can only be removed while the save source is available.");
                }

                // Match on the member instances from the current projection rather
                // than on entry keys, so a document with duplicate keys cannot drop
                // a member that is still present in the save.
                var missing = ResolvedMembers
                    .Where(resolved => resolved.Status == YourPalsEntryStatus.Stale && resolved.Member != null)
                    .Select(resolved => resolved.Member)
                    .ToHashSet();
                if (missing.Count == 0)
                    throw new InvalidOperationException("No saved Pals are missing from this save.");

                foreach (var group in (Document.Groups ?? []).Where(group => group != null))
                {
                    var retained = (group.Members ?? [])
                        .Where(member => !missing.Contains(member))
                        .ToList();
                    removed += (group.Members?.Count ?? 0) - retained.Count;
                    group.Members = retained;
                }
            }, out error);
            removedCount = removed;
            return succeeded;
        }

        public bool TryRemoveDuplicateMembers(
            out YourPalsRepairSummary summary,
            out string error)
        {
            YourPalsRepairSummary repairedSummary = null;
            var succeeded = TryMutate(() =>
            {
                repairedSummary = YourPalsRepairOperations.RemoveDuplicateMembers(Document);
                if (repairedSummary.RemovedDuplicateMembers == 0)
                    throw new InvalidOperationException("No duplicate members were found in any group.");
            }, out error);
            summary = repairedSummary;
            return succeeded;
        }

        public bool TryRepairRecoveredDocument(
            out YourPalsRepairSummary summary,
            out string error)
        {
            EventHandler refreshed;
            summary = null;
            lock (gate)
            {
                ThrowIfDisposed();
                if (!CanRepairRecoveredDocument)
                {
                    error = Document?.HasUnrecoverableRecoveryData == true
                        ? "The recovered document contains data that could not be represented safely; repair is disabled to preserve the original file."
                        : "Only a safely partially recovered document can be explicitly repaired.";
                    return false;
                }

                try
                {
                    summary = YourPalsRepairOperations.RepairRecoveredDocument(Document);
                    loadedDocument = documentStore.RepairAndSave(loadedDocument, Document);
                    isDirty = false;
                    hasExternalConflict = false;
                    RebuildProjection();
                    State = DetermineState();
                    refreshed = Refreshed;
                    error = null;
                }
                catch (YourPalsDocumentWriteException ex) when (ex.IsExternalConflict)
                {
                    isDirty = true;
                    hasExternalConflict = true;
                    AddDiagnostic(new YourPalsDiagnostic(
                        YourPalsDiagnosticCode.ExternalConflict,
                        YourPalsDiagnosticSeverity.Error,
                        ex.Message));
                    State = SavePalsSessionState.ExternalConflict;
                    error = ex.Message;
                    return false;
                }
                catch (Exception ex)
                {
                    isDirty = true;
                    AddDiagnostic(new YourPalsDiagnostic(
                        YourPalsDiagnosticCode.WriteFailed,
                        YourPalsDiagnosticSeverity.Error,
                        $"The recovered Your Pals document could not be repaired: {ex.Message}"));
                    State = SavePalsSessionState.WriteFailed;
                    error = ex.Message;
                    return false;
                }
            }

            refreshed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public YourPalsSolverSourceProjection BuildSolverSource() =>
            SolverSourceAdapter.Build(this);

        public void Refresh(
            CachedSaveGame cachedSave,
            ISavesLocation refreshedSourceLocation = null,
            ISaveGame refreshedSave = null)
        {
            RefreshCore(cachedSave, refreshedSourceLocation, refreshedSave, preserveSourceLoadFailure: false);
        }

        private void RefreshCore(
            CachedSaveGame cachedSave,
            ISavesLocation refreshedSourceLocation,
            ISaveGame refreshedSave,
            bool preserveSourceLoadFailure)
        {
            EventHandler refreshed;
            lock (gate)
            {
                ThrowIfDisposed();
                ValidateCachedSaveIdentity(cachedSave);
                if (refreshedSave != null && SaveIdentity.From(refreshedSave) != Identity)
                    throw new InvalidOperationException("A Your Pals session cannot be refreshed with another save.");

                CachedSave = cachedSave;
                if (refreshedSave != null)
                    Save = refreshedSave;
                if (refreshedSourceLocation != null)
                    sourceLocation = refreshedSourceLocation;
                // Cached data can be retained after a save disappears. Only a
                // live save object from the session manager may reactivate an
                // orphaned session.
                if (refreshedSave != null)
                    isOrphaned = false;
                if (!preserveSourceLoadFailure)
                    sourceLoadFailureDiagnostic = null;

                RebuildProjection();
                State = DetermineState();
                refreshed = Refreshed;
            }

            refreshed?.Invoke(this, EventArgs.Empty);
        }

        public void RefreshCurrent()
        {
            RefreshCore(CachedSave, sourceLocation, refreshedSave: null, preserveSourceLoadFailure: true);
        }

        public bool TryDiscardChangesAndReload(out string error)
        {
            EventHandler refreshed;
            lock (gate)
            {
                ThrowIfDisposed();
                if (isOrphaned)
                {
                    error = "The owning save is no longer available.";
                    return false;
                }

                try
                {
                    loadedDocument = documentStore.Load(Identity);
                    Document = loadedDocument.Document;
                    isDirty = false;
                    hasExternalConflict = false;
                    RebuildProjection();
                    State = DetermineState();
                    refreshed = Refreshed;
                    error = null;
                }
                catch (Exception ex)
                {
                    error = $"The Your Pals document could not be reloaded: {ex.Message}";
                    return false;
                }
            }

            refreshed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void MarkOrphaned()
        {
            EventHandler orphaned;
            lock (gate)
            {
                ThrowIfDisposed();
                isOrphaned = true;
                State = SavePalsSessionState.Orphaned;
                AddDiagnostic(new YourPalsDiagnostic(
                    YourPalsDiagnosticCode.SourceUnavailable,
                    YourPalsDiagnosticSeverity.Warning,
                    "The owning save is no longer available; Your Pals data was retained."));
                orphaned = Refreshed;
            }

            orphaned?.Invoke(this, EventArgs.Empty);
        }

        public void RecordSourceLoadFailure(Exception exception)
        {
            EventHandler failed;
            lock (gate)
            {
                ThrowIfDisposed();
                sourceLoadFailureDiagnostic = new YourPalsDiagnostic(
                    YourPalsDiagnosticCode.SourceUnavailable,
                    YourPalsDiagnosticSeverity.Error,
                    $"The save source could not be refreshed: {exception?.Message ?? "unknown error"}");
                AddDiagnostic(sourceLoadFailureDiagnostic);

                if (!isDirty && State == SavePalsSessionState.Healthy)
                    State = SavePalsSessionState.SourceUnavailable;
                failed = Refreshed;
            }

            failed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler Refreshed;

        public void Dispose()
        {
            lock (gate)
                disposed = true;
        }

        private void RebuildProjection()
        {
            SourceSnapshot = YourPalsSourceSnapshot.Build(Identity, CachedSave, sourceLocation);
            var manualDefinitions = Document?.ManualDefinitions ?? [];
            ResolvedGroups = (Document?.Groups ?? [])
                .Where(group => group != null)
                .OrderBy(group => group.Order)
                .ThenBy(group => group.GroupId, StringComparer.Ordinal)
                .Select(group => new YourPalsResolvedGroup
                {
                    Group = group,
                        Members = (group.Members ?? [])
                        .Select(member => PalReferenceResolver.Resolve(
                            member,
                            SourceSnapshot.Entries,
                            manualDefinitions))
                        .ToList()
                        .AsReadOnly(),
                })
                .ToList()
                .AsReadOnly();

            ResolvedMembers = ResolvedGroups
                .SelectMany(group => group.Members)
                .ToList()
                .AsReadOnly();

            diagnostics = [];
            diagnostics.AddRange(loadedDocument?.Diagnostics ?? []);
            diagnostics.AddRange(SourceSnapshot.Diagnostics);
            if (sourceLoadFailureDiagnostic != null)
                diagnostics.Add(sourceLoadFailureDiagnostic);
        }

        private SavePalsSessionState DetermineState()
        {
            if (isOrphaned)
                return SavePalsSessionState.Orphaned;
            if (loadedDocument == null || !loadedDocument.CanPersistSafely)
                return SavePalsSessionState.Recovery;
            if (hasExternalConflict)
                return SavePalsSessionState.ExternalConflict;
            if (isDirty)
                return SavePalsSessionState.Dirty;
            if (sourceLoadFailureDiagnostic != null || !SourceSnapshot.IsAvailable)
                return SavePalsSessionState.SourceUnavailable;
            return SavePalsSessionState.Healthy;
        }

        private void AddDiagnostic(YourPalsDiagnostic diagnostic)
        {
            if (!diagnostics.Any(existing =>
                existing.Code == diagnostic.Code &&
                existing.Message == diagnostic.Message))
            {
                diagnostics.Add(diagnostic);
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(SavePalsSession));
        }

        private bool TryMutate(Action mutation, out string error)
        {
            EventHandler refreshed;
            lock (gate)
            {
                ThrowIfDisposed();
                if (!CanEdit)
                {
                    error = "The Your Pals document is read-only and cannot be edited.";
                    return false;
                }

                try
                {
                    mutation();
                    // Deleting a group or member is the only way a manual definition
                    // becomes unreachable; drop it here so removed groups do not keep
                    // growing the document with data nothing can reference.
                    YourPalsRepairOperations.PruneUnreferencedManualDefinitions(Document);
                    isDirty = true;
                    State = SavePalsSessionState.Dirty;
                    RebuildProjection();
                    refreshed = Refreshed;
                    error = null;
                }
                catch (ArgumentException ex)
                {
                    error = ex.Message;
                    return false;
                }
                catch (InvalidOperationException ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            refreshed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private YourPalsGroup FindGroup(string groupId) =>
            (Document.Groups ?? []).FirstOrDefault(group =>
                group != null && string.Equals(group.GroupId, groupId, StringComparison.Ordinal));

        private YourPalsGroup RequireGroup(string groupId) =>
            FindGroup(groupId) ?? throw new InvalidOperationException("The selected group no longer exists.");

        private void NormalizeGroupOrder()
        {
            var ordered = (Document.Groups ?? [])
                .Where(group => group != null)
                .OrderBy(group => group.Order)
                .ThenBy(group => group.GroupId, StringComparer.Ordinal)
                .ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                ordered[i].Order = i;
            }
            Document.Groups = ordered;
        }

        private string NewMemberKey()
        {
            string key;
            do
            {
                key = NewId("entry");
            }
            while ((Document.Groups ?? [])
                .SelectMany(group => group?.Members ?? [])
                .Any(member => string.Equals(member?.PalEntryKey, key, StringComparison.Ordinal)));

            return key;
        }

        private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

        private void ValidateSourceEntry(YourPalsSourceEntry sourceEntry)
        {
            if (sourceEntry == null ||
                string.IsNullOrWhiteSpace(sourceEntry.SourceIdentity.Scope) ||
                string.IsNullOrWhiteSpace(sourceEntry.InstanceId))
            {
                throw new ArgumentException("A resolved source Pal with a stable identity is required.", nameof(sourceEntry));
            }

            if (!(SourceSnapshot?.Entries ?? []).Any(candidate => ReferenceEquals(candidate, sourceEntry)))
            {
                throw new InvalidOperationException(
                    "The selected source Pal does not belong to the active save session.");
            }

            if (!SourceSnapshot.IsSelectableForImport(sourceEntry))
            {
                throw new ArgumentException(
                    SourceSnapshot.GetImportSelectionFailureReason(sourceEntry),
                    nameof(sourceEntry));
            }
        }

        private void ValidateImportedSource(
            YourPalsGroup group,
            YourPalsSourceEntry sourceEntry,
            YourPalsMember excludedMember = null)
        {
            ValidateSourceEntry(sourceEntry);
            if ((group.Members ?? []).Any(member =>
                !ReferenceEquals(member, excludedMember) &&
                member?.KnownKind == YourPalsMemberKind.ImportedReference &&
                member.SourceIdentity == sourceEntry.SourceIdentity &&
                string.Equals(member.InstanceId, sourceEntry.InstanceId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("This source Pal is already in the selected group.");
            }
        }

        private void ValidateCachedSaveIdentity(CachedSaveGame cachedSave)
        {
            if (cachedSave?.UnderlyingSave != null &&
                SaveIdentity.From(cachedSave.UnderlyingSave) != Identity)
            {
                throw new InvalidOperationException("A Your Pals session cannot use cached data from another save.");
            }
        }

        private static IDictionary<string, JToken> CloneTokens(IDictionary<string, JToken> values) =>
            values?.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone())
            ?? new Dictionary<string, JToken>();
    }

    internal sealed class SavePalsSessionManager : IDisposable
    {
        private readonly Dictionary<SaveIdentity, SavePalsSession> sessions = new();
        private readonly HashSet<SaveIdentity> availableSaveIdentities = new();
        private bool disposed;

        public SavePalsSessionManager()
        {
            Storage.SaveReloadedWithCache += Storage_SaveReloadedWithCache;
            Storage.SaveRemoved += Storage_SaveRemoved;
            CachedSaveGame.SaveFileLoadError += CachedSaveGame_SaveFileLoadError;
        }

        public SavePalsSession ActiveSession { get; private set; }

        public IReadOnlyList<YourPalsOrphanedDocument> OrphanedDocuments =>
            YourPalsOrphanedDocumentManager.Find(Storage.DataPath, availableSaveIdentities);

        public event EventHandler OrphanedDocumentsChanged;

        public void SetAvailableSaves(IEnumerable<ISaveGame> saves)
        {
            var currentIdentities = (saves ?? [])
                .Where(save => save != null)
                .Select(SaveIdentity.From)
                .ToHashSet();

            foreach (var session in sessions.Values)
            {
                if (!currentIdentities.Contains(session.Identity) &&
                    session.State != SavePalsSessionState.Orphaned)
                {
                    session.MarkOrphaned();
                }
            }

            availableSaveIdentities.Clear();
            availableSaveIdentities.UnionWith(currentIdentities);

            OrphanedDocumentsChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool TryDeleteOrphanedDocument(
            YourPalsOrphanedDocument orphan,
            out string error)
        {
            var currentOrphan = OrphanedDocuments.FirstOrDefault(candidate =>
                string.Equals(candidate.DocumentPath, orphan?.DocumentPath, StringComparison.Ordinal));
            if (currentOrphan == null)
            {
                error = "The selected document is no longer an orphaned Your Pals document.";
                return false;
            }

            return YourPalsOrphanedDocumentManager.TryDelete(Storage.DataPath, currentOrphan, out error);
        }

        public SavePalsSession Activate(
            ISavesLocation sourceLocation,
            ISaveGame save,
            CachedSaveGame cachedSave)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SavePalsSessionManager));

            var identity = SaveIdentity.From(save);
            availableSaveIdentities.Add(identity);
            if (!sessions.TryGetValue(identity, out var session))
            {
                session = new SavePalsSession(
                    save,
                    sourceLocation,
                    cachedSave,
                    new YourPalsDocumentStore(Storage.YourPalsDocumentPath(save)));
                sessions.Add(identity, session);
            }
            else
            {
                session.Refresh(cachedSave, sourceLocation, save);
            }

            ActiveSession = session;
            return session;
        }

        public bool TryGet(SaveIdentity identity, out SavePalsSession session) =>
            sessions.TryGetValue(identity, out session);

        public void MarkOrphaned(ISaveGame save)
        {
            if (save != null && sessions.TryGetValue(SaveIdentity.From(save), out var session))
                session.MarkOrphaned();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Storage.SaveReloadedWithCache -= Storage_SaveReloadedWithCache;
            Storage.SaveRemoved -= Storage_SaveRemoved;
            CachedSaveGame.SaveFileLoadError -= CachedSaveGame_SaveFileLoadError;
            foreach (var session in sessions.Values)
                session.Dispose();
            sessions.Clear();
            availableSaveIdentities.Clear();
            ActiveSession = null;
        }

        private void Storage_SaveReloadedWithCache(
            ISavesLocation sourceLocation,
            ISaveGame save,
            CachedSaveGame cachedSave)
        {
            if (save != null && sessions.TryGetValue(SaveIdentity.From(save), out var session))
                session.Refresh(cachedSave, sourceLocation, save);
        }

        private void Storage_SaveRemoved(ISaveGame save)
        {
            if (save != null)
                availableSaveIdentities.Remove(SaveIdentity.From(save));
            MarkOrphaned(save);
            OrphanedDocumentsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void CachedSaveGame_SaveFileLoadError(ISaveGame save, Exception exception)
        {
            if (save != null && sessions.TryGetValue(SaveIdentity.From(save), out var session))
                session.RecordSourceLoadFailure(exception);
        }
    }
}
