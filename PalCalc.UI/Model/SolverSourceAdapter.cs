using PalCalc.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PalCalc.UI.Model
{
    internal sealed record YourPalsSolverSourceEntry(
        string PalEntryKey,
        PalInstance Record);

    internal sealed class YourPalsSolverSourceProjection
    {
        public YourPalsSolverSourceProjection(
            IReadOnlyList<YourPalsSolverSourceEntry> entries,
            IReadOnlyList<YourPalsResolvedMember> excludedEntries)
        {
            Entries = entries;
            ExcludedEntries = excludedEntries;
        }

        public IReadOnlyList<YourPalsSolverSourceEntry> Entries { get; }
        public IReadOnlyList<YourPalsResolvedMember> ExcludedEntries { get; }
        public IReadOnlyList<PalInstance> Pals => Entries.Select(entry => entry.Record).ToList().AsReadOnly();
    }

    internal static class SolverSourceAdapter
    {
        public static YourPalsSolverSourceProjection Build(SavePalsSession session) =>
            Build(session?.ResolvedMembers);

        public static YourPalsSolverSourceProjection Build(
            IEnumerable<YourPalsResolvedMember> resolvedMembers)
        {
            var entries = new List<YourPalsSolverSourceEntry>();
            var excluded = new List<YourPalsResolvedMember>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var resolved in resolvedMembers ?? [])
            {
                var record = resolved?.ResolvedRecord ?? resolved?.SourceEntry?.Record;
                if (resolved?.Status != YourPalsEntryStatus.Resolved ||
                    !YourPalsSourceEligibility.IsUsable(record))
                {
                    if (resolved != null)
                        excluded.Add(resolved);
                    continue;
                }

                var identity = resolved.ManualDefinition != null
                    ? $"manual:{resolved.ManualDefinition.ManualDefinitionId}"
                    : $"{resolved.SourceEntry?.SourceIdentity.StableKey}:{record.InstanceId}";
                if (!seen.Add(identity))
                    continue;

                entries.Add(new YourPalsSolverSourceEntry(
                    resolved.Member?.PalEntryKey ?? record.InstanceId,
                    record));
            }

            return new(
                entries.AsReadOnly(),
                excluded.AsReadOnly());
        }
    }
}
