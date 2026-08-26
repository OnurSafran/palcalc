using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PalCalc.UI.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PalCalc.UI.Persistence.Serialization
{
    internal sealed class YourPalsDocumentReadResult
    {
        public YourPalsDocumentReadResult(
            YourPalsDocument document,
            YourPalsRecoveryState recoveryState,
            IReadOnlyList<YourPalsDiagnostic> diagnostics)
        {
            Document = document;
            RecoveryState = recoveryState;
            Diagnostics = diagnostics;
        }

        public YourPalsDocument Document { get; }
        public YourPalsRecoveryState RecoveryState { get; }
        public IReadOnlyList<YourPalsDiagnostic> Diagnostics { get; }
    }

    // The normal serializer is deliberately strict for writes. This reader is the
    // recovery boundary: it salvages valid groups and members without ever turning
    // malformed authoritative data into an ordinary empty document.
    internal static class YourPalsDocumentRecoveryReader
    {
        private static readonly HashSet<string> DocumentProperties = new(StringComparer.Ordinal)
        {
            "documentType", "documentVersion", "ownerSaveIdentity", "groups", "manualDefinitions",
        };

        private static readonly HashSet<string> GroupProperties = new(StringComparer.Ordinal)
        {
            "groupId", "name", "order", "members",
        };

        private static readonly HashSet<string> MemberProperties = new(StringComparer.Ordinal)
        {
            "palEntryKey", "kind", "sourceIdentity", "sourceKey", "instanceId",
            "lastKnownInternalName", "lastKnownDisplayName", "manualDefinitionId",
        };

        private static readonly HashSet<string> ManualDefinitionProperties = new(StringComparer.Ordinal)
        {
            "manualDefinitionId", "rawInternalName", "rawValues",
        };

        public static YourPalsDocumentReadResult Read(JObject root, SaveIdentity expectedOwner)
        {
            var diagnostics = new List<YourPalsDiagnostic>();

            if (!TryReadRequiredString(root["documentType"], out var documentType) ||
                !string.Equals(documentType, YourPalsContract.DocumentType, StringComparison.Ordinal))
            {
                return FullRecovery(
                    YourPalsRecoveryState.CorruptReadOnly,
                    diagnostics,
                    "The Your Pals document has an unexpected or missing document type.");
            }

            if (root["ownerSaveIdentity"] is not JObject ownerObject ||
                !TryReadRequiredString(ownerObject["userId"], out var userId) ||
                !TryReadRequiredString(ownerObject["gameId"], out var gameId))
            {
                return FullRecovery(
                    YourPalsRecoveryState.CorruptReadOnly,
                    diagnostics,
                    "The Your Pals document has an invalid owner save identity.");
            }

            SaveIdentity owner;
            try
            {
                owner = SaveIdentity.Create(userId, gameId);
            }
            catch (ArgumentException ex)
            {
                return FullRecovery(
                    YourPalsRecoveryState.CorruptReadOnly,
                    diagnostics,
                    $"The Your Pals document has an invalid owner save identity: {ex.Message}");
            }

            if (owner != expectedOwner)
            {
                return new(
                    null,
                    YourPalsRecoveryState.OwnerMismatchReadOnly,
                    [new YourPalsDiagnostic(
                        YourPalsDiagnosticCode.DocumentOwnerMismatch,
                        YourPalsDiagnosticSeverity.Error,
                        "The Your Pals document belongs to a different save and was opened read-only.")]);
            }

            var partial = false;
            var unrecoverable = false;
            var groups = ReadGroups(root["groups"], diagnostics, ref partial, ref unrecoverable);
            var manualDefinitions = ReadManualDefinitions(root["manualDefinitions"], diagnostics, ref partial, ref unrecoverable);

            var document = new YourPalsDocument
            {
                OwnerSaveIdentity = owner,
                Groups = groups,
                ManualDefinitions = manualDefinitions,
                ExtensionData = ReadExtensionData(root, DocumentProperties),
                HasUnrecoverableRecoveryData = unrecoverable,
            };

            var state = partial
                ? YourPalsRecoveryState.PartiallyRecoveredReadOnly
                : YourPalsRecoveryState.Healthy;

            return new(document, state, diagnostics.AsReadOnly());
        }

        private static List<YourPalsGroup> ReadGroups(
            JToken token,
            List<YourPalsDiagnostic> diagnostics,
            ref bool partial,
            ref bool unrecoverable)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                partial = true;
                unrecoverable = true;
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.MalformedGroup,
                    "The Your Pals document has no groups array; available data was recovered.");
                return [];
            }

            if (token is not JArray array)
            {
                partial = true;
                unrecoverable = true;
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.MalformedGroup,
                    "The Your Pals groups value is not an array; available data was recovered.");
                return [];
            }

            var groups = new List<YourPalsGroup>();
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is not JObject groupObject ||
                    !TryReadRequiredString(groupObject["groupId"], out var groupId) ||
                    !TryReadString(groupObject["name"], out var name) ||
                    !TryReadRequiredInt(groupObject["order"], out var order))
                {
                    partial = true;
                    unrecoverable = true;
                    AddDiagnostic(
                        diagnostics,
                        YourPalsDiagnosticCode.MalformedGroup,
                        $"Group {index} could not be recovered and was kept out of the writable projection.");
                    continue;
                }

                var members = ReadMembers(
                    groupObject["members"],
                    groupId,
                    diagnostics,
                    ref partial,
                    ref unrecoverable);

                groups.Add(new YourPalsGroup
                {
                    GroupId = groupId,
                    Name = name,
                    Order = order,
                    Members = members,
                    ExtensionData = ReadExtensionData(groupObject, GroupProperties),
                });
            }

            foreach (var duplicate in groups
                .GroupBy(group => group.GroupId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1))
            {
                partial = true;
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.DuplicateGroupId,
                    $"The group ID '{duplicate.Key}' occurs more than once and requires repair.",
                    groupId: duplicate.Key);
            }

            var duplicateMembers = groups
                .SelectMany(group => (group.Members ?? []).Select(member => (Group: group, Member: member)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Member?.PalEntryKey))
                .GroupBy(item => item.Member.PalEntryKey, StringComparer.Ordinal)
                .Where(group => group.Count() > 1);
            foreach (var duplicate in duplicateMembers)
            {
                partial = true;
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.DuplicateMemberKey,
                    $"The member key '{duplicate.Key}' occurs more than once and requires repair.",
                    palEntryKey: duplicate.Key);
            }

            return groups;
        }

        private static List<YourPalsMember> ReadMembers(
            JToken token,
            string groupId,
            List<YourPalsDiagnostic> diagnostics,
            ref bool partial,
            ref bool unrecoverable)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                partial = true;
                unrecoverable = true;
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.MalformedMember,
                    "The group has no members array; available group data was recovered.",
                    groupId: groupId);
                return [];
            }

            if (token is not JArray array)
            {
                partial = true;
                unrecoverable = true;
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.MalformedMember,
                    "The group members value is not an array; available group data was recovered.",
                    groupId: groupId);
                return [];
            }

            var members = new List<YourPalsMember>();
            for (var index = 0; index < array.Count; index++)
            {
                var raw = array[index];
                var member = ReadMember(raw, groupId, index, diagnostics, ref partial);
                if (member != null)
                    members.Add(member);
            }

            return members;
        }

        private static YourPalsMember ReadMember(
            JToken raw,
            string groupId,
            int index,
            List<YourPalsDiagnostic> diagnostics,
            ref bool partial)
        {
            var memberObject = raw as JObject;
            var malformed = memberObject == null;
            string palEntryKey = null;
            string kind = null;

            if (memberObject != null)
            {
                if (!TryReadRequiredString(memberObject["palEntryKey"], out palEntryKey))
                {
                    malformed = true;
                    palEntryKey = RecoveryKey("member-key", groupId, index, raw);
                }

                if (!TryReadRequiredString(memberObject["kind"], out kind))
                {
                    malformed = true;
                    kind = "recovery-invalid";
                }
            }
            else
            {
                palEntryKey = RecoveryKey("member-key", groupId, index, raw);
                kind = "recovery-invalid";
            }

            var extensionData = ReadExtensionData(memberObject, MemberProperties);
            if (malformed)
            {
                partial = true;
                extensionData["_recoveryRawMember"] = raw?.DeepClone() ?? JValue.CreateNull();
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.InvalidMember,
                    $"Member {index} in group '{groupId}' was malformed and retained as recovery data.",
                    groupId,
                    palEntryKey);
            }

            var sourceIdentity = ReadSourceIdentity(
                memberObject?["sourceIdentity"],
                extensionData,
                groupId,
                palEntryKey,
                diagnostics,
                ref partial);

            var member = new YourPalsMember
            {
                PalEntryKey = palEntryKey,
                Kind = kind,
                SourceIdentity = sourceIdentity,
                SourceKey = ReadOptionalString(memberObject?["sourceKey"], "sourceKey", groupId, palEntryKey, diagnostics, extensionData, ref partial),
                InstanceId = ReadOptionalString(memberObject?["instanceId"], "instanceId", groupId, palEntryKey, diagnostics, extensionData, ref partial),
                LastKnownInternalName = ReadOptionalString(memberObject?["lastKnownInternalName"], "lastKnownInternalName", groupId, palEntryKey, diagnostics, extensionData, ref partial),
                LastKnownDisplayName = ReadOptionalString(memberObject?["lastKnownDisplayName"], "lastKnownDisplayName", groupId, palEntryKey, diagnostics, extensionData, ref partial),
                ManualDefinitionId = ReadOptionalString(memberObject?["manualDefinitionId"], "manualDefinitionId", groupId, palEntryKey, diagnostics, extensionData, ref partial),
                ExtensionData = extensionData,
            };

            if (member.KnownKind == YourPalsMemberKind.Unknown && !malformed)
            {
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.UnknownMemberKind,
                    $"Member '{palEntryKey}' uses the unknown kind '{kind}' and remains unresolved.",
                    groupId,
                    palEntryKey,
                    YourPalsDiagnosticSeverity.Info);
            }

            return member;
        }

        private static SourceIdentity? ReadSourceIdentity(
            JToken token,
            IDictionary<string, JToken> extensionData,
            string groupId,
            string palEntryKey,
            List<YourPalsDiagnostic> diagnostics,
            ref bool partial)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token is not JObject sourceObject ||
                !TryReadRequiredString(sourceObject["kind"], out var kind) ||
                !TryReadRequiredString(sourceObject["scope"], out var scope))
            {
                partial = true;
                extensionData["_recoveryRawSourceIdentity"] = token.DeepClone();
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.InvalidMember,
                    $"Member '{palEntryKey}' has an invalid source identity and remains unresolved.",
                    groupId,
                    palEntryKey);
                return null;
            }

            YourPalsSourceKind sourceKind;
            switch (kind)
            {
                case "save":
                    sourceKind = YourPalsSourceKind.Save;
                    break;
                case "global-pal-storage":
                    sourceKind = YourPalsSourceKind.GlobalPalStorage;
                    break;
                default:
                    partial = true;
                    extensionData["_recoveryRawSourceIdentity"] = token.DeepClone();
                    AddDiagnostic(
                        diagnostics,
                        YourPalsDiagnosticCode.InvalidMember,
                        $"Member '{palEntryKey}' uses an unknown source identity kind '{kind}'.",
                        groupId,
                        palEntryKey);
                    return null;
            }

            var unknownFields = ReadExtensionData(
                sourceObject,
                new HashSet<string>(StringComparer.Ordinal) { "kind", "scope" });
            if (unknownFields.Count > 0)
            {
                extensionData[YourPalsContract.SourceIdentityExtensionDataKey] = new JObject(
                    unknownFields.Select(pair => new JProperty(pair.Key, pair.Value)));
            }

            return new SourceIdentity(sourceKind, scope);
        }

        private static List<YourPalsManualDefinition> ReadManualDefinitions(
            JToken token,
            List<YourPalsDiagnostic> diagnostics,
            ref bool partial,
            ref bool unrecoverable)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                partial = true;
                unrecoverable = true;
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.MalformedManualDefinition,
                    "The Your Pals document has no manualDefinitions array; available data was recovered.");
                return [];
            }

            if (token is not JArray array)
            {
                partial = true;
                unrecoverable = true;
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.MalformedManualDefinition,
                    "The Your Pals manualDefinitions value is not an array; available data was recovered.");
                return [];
            }

            var definitions = new List<YourPalsManualDefinition>();
            for (var index = 0; index < array.Count; index++)
            {
                if (array[index] is not JObject definitionObject ||
                    !TryReadRequiredString(definitionObject["manualDefinitionId"], out var definitionId))
                {
                    partial = true;
                    unrecoverable = true;
                    AddDiagnostic(
                        diagnostics,
                        YourPalsDiagnosticCode.MalformedManualDefinition,
                        $"Manual definition {index} could not be recovered.");
                    continue;
                }

                var extensionData = ReadExtensionData(definitionObject, ManualDefinitionProperties);
                var rawInternalName = ReadOptionalString(
                    definitionObject["rawInternalName"],
                    "rawInternalName",
                    null,
                    definitionId,
                    diagnostics,
                    extensionData,
                    ref partial);

                IDictionary<string, JToken> rawValues;
                if (definitionObject["rawValues"] == null || definitionObject["rawValues"].Type == JTokenType.Null)
                {
                    rawValues = new Dictionary<string, JToken>();
                }
                else if (definitionObject["rawValues"] is JObject rawValuesObject)
                {
                    rawValues = ReadExtensionData(rawValuesObject, new HashSet<string>(StringComparer.Ordinal));
                }
                else
                {
                    partial = true;
                    rawValues = new Dictionary<string, JToken>();
                    AddRecoveryRawField(
                        extensionData,
                        "rawValues",
                        definitionObject["rawValues"]);
                    AddDiagnostic(
                        diagnostics,
                        YourPalsDiagnosticCode.MalformedManualDefinition,
                        $"Manual definition '{definitionId}' has invalid raw values.",
                        palEntryKey: definitionId);
                }

                definitions.Add(new YourPalsManualDefinition
                {
                    ManualDefinitionId = definitionId,
                    RawInternalName = rawInternalName,
                    RawValues = rawValues,
                    ExtensionData = extensionData,
                });
            }

            foreach (var duplicate in definitions
                .GroupBy(definition => definition.ManualDefinitionId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1))
            {
                partial = true;
                AddDiagnostic(
                    diagnostics,
                    YourPalsDiagnosticCode.DuplicateManualDefinitionId,
                    $"The manual definition ID '{duplicate.Key}' occurs more than once and requires repair.",
                    palEntryKey: duplicate.Key);
            }

            return definitions;
        }

        private static string ReadOptionalString(
            JToken token,
            string propertyName,
            string groupId,
            string palEntryKey,
            List<YourPalsDiagnostic> diagnostics,
            IDictionary<string, JToken> extensionData,
            ref bool partial)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (TryReadString(token, out var value))
                return value;

            partial = true;
            AddRecoveryRawField(extensionData, propertyName, token);
            AddDiagnostic(
                diagnostics,
                YourPalsDiagnosticCode.InvalidMember,
                $"The '{propertyName}' value for '{palEntryKey}' is invalid and was retained in recovery data.",
                groupId,
                palEntryKey);
            return null;
        }

        private static void AddRecoveryRawField(
            IDictionary<string, JToken> extensionData,
            string propertyName,
            JToken token)
        {
            if (extensionData == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            if (extensionData.TryGetValue(
                    YourPalsContract.RecoveryRawFieldsExtensionDataKey,
                    out var existing) && existing is JObject existingFields)
            {
                existingFields[propertyName] = token?.DeepClone() ?? JValue.CreateNull();
                return;
            }

            extensionData[YourPalsContract.RecoveryRawFieldsExtensionDataKey] = new JObject(
                new JProperty(propertyName, token?.DeepClone() ?? JValue.CreateNull()));
        }

        private static IDictionary<string, JToken> ReadExtensionData(
            JObject objectValue,
            ISet<string> knownProperties)
        {
            var result = new Dictionary<string, JToken>(StringComparer.Ordinal);
            if (objectValue == null)
                return result;

            foreach (var property in objectValue.Properties())
            {
                if (!knownProperties.Contains(property.Name))
                    result[property.Name] = property.Value?.DeepClone();
            }

            return result;
        }

        private static bool TryReadRequiredString(JToken token, out string value) =>
            TryReadString(token, out value) && !string.IsNullOrWhiteSpace(value);

        private static bool TryReadString(JToken token, out string value)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                value = null;
                return false;
            }

            if (token.Type != JTokenType.String)
            {
                value = null;
                return false;
            }

            value = token.Value<string>();
            return true;
        }

        private static bool TryReadRequiredInt(JToken token, out int value)
        {
            if (token?.Type == JTokenType.Integer)
                return int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

            value = default;
            return false;
        }

        private static void AddDiagnostic(
            List<YourPalsDiagnostic> diagnostics,
            YourPalsDiagnosticCode code,
            string message,
            string groupId = null,
            string palEntryKey = null,
            YourPalsDiagnosticSeverity severity = YourPalsDiagnosticSeverity.Error) =>
            diagnostics.Add(new(code, severity, message, groupId, palEntryKey));

        private static YourPalsDocumentReadResult FullRecovery(
            YourPalsRecoveryState state,
            List<YourPalsDiagnostic> diagnostics,
            string message)
        {
            AddDiagnostic(diagnostics, YourPalsDiagnosticCode.DocumentCorrupt, message);
            return new(null, state, diagnostics.AsReadOnly());
        }

        private static string RecoveryKey(string prefix, string groupId, int index, JToken raw)
        {
            var payload = $"{prefix}\0{groupId}\0{index}\0{raw?.ToString(Formatting.None) ?? "null"}";
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return $"recovery:{Convert.ToHexString(hash).ToLowerInvariant()}";
        }
    }
}
