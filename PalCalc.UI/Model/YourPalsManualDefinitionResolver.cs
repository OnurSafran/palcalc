using Newtonsoft.Json.Linq;
using PalCalc.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PalCalc.UI.Model
{
    internal static class YourPalsManualDefinitionResolver
    {
        private static readonly Lazy<PalDB> database = new(PalDB.LoadEmbedded);

        public static bool TryResolve(
            YourPalsManualDefinition definition,
            out PalInstance record,
            out string reason)
        {
            record = null;
            reason = null;

            if (definition == null || string.IsNullOrWhiteSpace(definition.ManualDefinitionId))
            {
                reason = "The manual definition has no stable ID.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.RawInternalName))
            {
                reason = "The manual definition has no Pal internal name.";
                return false;
            }

            var pal = database.Value.Pals.FirstOrDefault(candidate =>
                string.Equals(candidate.InternalName, definition.RawInternalName, StringComparison.OrdinalIgnoreCase));
            if (pal == null)
            {
                reason = $"The Pal internal name '{definition.RawInternalName}' is not known to the current catalog.";
                return false;
            }

            var values = definition.RawValues ?? new Dictionary<string, JToken>();
            if (!TryReadEnum(values, "gender", PalGender.MALE, out PalGender gender, out reason) ||
                !TryReadInt(values, "level", 1, out var level, out reason) ||
                !TryReadInt(values, "rank", 1, out var rank, out reason) ||
                !TryReadInt(values, "ivHp", 0, out var ivHp, out reason) ||
                !TryReadInt(values, "ivAttack", 0, out var ivAttack, out reason) ||
                !TryReadInt(values, "ivDefense", 0, out var ivDefense, out reason) ||
                !TryReadInt(values, "ivMelee", 0, out var ivMelee, out reason) ||
                !TryReadString(values, out var ownerPlayerId, out reason, "ownerPlayerId") ||
                !TryReadBool(values, "isOnExpedition", false, out var isOnExpedition, out reason) ||
                !TryReadStringList(values, out var passiveNames, out reason, "passiveSkills", "passives", "traits") ||
                !TryReadStringList(values, out var activeNames, out reason, "activeSkills", "active") ||
                !TryReadStringList(values, out var equippedActiveNames, out reason, "equippedActiveSkills", "equippedActives"))
            {
                return false;
            }

            var passiveSkills = passiveNames
                .Select(name => name.InternalToStandardPassive(database.Value))
                .ToList();
            if (passiveSkills.Any(skill => skill is UnrecognizedPassiveSkill))
            {
                reason = "The manual definition contains an unknown passive skill.";
                return false;
            }

            var activeSkills = activeNames
                .Select(name => name.ToActive(database.Value))
                .ToList();
            var equippedActiveSkills = equippedActiveNames
                .Select(name => name.ToActive(database.Value))
                .ToList();
            if (activeSkills.Any(skill => skill is UnrecognizedActiveSkill) ||
                equippedActiveSkills.Any(skill => skill is UnrecognizedActiveSkill))
            {
                reason = "The manual definition contains an unknown active skill.";
                return false;
            }

            if (gender != PalGender.MALE && gender != PalGender.FEMALE)
            {
                reason = "A manual Pal must have a usable male or female gender.";
                return false;
            }

            record = new PalInstance
            {
                InstanceId = $"manual:{definition.ManualDefinitionId}",
                NickName = ReadString(values, "nickname"),
                Level = Math.Max(1, level),
                OwnerPlayerId = ownerPlayerId,
                Pal = pal,
                Location = new PalLocation
                {
                    Type = LocationType.Custom,
                    ContainerId = $"manual:{definition.ManualDefinitionId}",
                },
                Gender = gender,
                Rank = Math.Clamp(rank, 1, 5),
                IV_HP = Math.Clamp(ivHp, 0, 100),
                IV_Shot = Math.Clamp(ivAttack, 0, 100),
                IV_Defense = Math.Clamp(ivDefense, 0, 100),
                IV_Melee = Math.Clamp(ivMelee, 0, 100),
                IsOnExpedition = isOnExpedition,
                PassiveSkills = passiveSkills,
                ActiveSkills = activeSkills,
                EquippedActiveSkills = equippedActiveSkills,
            };
            return true;
        }

        private static string ReadString(IDictionary<string, JToken> values, params string[] keys) =>
            FindToken(values, keys)?.Type == JTokenType.String
                ? FindToken(values, keys).Value<string>()
                : null;

        private static bool TryReadString(
            IDictionary<string, JToken> values,
            out string value,
            out string reason,
            params string[] keys)
        {
            value = null;
            reason = null;
            var token = FindToken(values, keys);
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type == JTokenType.String)
            {
                value = token.Value<string>();
                return true;
            }

            reason = $"The manual field '{keys[0]}' is not a valid string.";
            return false;
        }

        private static bool TryReadInt(
            IDictionary<string, JToken> values,
            string key,
            int defaultValue,
            out int value,
            out string reason)
        {
            value = defaultValue;
            reason = null;
            var token = FindToken(values, key);
            if (token == null)
            {
                token = key switch
                {
                    "ivHp" => FindToken(values, "IV_HP"),
                    "ivAttack" => FindToken(values, "IV_Shot", "ivShot"),
                    "ivDefense" => FindToken(values, "IV_Defense"),
                    "ivMelee" => FindToken(values, "IV_Melee"),
                    _ => null,
                };
            }
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type == JTokenType.Integer &&
                int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                value = integer;
                return true;
            }

            if (token.Type == JTokenType.String &&
                int.TryParse(token.Value<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            reason = $"The manual field '{key}' is not a valid integer.";
            return false;
        }

        private static bool TryReadEnum<T>(
            IDictionary<string, JToken> values,
            string key,
            T defaultValue,
            out T value,
            out string reason)
            where T : struct, Enum
        {
            value = defaultValue;
            reason = null;
            var token = FindToken(values, key);
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type == JTokenType.String)
            {
                var text = token.Value<string>();
                if (Enum.GetNames<T>().Any(name =>
                        string.Equals(name, text, StringComparison.OrdinalIgnoreCase)) &&
                    Enum.TryParse(text, ignoreCase: true, out value) &&
                    Enum.IsDefined(value))
                    return true;
            }

            reason = $"The manual field '{key}' is not a valid {typeof(T).Name}.";
            return false;
        }

        private static bool TryReadBool(
            IDictionary<string, JToken> values,
            string key,
            bool defaultValue,
            out bool value,
            out string reason)
        {
            value = defaultValue;
            reason = null;
            var token = FindToken(values, key);
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type == JTokenType.Boolean)
            {
                value = token.Value<bool>();
                return true;
            }

            if (token.Type == JTokenType.String &&
                bool.TryParse(token.Value<string>(), out value))
                return true;

            reason = $"The manual field '{key}' is not a valid boolean.";
            return false;
        }

        private static bool TryReadStringList(
            IDictionary<string, JToken> values,
            out List<string> result,
            out string reason,
            params string[] keys)
        {
            result = [];
            reason = null;
            var token = FindToken(values, keys);
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type == JTokenType.String)
            {
                result.Add(token.Value<string>());
                return true;
            }

            if (token is not JArray array || array.Any(item => item?.Type != JTokenType.String))
            {
                reason = $"The manual field '{keys[0]}' is not a valid string list.";
                return false;
            }

            result = array.Select(item => item.Value<string>()).ToList();
            return true;
        }

        private static JToken FindToken(IDictionary<string, JToken> values, params string[] keys)
        {
            if (values == null || keys == null)
                return null;

            foreach (var key in keys)
            {
                var pair = values.FirstOrDefault(candidate =>
                    string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(pair.Key))
                    return pair.Value;
            }

            return null;
        }
    }
}
