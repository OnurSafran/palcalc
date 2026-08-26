using PalCalc.Model;
using PalCalc.SaveReader;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PalCalc.UI.Model
{
    internal enum YourPalsScopeKind
    {
        Unresolved,
        SinglePlayer,
        Guild,
        Player,
        GlobalPalStorage,
    }

    internal sealed class YourPalsOwnershipScope
    {
        private YourPalsOwnershipScope(
            YourPalsScopeKind kind,
            SourceIdentity? sourceIdentity,
            string scopeName,
            bool ownedDataIsKnown,
            Func<CachedSaveGame, IEnumerable<PalInstance>> filter)
        {
            Kind = kind;
            SourceIdentity = sourceIdentity;
            ScopeName = scopeName;
            OwnedDataIsKnown = ownedDataIsKnown;
            filterPals = filter;
        }

        private readonly Func<CachedSaveGame, IEnumerable<PalInstance>> filterPals;

        public YourPalsScopeKind Kind { get; }
        public SourceIdentity? SourceIdentity { get; }
        public string ScopeName { get; }
        public bool OwnedDataIsKnown { get; }

        public IReadOnlyList<PalInstance> FilterPals(CachedSaveGame cachedSave) =>
            (filterPals(cachedSave) ?? []).ToList().AsReadOnly();

        public static YourPalsOwnershipScope Resolve(CachedSaveGame cachedSave)
        {
            if (cachedSave == null)
                return new(
                    YourPalsScopeKind.Unresolved,
                    null,
                    null,
                    ownedDataIsKnown: false,
                    _ => []);

            SourceIdentity? sourceIdentity = cachedSave.UnderlyingSave == null
                ? null
                : global::PalCalc.UI.Model.SourceIdentity.ForSave(SaveIdentity.From(cachedSave.UnderlyingSave));
            var rawPals = cachedSave.OwnedPals ?? [];

            if (!cachedSave.IsServerSave)
            {
                return new(
                    YourPalsScopeKind.SinglePlayer,
                    sourceIdentity,
                    null,
                    ownedDataIsKnown: true,
                    _ => rawPals);
            }

            var mainPlayer = cachedSave.Players?.FirstOrDefault(p => p?.Name == cachedSave.PlayerName);
            if (mainPlayer == null &&
                (string.IsNullOrWhiteSpace(cachedSave.PlayerName) ||
                 string.Equals(cachedSave.PlayerName, "UNKNOWN", StringComparison.OrdinalIgnoreCase)))
            {
                mainPlayer = cachedSave.Players?.FirstOrDefault();
            }

            if (mainPlayer == null)
            {
                return new(
                    YourPalsScopeKind.Unresolved,
                    sourceIdentity,
                    null,
                    ownedDataIsKnown: false,
                    _ => []);
            }

            var playerGuild = cachedSave.Guilds?
                .FirstOrDefault(g => g?.MemberIds?.Contains(mainPlayer.PlayerId) == true);
            if (playerGuild == null)
            {
                return new(
                    YourPalsScopeKind.Player,
                    sourceIdentity,
                    mainPlayer.Name,
                    ownedDataIsKnown: true,
                    save => rawPals.Where(p => IsOwnedByPlayer(save, p, mainPlayer.PlayerId)));
            }

            var guildMemberIds = (playerGuild.MemberIds ?? new List<string> { mainPlayer.PlayerId }).ToHashSet();
            return new(
                YourPalsScopeKind.Guild,
                sourceIdentity,
                playerGuild.Name ?? playerGuild.InternalName ?? playerGuild.Id,
                ownedDataIsKnown: true,
                save => rawPals.Where(p =>
                    (p?.OwnerPlayerId != null && guildMemberIds.Contains(p.OwnerPlayerId)) ||
                    (p?.Location?.ContainerId != null &&
                     save.GuildsByContainerId?.GetValueOrDefault(p.Location.ContainerId)?.Id == playerGuild.Id)));
        }

        public static YourPalsOwnershipScope GlobalPalStorage(ISavesLocation location) => new(
            YourPalsScopeKind.GlobalPalStorage,
            global::PalCalc.UI.Model.SourceIdentity.ForGlobalPalStorage(location),
            location.FolderName,
            ownedDataIsKnown: true,
            save => (save?.OwnedPals ?? []).Where(p => p?.Location?.Type == LocationType.GlobalPalStorage));

        private static bool IsOwnedByPlayer(CachedSaveGame cachedSave, PalInstance pal, string playerId)
        {
            if (pal == null || string.IsNullOrWhiteSpace(playerId))
                return false;
            if (string.Equals(pal.OwnerPlayerId, playerId, StringComparison.Ordinal))
                return true;

            var containerId = pal.Location?.ContainerId;
            if (string.IsNullOrWhiteSpace(containerId))
                return false;

            var container = cachedSave.PalContainers?.FirstOrDefault(c => c?.Id == containerId);
            return container switch
            {
                PalboxPalContainer pbc => pbc.PlayerId == playerId,
                PlayerPartyContainer ppc => ppc.PlayerId == playerId,
                DimensionalPalStorageContainer dpsc => dpsc.PlayerId == playerId,
                GlobalPalStorageContainer gpsc => gpsc.PlayerId == playerId,
                _ => false
            };
        }
    }
}
