using System;
using System.ComponentModel;

namespace TrainOP
{
    /// <summary>
    /// Links a generated route schema type to a factory method that returns <see cref="TrainRoute"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class RouteSchemaForAttribute : Attribute
    {
        /// <summary>
        /// Creates an attribute referencing the owner type and factory method name.
        /// </summary>
        public RouteSchemaForAttribute(Type ownerType, string methodName)
        {
            OwnerType = ownerType ?? throw new ArgumentNullException(nameof(ownerType));
            if (string.IsNullOrWhiteSpace(methodName))
            {
                throw new ArgumentException("Method name cannot be empty.", nameof(methodName));
            }

            MethodName = methodName;
        }

        /// <summary>
        /// Gets the type that declares the factory method.
        /// </summary>
        public Type OwnerType { get; }

        /// <summary>
        /// Gets the factory method name.
        /// </summary>
        public string MethodName { get; }

        /// <summary>
        /// Gets or sets the caller chain key for the factory's <c>new TrainRoute()</c> site.
        /// Must match <see cref="TrainRoute.CallerChainKey"/> for extension chain-dispatch.
        /// </summary>
        public string CallerChainKey { get; set; }

        /// <summary>
        /// Gets or sets the number of Station/ServiceStation registrations performed inside the factory
        /// before return (used as the ordinal offset for consumer extension stations).
        /// </summary>
        public int StationCount { get; set; }
    }
}
