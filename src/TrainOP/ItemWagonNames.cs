using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace TrainOP
{
    /// <summary>
    /// Allocates sequential <c>ItemN</c> wagon keys on a manifest (parity with default value-tuple merge).
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ItemWagonNames
    {
        /// <summary>
        /// Prefix for sequential default tuple wagon keys (<c>Item1</c>, <c>Item2</c>, …).
        /// </summary>
        public const string ItemPrefix = "Item";

        /// <summary>
        /// Returns the highest <c>ItemN</c> index among live wagon keys, or <c>0</c> when none exist.
        /// </summary>
        public static int GetMaxItemIndex(CargoManifest manifest)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var max = 0;
            foreach (var key in manifest.InspectWagons().Keys)
            {
                if (TryParseItemIndex(key, out var index) && index > max)
                {
                    max = index;
                }
            }

            return max;
        }

        /// <summary>
        /// Returns the next free <c>ItemN</c> name after the current max index on the manifest.
        /// </summary>
        public static string NextItemName(CargoManifest manifest)
        {
            return ItemPrefix + (GetMaxItemIndex(manifest) + 1);
        }

        /// <summary>
        /// Loads <paramref name="value"/> under the next allocated <c>ItemN</c> key.
        /// </summary>
        public static CargoManifest LoadNextItemWagon(CargoManifest manifest, object value)
        {
            return manifest.LoadWagon(NextItemName(manifest), value);
        }

        /// <summary>
        /// True when <paramref name="name"/> is exactly <c>Item</c> followed by a positive integer.
        /// </summary>
        public static bool TryParseItemIndex(string name, out int index)
        {
            index = 0;
            if (string.IsNullOrEmpty(name) || !name.StartsWith(ItemPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            var suffix = name.Substring(ItemPrefix.Length);
            if (suffix.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < suffix.Length; i++)
            {
                if (suffix[i] < '0' || suffix[i] > '9')
                {
                    return false;
                }
            }

            if (!int.TryParse(suffix, out index) || index < 1)
            {
                index = 0;
                return false;
            }

            return true;
        }
    }
}
