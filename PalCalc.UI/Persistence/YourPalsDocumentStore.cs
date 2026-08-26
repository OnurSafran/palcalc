using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PalCalc.UI.Model;
using PalCalc.UI.Persistence.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PalCalc.UI.Persistence
{
    internal sealed class YourPalsDocumentLoadResult
    {
        internal YourPalsDocumentLoadResult(
            string documentPath,
            YourPalsDocument document,
            YourPalsRecoveryState recoveryState,
            IReadOnlyList<YourPalsDiagnostic> diagnostics,
            bool isNew)
        {
            DocumentPath = documentPath;
            Document = document;
            RecoveryState = recoveryState;
            Diagnostics = diagnostics;
            IsNew = isNew;
        }

        public string DocumentPath { get; }
        public YourPalsDocument Document { get; }
        public YourPalsRecoveryState RecoveryState { get; }
        public IReadOnlyList<YourPalsDiagnostic> Diagnostics { get; }
        public bool IsNew { get; }
        public bool CanPersistSafely => RecoveryState == YourPalsRecoveryState.Healthy;
        public bool IsReadOnly => !CanPersistSafely;
        internal string ContentFingerprint { get; set; }
        internal string ContentPath { get; set; }
        internal string PrimaryContentFingerprint { get; set; }
    }

    internal sealed class YourPalsDocumentWriteException : Exception
    {
        public YourPalsDocumentWriteException(string message)
            : base(message)
        {
        }

        public YourPalsDocumentWriteException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public YourPalsDocumentWriteException(string message, bool isExternalConflict)
            : base(message)
        {
            IsExternalConflict = isExternalConflict;
        }

        public bool IsExternalConflict { get; }
    }

    internal sealed class YourPalsDocumentStore
    {
        private static readonly ConcurrentDictionary<string, object> writeGates = new(StringComparer.Ordinal);

        public YourPalsDocumentStore(string documentPath)
        {
            if (string.IsNullOrWhiteSpace(documentPath))
                throw new ArgumentException("A document path is required.", nameof(documentPath));

            DocumentPath = Path.GetFullPath(documentPath);
        }

        public string DocumentPath { get; }

        public YourPalsDocumentLoadResult Load(SaveIdentity expectedOwner)
        {
            var primaryExists = File.Exists(DocumentPath);
            var primary = primaryExists
                ? LoadFile(DocumentPath, expectedOwner)
                : null;

            // A valid backup is recovery data, not an implicit replacement. Load it
            // read-only and require explicit repair before creating/replacing the primary.
            var canTryBackup = !primaryExists || primary?.RecoveryState == YourPalsRecoveryState.CorruptReadOnly;
            var backupPath = DocumentPath + ".bak";
            YourPalsDocumentLoadResult backup = null;
            if (canTryBackup && File.Exists(backupPath))
            {
                backup = LoadFile(backupPath, expectedOwner);
                if (backup.Document != null &&
                    backup.RecoveryState is YourPalsRecoveryState.Healthy or
                    YourPalsRecoveryState.PartiallyRecoveredReadOnly)
                {
                    var diagnostics = new List<YourPalsDiagnostic>();
                    diagnostics.AddRange(primary?.Diagnostics ?? []);
                    diagnostics.Add(new YourPalsDiagnostic(
                        YourPalsDiagnosticCode.DocumentCorrupt,
                        YourPalsDiagnosticSeverity.Warning,
                        primaryExists
                            ? "The primary Your Pals document was unreadable; the backup was recovered read-only. Repair explicitly to restore it."
                            : "The primary Your Pals document was missing; the backup was recovered read-only. Repair explicitly to restore it."));
                    diagnostics.AddRange(backup.Diagnostics);

                    return new(
                        DocumentPath,
                        backup.Document,
                        YourPalsRecoveryState.PartiallyRecoveredReadOnly,
                        diagnostics.AsReadOnly(),
                        isNew: false)
                    {
                        ContentFingerprint = backup.ContentFingerprint,
                        ContentPath = backupPath,
                        PrimaryContentFingerprint = primary?.ContentFingerprint,
                    };
                }
            }

            if (primary != null)
            {
                primary.PrimaryContentFingerprint = primary.ContentFingerprint;
                return primary;
            }

            if (backup != null)
                return backup;

            return new(
                DocumentPath,
                null,
                YourPalsRecoveryState.MissingReadOnly,
                [new YourPalsDiagnostic(
                    YourPalsDiagnosticCode.DocumentMissing,
                    YourPalsDiagnosticSeverity.Warning,
                    "The Your Pals document does not exist yet. Create it explicitly before editing." )],
                isNew: true);
        }

        public YourPalsDocumentLoadResult CreateNew(SaveIdentity expectedOwner)
        {
            return new(
                DocumentPath,
                YourPalsDocument.Empty(expectedOwner),
                YourPalsRecoveryState.Healthy,
                [],
                isNew: true)
            {
                ContentPath = DocumentPath,
            };
        }

        public void Save(YourPalsDocumentLoadResult loaded, YourPalsDocument document)
        {
            SaveCore(loaded, document, allowPartialRecovery: false);
        }

        public YourPalsDocumentLoadResult RepairAndSave(
            YourPalsDocumentLoadResult loaded,
            YourPalsDocument document)
        {
            if (loaded?.RecoveryState != YourPalsRecoveryState.PartiallyRecoveredReadOnly)
                throw new InvalidOperationException(
                    "Only a partially recovered Your Pals document can be explicitly repaired.");

            var json = SaveCore(loaded, document, allowPartialRecovery: true);
            return new(
                DocumentPath,
                document,
                YourPalsRecoveryState.Healthy,
                [],
                isNew: false)
            {
                ContentFingerprint = Fingerprint(json),
                ContentPath = DocumentPath,
                PrimaryContentFingerprint = Fingerprint(json),
            };
        }

        private string SaveCore(
            YourPalsDocumentLoadResult loaded,
            YourPalsDocument document,
            bool allowPartialRecovery)
        {
            if (loaded == null)
                throw new ArgumentNullException(nameof(loaded));

            if (!loaded.CanPersistSafely &&
                !(allowPartialRecovery &&
                    loaded.RecoveryState == YourPalsRecoveryState.PartiallyRecoveredReadOnly))
                throw new InvalidOperationException(
                    "The Your Pals document was not loaded safely and cannot be replaced implicitly.");

            if (!string.Equals(loaded.DocumentPath, DocumentPath, StringComparison.Ordinal))
                throw new InvalidOperationException("A loaded Your Pals document belongs to another document path.");

            if (document == null)
                throw new ArgumentNullException(nameof(document));

            if (loaded.Document.OwnerSaveIdentity != document.OwnerSaveIdentity)
                throw new InvalidOperationException("A Your Pals document cannot be saved under another save identity.");

            var writeGate = writeGates.GetOrAdd(DocumentPath, _ => new object());
            lock (writeGate)
            {
                var currentFingerprint = File.Exists(loaded.ContentPath ?? DocumentPath)
                    ? Fingerprint(File.ReadAllText(loaded.ContentPath ?? DocumentPath))
                    : null;
                if (!string.Equals(currentFingerprint, loaded.ContentFingerprint, StringComparison.Ordinal))
                {
                    throw new YourPalsDocumentWriteException(
                        "The Your Pals document changed outside this session and was not overwritten. Reload before saving.",
                        isExternalConflict: true);
                }

                if (!string.Equals(loaded.ContentPath, DocumentPath, StringComparison.Ordinal))
                {
                    var currentPrimaryFingerprint = File.Exists(DocumentPath)
                        ? Fingerprint(File.ReadAllText(DocumentPath))
                        : null;
                    if (!string.Equals(currentPrimaryFingerprint, loaded.PrimaryContentFingerprint, StringComparison.Ordinal))
                    {
                        throw new YourPalsDocumentWriteException(
                            "The primary Your Pals document changed outside this session and was not overwritten. Reload before saving.",
                            isExternalConflict: true);
                    }
                }

                var json = YourPalsDocumentJsonSerializer.ToJson(document);
                try
                {
                    StorageFile.WriteAtomic(
                        DocumentPath,
                        json,
                        backup: true,
                        preserveExistingBackup: !string.Equals(loaded.ContentPath, DocumentPath, StringComparison.Ordinal));
                    loaded.ContentFingerprint = Fingerprint(json);
                    loaded.ContentPath = DocumentPath;
                    loaded.PrimaryContentFingerprint = loaded.ContentFingerprint;
                    return json;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new YourPalsDocumentWriteException(
                        "The Your Pals document could not be saved; the in-memory document remains dirty.",
                        ex);
                }
            }
        }

        private static string Fingerprint(string contents)
        {
            using var hash = SHA256.Create();
            return Convert.ToHexString(hash.ComputeHash(Encoding.UTF8.GetBytes(contents)));
        }

        private YourPalsDocumentLoadResult Recovery(
            YourPalsRecoveryState state,
            YourPalsDiagnosticCode code,
            string message) => new(
                DocumentPath,
                null,
                state,
                new[] { new YourPalsDiagnostic(code, YourPalsDiagnosticSeverity.Error, message) },
                isNew: false)
            {
                ContentPath = DocumentPath,
            };

        private YourPalsDocumentLoadResult LoadFile(string path, SaveIdentity expectedOwner)
        {
            string json = null;
            YourPalsDocumentLoadResult result;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result = Recovery(
                    YourPalsRecoveryState.CorruptReadOnly,
                    YourPalsDiagnosticCode.DocumentCorrupt,
                    $"The Your Pals document could not be read: {ex.Message}");
                result.ContentPath = path;
                return result;
            }

            try
            {
                var envelope = JObject.Parse(json);
                if (envelope["documentVersion"]?.Type != JTokenType.Integer)
                {
                    result = Recovery(
                        YourPalsRecoveryState.CorruptReadOnly,
                        YourPalsDiagnosticCode.DocumentCorrupt,
                        "The Your Pals document has no valid document version.");
                }
                else
                {
                    var version = envelope["documentVersion"].Value<int>();
                    if (version > YourPalsContract.CurrentDocumentVersion)
                    {
                        result = Recovery(
                            YourPalsRecoveryState.UnsupportedVersionReadOnly,
                            YourPalsDiagnosticCode.UnsupportedDocumentVersion,
                            $"Your Pals document version {version} is newer than the supported version {YourPalsContract.CurrentDocumentVersion}.");
                    }
                    else if (version < YourPalsContract.CurrentDocumentVersion)
                    {
                        result = Recovery(
                            YourPalsRecoveryState.MigrationPending,
                            YourPalsDiagnosticCode.UnsupportedDocumentVersion,
                            $"Your Pals document version {version} requires migration before it can be written.");
                    }
                    else
                    {
                        var recovered = YourPalsDocumentRecoveryReader.Read(envelope, expectedOwner);
                        result = new(
                            DocumentPath,
                            recovered.Document,
                            recovered.RecoveryState,
                            recovered.Diagnostics,
                            isNew: false);
                    }
                }
            }
            catch (Exception ex)
            {
                result = Recovery(
                    YourPalsRecoveryState.CorruptReadOnly,
                    YourPalsDiagnosticCode.DocumentCorrupt,
                    $"The Your Pals document is not readable: {ex.Message}");
            }

            result.ContentFingerprint = Fingerprint(json);
            result.ContentPath = path;
            return result;
        }
    }
}
