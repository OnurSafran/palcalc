using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PalCalc.UI.Model
{
    internal sealed class YourPalsOrphanedDocument
    {
        public YourPalsOrphanedDocument(
            string documentPath,
            SaveIdentity? ownerSaveIdentity,
            string reason)
        {
            DocumentPath = documentPath;
            OwnerSaveIdentity = ownerSaveIdentity;
            Reason = reason;
        }

        public string DocumentPath { get; }
        public SaveIdentity? OwnerSaveIdentity { get; }
        public string Reason { get; }
        public string OwnerLabel => OwnerSaveIdentity?.CanonicalKey ?? "Unknown owner";
    }

    internal static class YourPalsOrphanedDocumentManager
    {
        public static IReadOnlyList<YourPalsOrphanedDocument> Find(
            string dataRoot,
            IEnumerable<SaveIdentity> availableSaveIdentities)
        {
            if (string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(dataRoot))
                return [];

            var available = new HashSet<SaveIdentity>(availableSaveIdentities ?? []);
            var results = new List<YourPalsOrphanedDocument>();
            IEnumerable<string> paths;
            try
            {
                paths = Directory.EnumerateFiles(
                    dataRoot,
                    YourPalsContract.DocumentFileName + "*",
                    SearchOption.AllDirectories).ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return [];
            }

            foreach (var group in paths
                .Where(path =>
                    string.Equals(Path.GetFileName(path), YourPalsContract.DocumentFileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(path), YourPalsContract.DocumentFileName + ".bak", StringComparison.OrdinalIgnoreCase))
                .GroupBy(path =>
                    string.Equals(Path.GetFileName(path), YourPalsContract.DocumentFileName + ".bak", StringComparison.OrdinalIgnoreCase)
                        ? path[..^4]
                        : path,
                    StringComparer.OrdinalIgnoreCase))
            {
                var documentPath = group.Key;
                var primaryPath = group.FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path),
                    YourPalsContract.DocumentFileName,
                    StringComparison.OrdinalIgnoreCase));
                var backupPath = group.FirstOrDefault(path => string.Equals(
                    Path.GetFileName(path),
                    YourPalsContract.DocumentFileName + ".bak",
                    StringComparison.OrdinalIgnoreCase));

                var primaryOwner = default(SaveIdentity);
                var backupOwner = default(SaveIdentity);
                string primaryReason = null;
                string backupReason = null;
                var primaryReadable = !string.IsNullOrWhiteSpace(primaryPath) &&
                    TryReadOwner(primaryPath, out primaryOwner, out primaryReason);
                var backupReadable = !string.IsNullOrWhiteSpace(backupPath) &&
                    TryReadOwner(backupPath, out backupOwner, out backupReason);

                if (primaryReadable && backupReadable && primaryOwner != backupOwner)
                {
                    results.Add(new YourPalsOrphanedDocument(
                        Path.GetFullPath(documentPath),
                        null,
                        "The document and its backup have different owner identities; manual review is required."));
                    continue;
                }

                var owner = primaryReadable ? primaryOwner : backupReadable ? backupOwner : default;
                if (!primaryReadable && !backupReadable)
                {
                    results.Add(new YourPalsOrphanedDocument(
                        Path.GetFullPath(documentPath),
                        null,
                        primaryReason ?? backupReason ??
                        "The document owner identity could not be read; manual review is required."));
                    continue;
                }

                if (available.Contains(owner))
                    continue;

                results.Add(new YourPalsOrphanedDocument(
                    Path.GetFullPath(documentPath),
                    owner,
                    primaryReadable
                        ? "The owning save is not currently available."
                        : "The primary document was unreadable; owner identity was recovered from its backup."));
            }

            return results
                .OrderBy(orphan => orphan.OwnerLabel, StringComparer.Ordinal)
                .ThenBy(orphan => orphan.DocumentPath, StringComparer.Ordinal)
                .ToList()
                .AsReadOnly();
        }

        public static bool TryDelete(
            string dataRoot,
            YourPalsOrphanedDocument orphan,
            out string error)
        {
            error = null;
            if (orphan == null || string.IsNullOrWhiteSpace(orphan.DocumentPath))
            {
                error = "An orphaned document must be selected.";
                return false;
            }

            var root = Path.GetFullPath(dataRoot ?? throw new ArgumentNullException(nameof(dataRoot)))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var documentPath = Path.GetFullPath(orphan.DocumentPath);
            if (!documentPath.StartsWith(root, StringComparison.Ordinal) ||
                !string.Equals(
                    Path.GetFileName(documentPath),
                    YourPalsContract.DocumentFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "The selected document is outside the Your Pals data directory.";
                return false;
            }

            try
            {
                if (!File.Exists(documentPath))
                {
                    if (!File.Exists(documentPath + ".bak"))
                    {
                        error = "The selected orphaned document no longer exists.";
                        return false;
                    }
                }

                var primaryOwner = default(SaveIdentity);
                var backupOwner = default(SaveIdentity);
                var primaryReadable = File.Exists(documentPath) &&
                    TryReadOwner(documentPath, out primaryOwner, out _);
                var backupPath = documentPath + ".bak";
                var backupReadable = File.Exists(backupPath) &&
                    TryReadOwner(backupPath, out backupOwner, out _);

                var ownerIsVerifiable = primaryReadable || backupReadable;
                if (primaryReadable && backupReadable && primaryOwner != backupOwner)
                    ownerIsVerifiable = false;

                // An orphan listed without an owner is a damaged document, or one
                // whose backup disagrees with it. There is nothing left to verify
                // it against, but the path check above already proved it is one of
                // Pal Calc's own documents inside its data directory - never a game
                // save - so it can still be removed. Anything that *is* verifiable
                // must match what the caller was shown, otherwise the file changed
                // since the list was built and the deletion is refused.
                if (orphan.OwnerSaveIdentity.HasValue)
                {
                    if (!ownerIsVerifiable)
                    {
                        error = "The selected orphaned document no longer has a verifiable owner; refresh the list before deleting it.";
                        return false;
                    }

                    if (orphan.OwnerSaveIdentity.Value != (primaryReadable ? primaryOwner : backupOwner))
                    {
                        error = "The selected orphaned document no longer matches its recorded owner.";
                        return false;
                    }
                }
                else if (ownerIsVerifiable)
                {
                    error = "The selected orphaned document now has a readable owner identity; refresh the list before deleting it.";
                    return false;
                }

                var stagedBackupPath = backupPath + ".delete-" + Guid.NewGuid().ToString("N");
                var backupWasStaged = false;
                try
                {
                    if (File.Exists(backupPath))
                    {
                        File.Move(backupPath, stagedBackupPath);
                        backupWasStaged = true;
                    }

                    if (File.Exists(documentPath))
                        File.Delete(documentPath);

                    if (backupWasStaged)
                        File.Delete(stagedBackupPath);
                }
                catch
                {
                    if (backupWasStaged &&
                        File.Exists(stagedBackupPath) &&
                        !File.Exists(backupPath))
                    {
                        File.Move(stagedBackupPath, backupPath);
                    }

                    throw;
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = $"The orphaned document could not be deleted: {ex.Message}";
                return false;
            }
        }

        private static bool TryReadOwner(
            string path,
            out SaveIdentity owner,
            out string reason)
        {
            owner = default;
            reason = null;
            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                var identity = root["ownerSaveIdentity"] as JObject;
                var userId = identity?["userId"]?.Value<string>();
                var gameId = identity?["gameId"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(gameId))
                {
                    reason = "The document owner identity could not be read; manual review is required.";
                    return false;
                }

                owner = SaveIdentity.Create(userId, gameId);
                return true;
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is JsonException ||
                ex is ArgumentException)
            {
                reason = $"The document owner identity could not be read: {ex.Message}";
                return false;
            }
        }
    }
}
