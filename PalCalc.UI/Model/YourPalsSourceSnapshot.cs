using PalCalc.Model;
using PalCalc.SaveReader;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PalCalc.UI.Model
{
    internal sealed class YourPalsSourceSnapshot
    {
        private YourPalsSourceSnapshot(
            IReadOnlyList<YourPalsSourceEntry> entries,
            IReadOnlyList<YourPalsDiagnostic> diagnostics,
            bool isAvailable)
        {
            Entries = entries;
            Diagnostics = diagnostics;
            IsAvailable = isAvailable;
        }

        public IReadOnlyList<YourPalsSourceEntry> Entries { get; }
        public IReadOnlyList<YourPalsDiagnostic> Diagnostics { get; }
        public bool IsAvailable { get; }

        public bool IsSolverEligible(YourPalsSourceEntry entry) =>
            GetSolverEligibilityFailureReason(entry) == null;

        public bool IsSelectableForImport(YourPalsSourceEntry entry) =>
            GetImportSelectionFailureReason(entry) == null;

        public string GetImportSelectionFailureReason(YourPalsSourceEntry entry)
        {
            if (entry == null)
                return "A source Pal must be selected.";

            if (!(Entries ?? []).Any(candidate => ReferenceEquals(candidate, entry)))
                return "The selected source Pal does not belong to the active snapshot.";

            var failure = YourPalsSourceEligibility.FailureReason(entry.Record);
            if (failure != null)
                return failure;

            // A conflicting instance may legitimately appear more than once (the
            // "choose a copy" repair flow selects one of them), but the chosen copy
            // must still be addressable on its own: the saved member records the
            // source key and content fingerprint and resolves through them later.
            // `entry` is always one of `matches`, so uniqueness has to be measured
            // by counting the entries that share its fingerprint, not by asking
            // whether any entry has it.
            var matches = Entries
                .Where(candidate => candidate.SourceIdentity == entry.SourceIdentity &&
                    string.Equals(candidate.InstanceId, entry.InstanceId, StringComparison.Ordinal) &&
                    string.Equals(candidate.SourceKey, entry.SourceKey, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1 && matches.Count(candidate =>
                    string.Equals(candidate.ContentFingerprint, entry.ContentFingerprint, StringComparison.Ordinal)) != 1)
                return "The selected source Pal has no unique source key in the active snapshot.";

            return null;
        }

        public string GetSolverEligibilityFailureReason(YourPalsSourceEntry entry)
        {
            if (entry == null)
                return "A source Pal must be selected.";

            var matches = Entries
                .Where(candidate => candidate.SourceIdentity == entry.SourceIdentity &&
                    string.Equals(candidate.InstanceId, entry.InstanceId, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1 || !ReferenceEquals(matches[0], entry))
                return "The source Pal has conflicting duplicate records in the active snapshot.";

            return YourPalsSourceEligibility.FailureReason(entry.Record);
        }

        public static YourPalsSourceSnapshot Build(
            SaveIdentity owner,
            CachedSaveGame cachedSave,
            ISavesLocation sourceLocation)
        {
            if (cachedSave == null)
            {
                return Unavailable("No cached save data is available to build the Pal source snapshot.");
            }

            SourceIdentity? globalIdentity = null;
            var diagnostics = new List<YourPalsDiagnostic>();
            var sourceEntries = new List<YourPalsSourceEntry>();
            var ownershipScope = YourPalsOwnershipScope.Resolve(cachedSave);
            if (!ownershipScope.OwnedDataIsKnown)
            {
                diagnostics.Add(new YourPalsDiagnostic(
                    YourPalsDiagnosticCode.OwnershipUnresolved,
                    YourPalsDiagnosticSeverity.Error,
                    "The selected save's Pal ownership could not be resolved safely; the source snapshot is unavailable."));
            }

            // Inspect custom containers are an independent user store. They must not
            // become imported save references just because a combined source list
            // contains a custom-location record.
            var scopedPals = ownershipScope
                .FilterPals(cachedSave)
                .Where(p => p?.Location?.Type != LocationType.Custom)
                .ToList();

            var hasGlobalPals = scopedPals.Any(p =>
                p?.Location?.Type == LocationType.GlobalPalStorage);
            if (hasGlobalPals)
            {
                try
                {
                    globalIdentity = SourceIdentity.ForGlobalPalStorage(sourceLocation);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    diagnostics.Add(new YourPalsDiagnostic(
                        YourPalsDiagnosticCode.SourceUnavailable,
                        YourPalsDiagnosticSeverity.Error,
                        $"Global Pal Storage could not be identified: {ex.Message}"));
                }
            }

            foreach (var pal in scopedPals)
            {
                if (pal == null || string.IsNullOrWhiteSpace(pal.InstanceId))
                {
                    diagnostics.Add(new YourPalsDiagnostic(
                        YourPalsDiagnosticCode.InvalidSourceRecord,
                        YourPalsDiagnosticSeverity.Warning,
                        "A source Pal without a non-empty instance ID was excluded from the snapshot."));
                    continue;
                }

                SourceIdentity sourceIdentity;
                if (pal.Location?.Type == LocationType.GlobalPalStorage)
                {
                    if (globalIdentity == null)
                        continue;

                    sourceIdentity = globalIdentity.Value;
                }
                else
                {
                    sourceIdentity = SourceIdentity.ForSave(owner);
                }

                sourceEntries.Add(new YourPalsSourceEntry
                {
                    SourceIdentity = sourceIdentity,
                    SourceKey = SourceKeyFor(pal),
                    InstanceId = pal.InstanceId,
                    Record = pal,
                    ContentFingerprint = ContentFingerprintFor(pal),
                });
            }

            var normalized = new List<YourPalsSourceEntry>();
            foreach (var group in sourceEntries.GroupBy(
                entry => (entry.SourceIdentity, entry.InstanceId),
                new SourceEntryGroupComparer()))
            {
                var records = group
                    .OrderBy(entry => entry.SourceKey, StringComparer.Ordinal)
                    .ThenBy(entry => entry.ContentFingerprint, StringComparer.Ordinal)
                    .ToList();
                var first = records[0];

                if (records.Count == 1)
                {
                    normalized.Add(first);
                    continue;
                }

                if (records.Skip(1).All(record =>
                    PalBreedingCatalogCalculator.AreEquivalentOwnedRecords(first.Record, record.Record)))
                {
                    normalized.Add(first);
                    diagnostics.Add(new YourPalsDiagnostic(
                        YourPalsDiagnosticCode.DuplicateSourceRecord,
                        YourPalsDiagnosticSeverity.Info,
                        $"Duplicate source records for instance '{first.InstanceId}' were deduplicated."));
                }
                else
                {
                    normalized.AddRange(records);
                    diagnostics.Add(new YourPalsDiagnostic(
                        YourPalsDiagnosticCode.ConflictingSourceRecord,
                        YourPalsDiagnosticSeverity.Error,
                        $"Source records for instance '{first.InstanceId}' conflict and remain visible."));
                }
            }

            var ordered = normalized
                .OrderBy(entry => entry.SourceIdentity.StableKey, StringComparer.Ordinal)
                .ThenBy(entry => entry.InstanceId, StringComparer.Ordinal)
                .ThenBy(entry => entry.SourceKey, StringComparer.Ordinal)
                .ThenBy(entry => entry.ContentFingerprint, StringComparer.Ordinal)
                .ToList();

            return new(
                ordered.AsReadOnly(),
                diagnostics.AsReadOnly(),
                isAvailable: ownershipScope.OwnedDataIsKnown &&
                    !diagnostics.Any(d => d.Code == YourPalsDiagnosticCode.SourceUnavailable));
        }

        private static YourPalsSourceSnapshot Unavailable(string message) => new(
            [],
            [new YourPalsDiagnostic(
                YourPalsDiagnosticCode.SourceUnavailable,
                YourPalsDiagnosticSeverity.Error,
                message)],
            isAvailable: false);

        private static string SourceKeyFor(PalInstance pal) =>
            $"{pal.Location?.Type}:{pal.Location?.ContainerId ?? "unknown"}:{pal.Location?.Index ?? -1}";

        private static string ContentFingerprintFor(PalInstance pal)
        {
            // Length-prefix every field so the ordering remains total even when a
            // source value contains the old delimiter characters.
            var fields = new[]
            {
                pal.Pal?.InternalName,
                pal.Gender.ToString(),
                pal.OwnerPlayerId,
                pal.NickName,
                pal.Level.ToString(CultureInfo.InvariantCulture),
                pal.Rank.ToString(CultureInfo.InvariantCulture),
                pal.IV_HP.ToString(CultureInfo.InvariantCulture),
                pal.IV_Melee.ToString(CultureInfo.InvariantCulture),
                pal.IV_Shot.ToString(CultureInfo.InvariantCulture),
                pal.IV_Defense.ToString(CultureInfo.InvariantCulture),
                pal.IsOnExpedition.ToString(),
                pal.Location?.Type.ToString(),
                pal.Location?.ContainerId,
                pal.Location?.Index.ToString(CultureInfo.InvariantCulture),
                StableList(pal.PassiveSkills?.Select(skill => skill?.InternalName)),
                StableList(pal.ActiveSkills?.Select(skill => skill?.InternalName)),
                StableList(pal.EquippedActiveSkills?.Select(skill => skill?.InternalName)),
            };

            return string.Concat(fields.Select(StableField));
        }

        private static string StableList(IEnumerable<string> values) =>
            string.Concat((values ?? [])
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(StableField));

        private static string StableField(string value) =>
            $"{value?.Length ?? -1}:{value}";

        private sealed class SourceEntryGroupComparer : IEqualityComparer<(SourceIdentity SourceIdentity, string InstanceId)>
        {
            public bool Equals(
                (SourceIdentity SourceIdentity, string InstanceId) x,
                (SourceIdentity SourceIdentity, string InstanceId) y) =>
                x.SourceIdentity == y.SourceIdentity &&
                string.Equals(x.InstanceId, y.InstanceId, StringComparison.Ordinal);

            public int GetHashCode((SourceIdentity SourceIdentity, string InstanceId) value) =>
                HashCode.Combine(value.SourceIdentity, StringComparer.Ordinal.GetHashCode(value.InstanceId));
        }
    }
}
