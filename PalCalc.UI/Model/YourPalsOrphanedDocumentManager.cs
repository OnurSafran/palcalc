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

            foreach (var path in paths
                .Where(path =>
                    string.Equals(Path.GetFileName(path), YourPalsContract.DocumentFileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(path), YourPalsContract.DocumentFileName + ".bak", StringComparison.OrdinalIgnoreCase))
                .GroupBy(path =>
                    string.Equals(Path.GetFileName(path), YourPalsContract.DocumentFileName + ".bak", StringComparison.OrdinalIgnoreCase)
                        ? path[..^4]
                        : path,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(path =>
                    string.Equals(Path.GetFileName(path), YourPalsContract.DocumentFileName, StringComparison.OrdinalIgnoreCase) ? 0 : 1).First()))
            {
                var documentPath = string.Equals(
                    Path.GetFileName(path),
                    YourPalsContract.DocumentFileName + ".bak",
                    StringComparison.OrdinalIgnoreCase)
                    ? path[..^4]
                    : path;
                if (!TryReadOwner(path, out var owner, out var reason))
                {
                    results.Add(new YourPalsOrphanedDocument(
                        Path.GetFullPath(documentPath),
                        null,
                        reason));
                    continue;
                }

                if (available.Contains(owner))
                    continue;

                results.Add(new YourPalsOrphanedDocument(
                    Path.GetFullPath(documentPath),
                    owner,
                    "The owning save is not currently available."));
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

                var ownerPath = File.Exists(documentPath) ? documentPath : documentPath + ".bak";
                var actualOwner = default(SaveIdentity);
                string ownerReadError = null;
                if (!orphan.OwnerSaveIdentity.HasValue ||
                    !TryReadOwner(ownerPath, out actualOwner, out ownerReadError))
                {
                    error = ownerReadError ??
                        "The selected orphaned document has no verifiable owner identity.";
                    return false;
                }

                if (orphan.OwnerSaveIdentity.Value != actualOwner)
                {
                    error = "The selected orphaned document no longer matches its recorded owner.";
                    return false;
                }

                var backupPath = documentPath + ".bak";
                if (File.Exists(documentPath) &&
                    File.Exists(backupPath) &&
                    TryReadOwner(backupPath, out var backupOwner, out _) &&
                    backupOwner != actualOwner)
                {
                    error = "The document backup has a different owner identity and was not deleted.";
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
