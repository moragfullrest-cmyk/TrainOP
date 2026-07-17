using System;

namespace TrainOP
{
    /// <summary>
    /// Describes a terminal wagon exported in a generated route schema.
    /// </summary>
    public readonly struct WagonSlot
    {
        /// <summary>
        /// Creates a wagon slot with a manifest key and runtime type.
        /// </summary>
        public WagonSlot(string name, Type type)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Wagon name cannot be empty.", nameof(name));
            }

            Name = name;
            Type = type ?? throw new ArgumentNullException(nameof(type));
        }

        /// <summary>
        /// Gets the wagon manifest key.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the wagon value type.
        /// </summary>
        public Type Type { get; }
    }
}
