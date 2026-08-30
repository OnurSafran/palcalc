using PalCalc.SaveReader;
using PalCalc.Model;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PalCalc.UI.Model
{
    internal static class YourPalsContract
    {
        public const string DocumentType = "your-pals";
        public const int CurrentDocumentVersion = 1;
        public const string DocumentFileName = "your-pals.json";
        public const string SourceIdentityExtensionDataKey = "_sourceIdentityExtensionData";
        public const string RecoveryRawFieldsExtensionDataKey = "_recoveryRawFields";
    }

    internal enum YourPalsSourceKind
    {
        Save,
        GlobalPalStorage,
    }

    internal readonly record struct SourceIdentity(YourPalsSourceKind Kind, string Scope)
    {
        public static SourceIdentity ForSave(SaveIdentity save) =>
            new(YourPalsSourceKind.Save, save.CanonicalKey);

        public static SourceIdentity ForGlobalPalStorage(ISavesLocation location)
        {
            if (location == null) throw new ArgumentNullException(nameof(location));
            if (string.IsNullOrWhiteSpace(location.FolderPath))
                throw new ArgumentException("Global Pal Storage requires a parent save location.", nameof(location));

            var path = Path.GetFullPath(location.FolderPath);
            var root = Path.GetPathRoot(path);
            if (!string.Equals(path, root, StringComparison.Ordinal))
                path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            path = path.Replace(Path.DirectorySeparatorChar, '/');

            return new(YourPalsSourceKind.GlobalPalStorage, path);
        }

        public string StableKey => $"{Kind}:{Scope}";
    }

    internal enum YourPalsMemberKind
    {
        ImportedReference,
        ManualDefinitionReference,
        Unknown,
    }

    internal enum YourPalsEntryStatus
    {
        Resolved,
        Unresolved,
        Stale,
        Conflict,
        Invalid,
    }

    internal enum YourPalsRecoveryState
    {
        MissingReadOnly,
        Healthy,
        PartiallyRecoveredReadOnly,
        CorruptReadOnly,
        OwnerMismatchReadOnly,
        UnsupportedVersionReadOnly,
        WriteFailed,
        MigrationPending,
        MigrationFailed,
        Orphaned,
    }

    internal enum SavePalsSessionState
    {
        Healthy,
        Dirty,
        ReadOnly,
        Recovery,
        SourceUnavailable,
        WriteFailed,
        ExternalConflict,
        Orphaned,
    }

    internal enum YourPalsDiagnosticSeverity
    {
        Info,
        Warning,
        Error,
    }

    internal enum YourPalsDiagnosticCode
    {
        MissingSourcePal,
        UnknownMemberKind,
        InvalidMember,
        MalformedGroup,
        MalformedMember,
        MalformedManualDefinition,
        InvalidSourceRecord,
        DuplicateSourceRecord,
        ConflictingSourceRecord,
        DuplicateGroupId,
        DuplicateMemberKey,
        DuplicateManualDefinitionId,
        SourceUnavailable,
        DocumentMissing,
        OwnershipUnresolved,
        DocumentCorrupt,
        DocumentOwnerMismatch,
        UnsupportedDocumentVersion,
        WriteFailed,
        ExternalConflict,
    }

    internal sealed record YourPalsDiagnostic(
        YourPalsDiagnosticCode Code,
        YourPalsDiagnosticSeverity Severity,
        string Message,
        string GroupId = null,
        string PalEntryKey = null);

    internal sealed class YourPalsDocument
    {
        public SaveIdentity OwnerSaveIdentity { get; init; }
        public List<YourPalsGroup> Groups { get; set; } = [];
        public List<YourPalsManualDefinition> ManualDefinitions { get; set; } = [];
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
        public bool HasUnrecoverableRecoveryData { get; set; }

        public static YourPalsDocument Empty(SaveIdentity owner) => new() { OwnerSaveIdentity = owner };
    }

    internal sealed class YourPalsGroup
    {
        public string GroupId { get; set; }
        public string Name { get; set; }
        public int Order { get; set; }
        public List<YourPalsMember> Members { get; set; } = [];
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    internal sealed class YourPalsMember
    {
        // Kind intentionally remains a string at the persistence boundary so a newer
        // member kind is retained as opaque data instead of being discarded.
        public string PalEntryKey { get; set; }
        public string Kind { get; set; }
        public SourceIdentity? SourceIdentity { get; set; }
        public string SourceKey { get; set; }
        // Optional so documents written before conflict-copy selection remain readable.
        public string SourceContentFingerprint { get; set; }
        public string InstanceId { get; set; }
        public string LastKnownInternalName { get; set; }
        public string LastKnownDisplayName { get; set; }
        public string ManualDefinitionId { get; set; }
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();

        public YourPalsMemberKind KnownKind => Kind switch
        {
            "imported-reference" => YourPalsMemberKind.ImportedReference,
            "manual-definition-reference" => YourPalsMemberKind.ManualDefinitionReference,
            _ => YourPalsMemberKind.Unknown,
        };

        public static YourPalsMember Imported(ImportedPalReference reference, string palEntryKey) => new()
        {
            PalEntryKey = palEntryKey,
            Kind = "imported-reference",
            SourceIdentity = reference.SourceIdentity,
            SourceKey = reference.SourceKey,
            SourceContentFingerprint = reference.SourceContentFingerprint,
            InstanceId = reference.InstanceId,
            LastKnownInternalName = reference.LastKnownInternalName,
            LastKnownDisplayName = reference.LastKnownDisplayName,
        };

        public static YourPalsMember Manual(string manualDefinitionId, string palEntryKey) => new()
        {
            PalEntryKey = palEntryKey,
            Kind = "manual-definition-reference",
            ManualDefinitionId = manualDefinitionId,
        };
    }

    internal sealed class ImportedPalReference
    {
        public SourceIdentity SourceIdentity { get; init; }
        public string SourceKey { get; init; }
        public string SourceContentFingerprint { get; init; }
        public string InstanceId { get; init; }
        public string LastKnownInternalName { get; init; }
        public string LastKnownDisplayName { get; init; }
    }

    internal sealed class YourPalsManualDefinition
    {
        public string ManualDefinitionId { get; set; }
        public string RawInternalName { get; set; }
        public IDictionary<string, JToken> RawValues { get; set; } = new Dictionary<string, JToken>();
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    internal sealed class YourPalsSourceEntry
    {
        public SourceIdentity SourceIdentity { get; init; }
        public string SourceKey { get; init; }
        public string InstanceId { get; init; }
        public PalInstance Record { get; init; }
        public string ContentFingerprint { get; init; }
    }

    internal static class YourPalsSourceEligibility
    {
        public static bool IsUsable(PalInstance record) =>
            record != null &&
            record.Pal != null &&
            IsUsableGender(record.Gender) &&
            !string.IsNullOrWhiteSpace(record.InstanceId);

        public static string FailureReason(PalInstance record)
        {
            if (record == null)
                return "The source Pal record is missing.";
            if (string.IsNullOrWhiteSpace(record.InstanceId))
                return "The source Pal has no stable instance ID.";
            if (record.Pal == null)
                return "The source instance exists, but its Pal is not known to the current catalog.";
            if (!IsUsableGender(record.Gender))
                return "The source Pal does not have a usable male or female gender.";
            return null;
        }

        private static bool IsUsableGender(PalGender gender) =>
            gender == PalGender.MALE || gender == PalGender.FEMALE;
    }

    internal sealed class YourPalsResolvedMember
    {
        public YourPalsMember Member { get; init; }
        public YourPalsEntryStatus Status { get; init; }
        public string Reason { get; init; }
        public YourPalsSourceEntry SourceEntry { get; init; }
        public YourPalsManualDefinition ManualDefinition { get; init; }
        public PalInstance ResolvedRecord { get; init; }
    }

    internal sealed class YourPalsResolvedGroup
    {
        public YourPalsGroup Group { get; init; }
        public IReadOnlyList<YourPalsResolvedMember> Members { get; init; }
    }

    internal static class PalReferenceResolver
    {
        public static YourPalsResolvedMember Resolve(
            YourPalsMember member,
            IEnumerable<YourPalsSourceEntry> sourceEntries,
            IEnumerable<YourPalsManualDefinition> manualDefinitions = null)
        {
            if (member == null)
                return new() { Status = YourPalsEntryStatus.Invalid, Reason = "The member is missing." };

            if (member.KnownKind != YourPalsMemberKind.ImportedReference)
            {
                if (member.KnownKind == YourPalsMemberKind.ManualDefinitionReference)
                {
                    var definition = (manualDefinitions ?? [])
                        .FirstOrDefault(candidate => candidate != null &&
                            string.Equals(
                                candidate.ManualDefinitionId,
                                member.ManualDefinitionId,
                                StringComparison.Ordinal));
                    if (definition == null)
                    {
                        return new()
                        {
                            Member = member,
                            Status = YourPalsEntryStatus.Unresolved,
                            Reason = "The referenced manual definition is missing.",
                        };
                    }

                    if (!YourPalsManualDefinitionResolver.TryResolve(
                            definition,
                            out var record,
                            out var reason))
                    {
                        return new()
                        {
                            Member = member,
                            ManualDefinition = definition,
                            Status = YourPalsEntryStatus.Unresolved,
                            Reason = reason,
                        };
                    }

                    return new()
                    {
                        Member = member,
                        ManualDefinition = definition,
                        ResolvedRecord = record,
                        Status = YourPalsEntryStatus.Resolved,
                        Reason = "The manual definition was resolved.",
                    };
                }

                return new()
                {
                    Member = member,
                    Status = YourPalsEntryStatus.Invalid,
                    Reason = "The member kind is not recognized by this version.",
                };
            }

            if (string.IsNullOrWhiteSpace(member.PalEntryKey) ||
                member.SourceIdentity == null ||
                string.IsNullOrWhiteSpace(member.SourceIdentity.Value.Scope) ||
                string.IsNullOrWhiteSpace(member.InstanceId))
            {
                return new()
                {
                    Member = member,
                    Status = YourPalsEntryStatus.Invalid,
                    Reason = "An imported reference requires a stable entry key, source identity, and non-empty instance ID.",
                };
            }

            var matches = (sourceEntries ?? []).Where(source => source != null &&
                source.SourceIdentity == member.SourceIdentity.Value &&
                string.Equals(source.InstanceId, member.InstanceId, StringComparison.Ordinal)).ToList();

            if (matches.Count > 1 && !string.IsNullOrWhiteSpace(member.SourceKey))
            {
                var sourceKeyMatches = matches
                    .Where(source => string.Equals(source.SourceKey, member.SourceKey, StringComparison.Ordinal))
                    .ToList();
                if (sourceKeyMatches.Count == 1)
                    matches = sourceKeyMatches;
                else if (!string.IsNullOrWhiteSpace(member.SourceContentFingerprint))
                {
                    var fingerprintMatches = sourceKeyMatches
                        .Where(source => string.Equals(
                            source.ContentFingerprint,
                            member.SourceContentFingerprint,
                            StringComparison.Ordinal))
                        .ToList();
                    if (fingerprintMatches.Count == 1)
                        matches = fingerprintMatches;
                }
            }

            if (matches.Count == 0)
            {
                return new()
                {
                    Member = member,
                    Status = YourPalsEntryStatus.Stale,
                    Reason = "The source no longer contains this Pal instance.",
                };
            }

            var firstRecord = matches[0].Record;
            if (matches.Skip(1).Any(match =>
                !PalBreedingCatalogCalculator.AreEquivalentOwnedRecords(firstRecord, match.Record)))
            {
                return new()
                {
                    Member = member,
                    Status = YourPalsEntryStatus.Conflict,
                    Reason = "Multiple source records contain conflicting data for this instance.",
                };
            }

            if (matches.Any(match => match.Record?.Pal == null))
            {
                return new()
                {
                    Member = member,
                    Status = YourPalsEntryStatus.Unresolved,
                    Reason = "The source instance exists, but its Pal is not known to the current catalog.",
                    SourceEntry = matches[0],
                };
            }

            if (matches.Any(match => !YourPalsSourceEligibility.IsUsable(match.Record)))
            {
                var invalidMatch = matches.First(match =>
                    !YourPalsSourceEligibility.IsUsable(match.Record));
                return new()
                {
                    Member = member,
                    Status = YourPalsEntryStatus.Invalid,
                    Reason = YourPalsSourceEligibility.FailureReason(invalidMatch.Record),
                    SourceEntry = invalidMatch,
                };
            }

            return new()
            {
                Member = member,
                Status = YourPalsEntryStatus.Resolved,
                Reason = "The source instance was found.",
                SourceEntry = matches[0],
            };
        }
    }
}
