using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PalCalc.UI.Model;
using PalCalc.UI.Persistence.Dto;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PalCalc.UI.Persistence.Serialization
{
    internal static class YourPalsDocumentJsonSerializer
    {
        public static YourPalsDocumentDto FromCurrentJson(string json) =>
            JsonConvert.DeserializeObject<YourPalsDocumentDto>(json)
                ?? throw new JsonSerializationException("Your Pals document was empty.");

        public static string ToJson(YourPalsDocument document) =>
            JsonConvert.SerializeObject(ToDto(document), Formatting.Indented);

        public static YourPalsDocument ToRuntime(YourPalsDocumentDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (!string.Equals(dto.DocumentType, YourPalsContract.DocumentType, StringComparison.Ordinal))
                throw new JsonSerializationException($"Unexpected Your Pals document type '{dto.DocumentType}'.");

            if (dto.DocumentVersion != YourPalsContract.CurrentDocumentVersion)
                throw new JsonSerializationException(
                    $"Your Pals document version {dto.DocumentVersion} cannot be written without an explicit migration.");

            if (dto.OwnerSaveIdentity == null)
                throw new JsonSerializationException("Your Pals document has no owner save identity.");

            var document = new YourPalsDocument
            {
                OwnerSaveIdentity = SaveIdentity.Create(dto.OwnerSaveIdentity.UserId, dto.OwnerSaveIdentity.GameId),
                Groups = (dto.Groups ?? []).Select(ToRuntime).ToList(),
                ManualDefinitions = (dto.ManualDefinitions ?? []).Select(ToRuntime).ToList(),
                ExtensionData = CloneExtensionData(dto.ExtensionData),
            };

            return document;
        }

        public static YourPalsDocumentDto ToDto(YourPalsDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var owner = SaveIdentity.Create(
                document.OwnerSaveIdentity.UserId,
                document.OwnerSaveIdentity.GameId);

            return new()
            {
                DocumentType = YourPalsContract.DocumentType,
                DocumentVersion = YourPalsContract.CurrentDocumentVersion,
                OwnerSaveIdentity = new SaveIdentityDto
                {
                    UserId = owner.UserId,
                    GameId = owner.GameId,
                },
                Groups = (document.Groups ?? []).Select(ToDto).ToList(),
                ManualDefinitions = (document.ManualDefinitions ?? []).Select(ToDto).ToList(),
                ExtensionData = CloneExtensionData(document.ExtensionData),
            };
        }

        private static YourPalsGroup ToRuntime(YourPalsGroupDto dto) => new()
        {
            GroupId = dto.GroupId,
            Name = dto.Name,
            Order = dto.Order,
            Members = (dto.Members ?? []).Select(ToRuntime).ToList(),
            ExtensionData = CloneExtensionData(dto.ExtensionData),
        };

        private static YourPalsMember ToRuntime(YourPalsMemberDto dto)
        {
            var extensionData = CloneExtensionData(dto.ExtensionData);
            if (dto.SourceIdentity?.ExtensionData?.Count > 0)
            {
                extensionData[YourPalsContract.SourceIdentityExtensionDataKey] = new JObject(
                    dto.SourceIdentity.ExtensionData.Select(pair => new JProperty(pair.Key, pair.Value?.DeepClone())));
            }

            return new()
            {
                PalEntryKey = dto.PalEntryKey,
                Kind = dto.Kind,
                SourceIdentity = dto.SourceIdentity == null ? null : ToRuntime(dto.SourceIdentity),
                SourceKey = dto.SourceKey,
                SourceContentFingerprint = dto.SourceContentFingerprint,
                InstanceId = dto.InstanceId,
                LastKnownInternalName = dto.LastKnownInternalName,
                LastKnownDisplayName = dto.LastKnownDisplayName,
                ManualDefinitionId = dto.ManualDefinitionId,
                ExtensionData = extensionData,
            };
        }

        private static YourPalsManualDefinition ToRuntime(YourPalsManualDefinitionDto dto) => new()
        {
            ManualDefinitionId = dto.ManualDefinitionId,
            RawInternalName = dto.RawInternalName,
            RawValues = CloneExtensionData(dto.RawValues),
            ExtensionData = CloneExtensionData(dto.ExtensionData),
        };

        private static SourceIdentity ToRuntime(SourceIdentityDto dto)
        {
            var kind = dto.Kind switch
            {
                "save" => YourPalsSourceKind.Save,
                "global-pal-storage" => YourPalsSourceKind.GlobalPalStorage,
                _ => throw new JsonSerializationException($"Unknown Your Pals source kind '{dto.Kind}'."),
            };

            if (string.IsNullOrWhiteSpace(dto.Scope))
                throw new JsonSerializationException($"Unknown Your Pals source kind '{dto.Kind}'.");

            return new(kind, dto.Scope);
        }

        private static YourPalsGroupDto ToDto(YourPalsGroup group) => new()
        {
            GroupId = group.GroupId,
            Name = group.Name,
            Order = group.Order,
            Members = (group.Members ?? []).Select(ToDto).ToList(),
            ExtensionData = CloneExtensionData(group.ExtensionData),
        };

        private static YourPalsMemberDto ToDto(YourPalsMember member)
        {
            var extensionData = CloneExtensionData(member.ExtensionData);
            JObject sourceIdentityExtensionData = null;
            if (extensionData.TryGetValue(YourPalsContract.SourceIdentityExtensionDataKey, out var rawSourceIdentityExtensionData))
            {
                sourceIdentityExtensionData = rawSourceIdentityExtensionData as JObject;
                extensionData.Remove(YourPalsContract.SourceIdentityExtensionDataKey);
            }

            return new()
            {
                PalEntryKey = member.PalEntryKey,
                Kind = member.Kind,
                SourceIdentity = member.SourceIdentity == null
                    ? null
                    : ToDto(member.SourceIdentity.Value, sourceIdentityExtensionData),
                SourceKey = member.SourceKey,
                SourceContentFingerprint = member.SourceContentFingerprint,
                InstanceId = member.InstanceId,
                LastKnownInternalName = member.LastKnownInternalName,
                LastKnownDisplayName = member.LastKnownDisplayName,
                ManualDefinitionId = member.ManualDefinitionId,
                ExtensionData = extensionData,
            };
        }

        private static YourPalsManualDefinitionDto ToDto(YourPalsManualDefinition definition) => new()
        {
            ManualDefinitionId = definition.ManualDefinitionId,
            RawInternalName = definition.RawInternalName,
            RawValues = CloneExtensionData(definition.RawValues),
            ExtensionData = CloneExtensionData(definition.ExtensionData),
        };

        private static SourceIdentityDto ToDto(
            SourceIdentity identity,
            JObject sourceIdentityExtensionData = null) => new()
        {
            Kind = identity.Kind switch
            {
                YourPalsSourceKind.Save => "save",
                YourPalsSourceKind.GlobalPalStorage => "global-pal-storage",
                _ => throw new JsonSerializationException($"Unknown Your Pals source kind '{identity.Kind}'."),
            },
            Scope = identity.Scope,
            ExtensionData = sourceIdentityExtensionData?.Properties()
                .ToDictionary(property => property.Name, property => property.Value.DeepClone()),
        };

        private static IDictionary<string, Newtonsoft.Json.Linq.JToken> CloneExtensionData(
            IDictionary<string, Newtonsoft.Json.Linq.JToken> source) =>
            source?.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone())
                ?? new Dictionary<string, Newtonsoft.Json.Linq.JToken>();
    }
}
