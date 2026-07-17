using System;

namespace TrainOP
{
    /// <summary>
    /// Links a generated route schema type to a factory method that returns <see cref="TrainRoute"/>.
    /// </summary>
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
    }
}
