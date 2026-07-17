using System;

namespace TrainOP
{
    /// <summary>
    /// Declares one terminal wagon on a generated route schema type (metadata-readable across assemblies).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class RouteSchemaWagonAttribute : Attribute
    {
        /// <summary>
        /// Creates a wagon export entry for schema lookup.
        /// </summary>
        public RouteSchemaWagonAttribute(string name, Type type)
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
