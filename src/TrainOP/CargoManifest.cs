using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace TrainOP
{
    /// <summary>
    /// Mutable shared storage for wagon values between route stations.
    /// </summary>
    public sealed class CargoManifest
    {
        private readonly Dictionary<string, object> _wagons;

        /// <summary>
        /// Creates an empty cargo manifest.
        /// </summary>
        public CargoManifest()
        {
            _wagons = new Dictionary<string, object>();
        }

        /// <summary>
        /// Checks whether a wagon with the specified name exists.
        /// </summary>
        public bool HasWagon(string wagonName)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            return HasWagonUnchecked(wagonName);
        }

        /// <summary>
        /// Checks whether a wagon exists. Names from generated adapters are already non-empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool HasWagonUnchecked(string wagonName)
        {
            return _wagons.ContainsKey(wagonName);
        }

        /// <summary>
        /// Tries to read a wagon value by name without throwing when the wagon is missing.
        /// </summary>
        public bool TryGetWagon(string wagonName, out object cargo)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            return TryGetWagonUnchecked(wagonName, out cargo);
        }

        /// <summary>
        /// Tries to read a wagon value. Names from generated adapters are already non-empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool TryGetWagonUnchecked(string wagonName, out object cargo)
        {
            return _wagons.TryGetValue(wagonName, out cargo);
        }

        /// <summary>
        /// Reads a typed wagon value by name.
        /// </summary>
        public T PullWagon<T>(string wagonName)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            return PullWagonUnchecked<T>(wagonName);
        }

        /// <summary>
        /// Reads a typed wagon value. Names from generated adapters are already non-empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public T PullWagonUnchecked<T>(string wagonName)
        {
            if (!TryGetWagonUnchecked(wagonName, out var value))
            {
                throw new KeyNotFoundException($"Wagon '{wagonName}' was not found in the manifest.");
            }

            return CastWagonValue<T>(wagonName, value);
        }

        /// <summary>
        /// Casts a stored wagon value to <typeparamref name="T"/>, allowing null when <typeparamref name="T"/> is null-compatible.
        /// </summary>
        internal static T CastWagonValue<T>(string wagonName, object value)
        {
            if (value == null)
            {
                if (default(T) == null)
                {
                    return default;
                }

                throw new InvalidCastException(
                    $"Wagon '{wagonName}' contains null, cannot cast to '{typeof(T).FullName}'.");
            }

            if (!(value is T typed))
            {
                throw new InvalidCastException(
                    $"Wagon '{wagonName}' contains '{value.GetType().FullName}', cannot cast to '{typeof(T).FullName}'.");
            }

            return typed;
        }

        /// <summary>
        /// Adds or replaces a wagon value in place and returns this manifest.
        /// </summary>
        public CargoManifest LoadWagon(string wagonName, object cargo)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            return LoadWagonUnchecked(wagonName, cargo);
        }

        /// <summary>
        /// Adds or replaces a wagon value. Names from generated adapters are already non-empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public CargoManifest LoadWagonUnchecked(string wagonName, object cargo)
        {
            _wagons[wagonName] = cargo;
            return this;
        }

        /// <summary>
        /// Removes a wagon by name in place and returns this manifest.
        /// </summary>
        public CargoManifest UnloadWagon(string wagonName)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(wagonName));
            }

            return UnloadWagonUnchecked(wagonName);
        }

        /// <summary>
        /// Removes a wagon by name. Names from generated adapters are already non-empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public CargoManifest UnloadWagonUnchecked(string wagonName)
        {
            _wagons.Remove(wagonName);
            return this;
        }

        /// <summary>
        /// Returns a read-only view of current wagon values (live; not a copy).
        /// </summary>
        public IReadOnlyDictionary<string, object> InspectWagons()
        {
            return _wagons;
        }

        /// <summary>
        /// Replaces all wagon entries with those from <paramref name="source"/> (same instance is a no-op).
        /// </summary>
        internal void ReplaceWith(CargoManifest source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (ReferenceEquals(source, this))
            {
                return;
            }

            _wagons.Clear();
            foreach (var pair in source._wagons)
            {
                _wagons[pair.Key] = pair.Value;
            }
        }
    }
}
