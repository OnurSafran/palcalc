using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace PalCalc.UI.Persistence.Dto
{
    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class SaveIdentityDto
    {
        [JsonProperty("userId", Required = Required.Always)]
        public string UserId { get; init; }

        [JsonProperty("gameId", Required = Required.Always)]
        public string GameId { get; init; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class SourceIdentityDto
    {
        [JsonProperty("kind", Required = Required.Always)]
        public string Kind { get; init; }

        [JsonProperty("scope", Required = Required.Always)]
        public string Scope { get; init; }

        [JsonExtensionData(ReadData = true, WriteData = true)]
        public IDictionary<string, JToken> ExtensionData { get; init; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class YourPalsDocumentDto
    {
        [JsonProperty("documentType", Required = Required.Always)]
        public string DocumentType { get; init; }

        [JsonProperty("documentVersion", Required = Required.Always)]
        public int DocumentVersion { get; init; }

        [JsonProperty("ownerSaveIdentity", Required = Required.Always)]
        public SaveIdentityDto OwnerSaveIdentity { get; init; }

        [JsonProperty("groups", Required = Required.Always)]
        public List<YourPalsGroupDto> Groups { get; init; }

        [JsonProperty("manualDefinitions", Required = Required.Always)]
        public List<YourPalsManualDefinitionDto> ManualDefinitions { get; init; }

        [JsonExtensionData(ReadData = true, WriteData = true)]
        public IDictionary<string, JToken> ExtensionData { get; init; }
    }

    [JsonObject(ItemRequired = Required.Always)]
    internal sealed class YourPalsGroupDto
    {
        [JsonProperty("groupId", Required = Required.Always)]
        public string GroupId { get; init; }

        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; init; }

        [JsonProperty("order", Required = Required.Always)]
        public int Order { get; init; }

        [JsonProperty("members", Required = Required.Always)]
        public List<YourPalsMemberDto> Members { get; init; }

        [JsonExtensionData(ReadData = true, WriteData = true)]
        public IDictionary<string, JToken> ExtensionData { get; init; }
    }

    [JsonObject]
    internal sealed class YourPalsMemberDto
    {
        [JsonProperty("palEntryKey", Required = Required.Always)]
        public string PalEntryKey { get; init; }

        // Keep this as a string. Unknown future kinds must survive a read/write cycle.
        [JsonProperty("kind", Required = Required.Always)]
        public string Kind { get; init; }

        [JsonProperty("sourceIdentity")]
        public SourceIdentityDto SourceIdentity { get; init; }

        [JsonProperty("sourceKey")]
        public string SourceKey { get; init; }

        [JsonProperty("sourceContentFingerprint")]
        public string SourceContentFingerprint { get; init; }

        [JsonProperty("instanceId")]
        public string InstanceId { get; init; }

        [JsonProperty("lastKnownInternalName")]
        public string LastKnownInternalName { get; init; }

        [JsonProperty("lastKnownDisplayName")]
        public string LastKnownDisplayName { get; init; }

        [JsonProperty("manualDefinitionId")]
        public string ManualDefinitionId { get; init; }

        [JsonExtensionData(ReadData = true, WriteData = true)]
        public IDictionary<string, JToken> ExtensionData { get; init; }
    }

    [JsonObject]
    internal sealed class YourPalsManualDefinitionDto
    {
        [JsonProperty("manualDefinitionId", Required = Required.Always)]
        public string ManualDefinitionId { get; init; }

        [JsonProperty("rawInternalName")]
        public string RawInternalName { get; init; }

        [JsonProperty("rawValues")]
        public IDictionary<string, JToken> RawValues { get; init; }

        [JsonExtensionData(ReadData = true, WriteData = true)]
        public IDictionary<string, JToken> ExtensionData { get; init; }
    }
}
