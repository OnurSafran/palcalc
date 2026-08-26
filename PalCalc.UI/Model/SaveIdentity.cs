using PalCalc.SaveReader;
using System;
using System.Text;

namespace PalCalc.UI.Model
{
    internal readonly record struct SaveIdentity(string UserId, string GameId)
    {
        public static SaveIdentity Create(string userId, string gameId) => new(
            CanonicalizePart(userId, nameof(userId)),
            CanonicalizePart(gameId, nameof(gameId)));

        public static SaveIdentity From(ISaveGame save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));

            return Create(save.UserId, save.GameId);
        }

        // Save IDs are identifiers, not display labels. Preserve their value and case;
        // compare and serialize them with ordinal semantics everywhere they are persisted.
        public string CanonicalKey => $"{UserId.Length}:{UserId}{GameId.Length}:{GameId}";

        // Hex-encode the injective canonical key so it is safe and collision-free as a
        // filename on every supported filesystem.
        public string StorageKey => Convert.ToHexString(Encoding.UTF8.GetBytes(CanonicalKey));

        private static string CanonicalizePart(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Save identity parts must be non-empty.", name);

            return value;
        }
    }
}
