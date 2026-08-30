using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PalCalc.UI.Model
{
    internal sealed record YourPalsRepairSummary(
        int RepairedGroupIds,
        int RepairedMemberKeys,
        int RepairedManualDefinitionIds,
        int RemovedDuplicateMembers,
        int RemovedDuplicateManualDefinitions,
        int RemovedInvalidMembers,
        int RemovedInvalidManualDefinitions,
        int RemovedUnreferencedManualDefinitions = 0)
    {
        public int TotalChanges =>
            RepairedGroupIds +
            RepairedMemberKeys +
            RepairedManualDefinitionIds +
            RemovedDuplicateMembers +
            RemovedDuplicateManualDefinitions +
            RemovedInvalidMembers +
            RemovedInvalidManualDefinitions +
            RemovedUnreferencedManualDefinitions;
    }

    internal static class YourPalsRepairOperations
    {
        public static YourPalsRepairSummary RepairRecoveredDocument(YourPalsDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var repairedGroupIds = 0;
            var repairedMemberKeys = 0;
            var repairedManualDefinitionIds = 0;
            var removedDuplicateManualDefinitions = 0;
            var removedInvalidMembers = 0;
            var removedInvalidManualDefinitions = 0;

            var groups = (document.Groups ?? [])
                .Select((group, index) => (Group: group, Index: index))
                .Where(item => item.Group != null)
                .OrderBy(item => item.Group.Order)
                .ThenBy(item => item.Index)
                .Select(item => item.Group)
                .ToList();

            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < groups.Count; index++)
            {
                var group = groups[index];
                var originalId = group.GroupId;
                if (string.IsNullOrWhiteSpace(originalId) || !groupIds.Add(originalId))
                {
                    group.GroupId = CreateUniqueRepairId(
                        "group",
                        originalId,
                        index,
                        groupIds);
                    repairedGroupIds++;
                }

                if (string.IsNullOrWhiteSpace(group.Name))
                    group.Name = $"Recovered group {index + 1}";

                group.Order = index;
                group.Members ??= [];
            }

            var memberKeys = new HashSet<string>(StringComparer.Ordinal);
            var removedDuplicateMembers = 0;
            foreach (var group in groups)
            {
                var repairedMembers = new List<YourPalsMember>();
                var groupMemberIdentities = new HashSet<string>(StringComparer.Ordinal);

                for (var index = 0; index < group.Members.Count; index++)
                {
                    var member = group.Members[index];
                    if (member == null)
                    {
                        removedInvalidMembers++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(member.PalEntryKey) ||
                        !memberKeys.Add(member.PalEntryKey))
                    {
                        member.PalEntryKey = CreateUniqueRepairId(
                            "entry",
                            $"{group.GroupId}:{member.PalEntryKey}",
                            index,
                            memberKeys);
                        repairedMemberKeys++;
                    }

                    var duplicateIdentity = DuplicateIdentity(member);
                    if (duplicateIdentity != null && !groupMemberIdentities.Add(duplicateIdentity))
                    {
                        removedDuplicateMembers++;
                        continue;
                    }

                    member.ExtensionData ??= new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
                    repairedMembers.Add(member);
                }

                group.Members = repairedMembers;
            }

            var manualDefinitionIds = new HashSet<string>(StringComparer.Ordinal);
            var manualDefinitions = new List<YourPalsManualDefinition>();
            foreach (var definition in document.ManualDefinitions ?? [])
            {
                if (definition == null)
                {
                    removedInvalidManualDefinitions++;
                    continue;
                }

                var originalId = definition.ManualDefinitionId;
                if (!string.IsNullOrWhiteSpace(originalId) && !manualDefinitionIds.Add(originalId))
                {
                    // A member containing a duplicate ID cannot identify which
                    // definition it meant. Keep the first stable definition as
                    // the canonical target and report the later one as removed.
                    removedDuplicateManualDefinitions++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(originalId))
                {
                    definition.ManualDefinitionId = CreateUniqueRepairId(
                        "manual",
                        originalId,
                        manualDefinitions.Count,
                        manualDefinitionIds);
                    repairedManualDefinitionIds++;
                }

                definition.RawValues ??= new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
                definition.ExtensionData ??= new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
                manualDefinitions.Add(definition);
            }

            document.Groups = groups;
            document.ManualDefinitions = manualDefinitions;

            return new(
                repairedGroupIds,
                repairedMemberKeys,
                repairedManualDefinitionIds,
                removedDuplicateMembers,
                removedDuplicateManualDefinitions,
                removedInvalidMembers,
                removedInvalidManualDefinitions,
                PruneUnreferencedManualDefinitions(document));
        }

        // A manual definition is only reachable through the member that references
        // it. Once every referencing member is gone (group deleted, member removed,
        // duplicate dropped) the definition can never be shown or edited again, so
        // it is dropped instead of being carried forward on every save.
        public static int PruneUnreferencedManualDefinitions(YourPalsDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (document.ManualDefinitions == null || document.ManualDefinitions.Count == 0)
                return 0;

            var referenced = (document.Groups ?? [])
                .Where(group => group != null)
                .SelectMany(group => group.Members ?? [])
                .Where(member => member?.KnownKind == YourPalsMemberKind.ManualDefinitionReference &&
                    !string.IsNullOrWhiteSpace(member.ManualDefinitionId))
                .Select(member => member.ManualDefinitionId)
                .ToHashSet(StringComparer.Ordinal);

            var retained = document.ManualDefinitions
                .Where(definition => definition != null &&
                    !string.IsNullOrWhiteSpace(definition.ManualDefinitionId) &&
                    referenced.Contains(definition.ManualDefinitionId))
                .ToList();

            var removed = document.ManualDefinitions.Count - retained.Count;
            if (removed > 0)
                document.ManualDefinitions = retained;

            return removed;
        }

        public static YourPalsRepairSummary RemoveDuplicateMembers(YourPalsDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var removed = 0;
            foreach (var group in document.Groups ?? [])
            {
                if (group == null)
                    continue;

                var seenKeys = new HashSet<string>(StringComparer.Ordinal);
                var seenIdentities = new HashSet<string>(StringComparer.Ordinal);
                var retained = new List<YourPalsMember>();
                for (var index = 0; index < (group.Members ?? []).Count; index++)
                {
                    var member = group.Members[index];
                    if (member == null)
                        continue;

                    var identity = DuplicateIdentity(member) ??
                        $"entry:{member.PalEntryKey ?? $"missing:{index}"}";
                    var hasDuplicateKey = !string.IsNullOrWhiteSpace(member.PalEntryKey) &&
                        !seenKeys.Add(member.PalEntryKey);
                    if (hasDuplicateKey || !seenIdentities.Add(identity))
                    {
                        removed++;
                        continue;
                    }

                    retained.Add(member);
                }

                if (retained.Count != (group.Members?.Count ?? 0))
                    group.Members = retained;
            }

            // Pruning is deliberately left to the caller: this operation reports
            // "nothing to do" by leaving the document untouched when removed == 0.
            return new(0, 0, 0, removed, 0, 0, 0);
        }

        private static string DuplicateIdentity(YourPalsMember member)
        {
            if (member == null)
                return null;

            return member.KnownKind switch
            {
                YourPalsMemberKind.ImportedReference when member.SourceIdentity.HasValue &&
                    !string.IsNullOrWhiteSpace(member.InstanceId) =>
                    $"imported:{member.SourceIdentity.Value.StableKey}:{member.InstanceId}",
                YourPalsMemberKind.ManualDefinitionReference when
                    !string.IsNullOrWhiteSpace(member.ManualDefinitionId) =>
                    $"manual:{member.ManualDefinitionId}",
                _ when !string.IsNullOrWhiteSpace(member.PalEntryKey) =>
                    $"entry:{member.PalEntryKey}",
                _ => null,
            };
        }

        private static string CreateUniqueRepairId(
            string prefix,
            string original,
            int ordinal,
            ISet<string> used)
        {
            var payload = $"your-pals-repair\0{prefix}\0{original ?? "(missing)"}\0{ordinal}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
                .ToLowerInvariant()[..16];
            var candidate = $"repaired-{prefix}-{hash}";
            var suffix = 1;
            while (!used.Add(candidate))
                candidate = $"repaired-{prefix}-{hash}-{++suffix}";

            return candidate;
        }
    }
}
