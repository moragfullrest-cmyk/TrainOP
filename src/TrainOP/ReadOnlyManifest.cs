using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TrainOP
{
    /// <summary>
    /// Read-only snapshot of wagon keys. Values are the same references as the trip manifest.
    /// <see cref="InspectWagons"/> is a wrapper, not the live <see cref="Dictionary{TKey,TValue}"/>.
    /// </summary>
    public sealed class ReadOnlyManifest
    {
        private readonly Dictionary<string, object> _wagons;
        private readonly ReadOnlyDictionary<string, object> _view;

        /// <summary>
        /// Copies keys from <paramref name="source"/>. Reference values stay shared.
        /// </summary>
        public ReadOnlyManifest(CargoManifest source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            _wagons = new Dictionary<string, object>();
            foreach (var pair in source.InspectWagons())
            {
                _wagons[pair.Key] = pair.Value;
            }

            _view = new ReadOnlyDictionary<string, object>(_wagons);
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

            if (!_wagons.TryGetValue(wagonName, out var value))
            {
                throw new KeyNotFoundException($"Wagon '{wagonName}' was not found in the manifest.");
            }

            return CargoManifest.CastWagonValue<T>(wagonName, value);
        }

        /// <summary>
        /// Returns a read-only view that cannot be cast to <see cref="Dictionary{TKey,TValue}"/>.
        /// </summary>
        public IReadOnlyDictionary<string, object> InspectWagons()
        {
            return _view;
        }
    }
}
