using System;
using System.Reflection;

namespace TrainOP
{
    /// <summary>
    /// Reads wagon values from station handler return values using generator-provided names and ordinals.
    /// </summary>
    public static class WagonStationReturn
    {
        /// <summary>
        /// Tries to read a member value from a typed source by name.
        /// </summary>
        public static bool TryGetMemberValue<T>(T source, string memberName, out object value)
        {
            if (source == null)
            {
                value = null;
                return false;
            }

            return TryGetMemberValue(source.GetType(), source, memberName, out value);
        }

        /// <summary>
        /// Tries to read a member value from a source object by name using the specified type.
        /// </summary>
        public static bool TryGetMemberValue(Type type, object source, string memberName, out object value)
        {
            value = null;
            if (source == null || type == null || string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;

            var property = type.GetProperty(memberName, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                value = property.GetValue(source);
                return true;
            }

            var field = type.GetField(memberName, flags);
            if (field != null)
            {
                value = field.GetValue(source);
                return true;
            }

            if (IsValueTupleType(type) && TryGetValueTupleElementByName(type, source, memberName, flags, out value))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to read a value tuple element by field or tuple element name.
        /// </summary>
        private static bool TryGetValueTupleElementByName(
            Type type,
            object source,
            string memberName,
            BindingFlags flags,
            out object value)
        {
            value = null;
            foreach (var field in type.GetFields(flags))
            {
                if (string.Equals(field.Name, memberName, StringComparison.Ordinal))
                {
                    value = field.GetValue(source);
                    return true;
                }

                var elementName = GetTupleElementName(field);
                if (elementName != null && string.Equals(elementName, memberName, StringComparison.Ordinal))
                {
                    value = field.GetValue(source);
                    return true;
                }
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                if (string.Equals(property.Name, memberName, StringComparison.Ordinal))
                {
                    value = property.GetValue(source);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads a value tuple element by generator-provided position (Item1, Item2, ...).
        /// </summary>
        public static bool TryGetTupleElement<T>(T source, int ordinal, out object value)
        {
            if (source == null)
            {
                value = null;
                return false;
            }

            return TryGetTupleElement((object)source, ordinal, out value);
        }

        /// <summary>
        /// Reads a value tuple element by generator-provided position from an untyped source.
        /// </summary>
        public static bool TryGetTupleElement(object source, int ordinal, out object value)
        {
            value = null;
            if (source == null || ordinal < 0)
            {
                return false;
            }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER
            if (source is ITuple tuple)
            {
                if (ordinal >= tuple.Length)
                {
                    return false;
                }

                value = tuple[ordinal];
                return true;
            }
#endif

            var type = source.GetType();
            if (!IsValueTupleType(type))
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var itemField = type.GetField("Item" + (ordinal + 1), flags);
            if (itemField == null)
            {
                return false;
            }

            value = itemField.GetValue(source);
            return true;
        }

        /// <summary>
        /// Checks whether the source object is a value tuple.
        /// </summary>
        public static bool IsValueTuple(object source)
        {
            return source != null && IsValueTupleType(source.GetType());
        }

        /// <summary>
        /// When tuple element names are unavailable at runtime, maps a wagon to the sole tuple
        /// element whose type matches the existing manifest value.
        /// </summary>
        public static bool TryGetUniqueTupleElementByType(object source, Type expectedType, out object value)
        {
            value = null;
            if (source == null || expectedType == null || !IsValueTuple(source))
            {
                return false;
            }

            object match = null;
            var matchCount = 0;

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER
            if (source is ITuple tuple)
            {
                for (var i = 0; i < tuple.Length; i++)
                {
                    if (!IsCompatibleType(expectedType, tuple[i]))
                    {
                        continue;
                    }

                    match = tuple[i];
                    matchCount++;
                }
            }
            else
#endif
            {
                for (var i = 0; ; i++)
                {
                    if (!TryGetTupleElement(source, i, out var element))
                    {
                        break;
                    }

                    if (!IsCompatibleType(expectedType, element))
                    {
                        continue;
                    }

                    match = element;
                    matchCount++;
                }
            }

            if (matchCount != 1)
            {
                return false;
            }

            value = match;
            return true;
        }

        /// <summary>
        /// Checks whether an element type is compatible with the expected wagon type.
        /// </summary>
        private static bool IsCompatibleType(Type expectedType, object element)
        {
            if (element == null)
            {
                return false;
            }

            return TypesCompatible(expectedType, element.GetType());
        }

        /// <summary>
        /// Checks whether the actual type can be assigned to the expected type.
        /// </summary>
        internal static bool TypesCompatible(Type expectedType, Type actualType)
        {
            if (expectedType == null || actualType == null)
            {
                return false;
            }

            return expectedType.IsAssignableFrom(actualType);
        }

        /// <summary>
        /// Reads the tuple element name from a TupleElementNameAttribute, if present.
        /// </summary>
        private static string GetTupleElementName(FieldInfo field)
        {
            foreach (var attribute in field.GetCustomAttributes(inherit: false))
            {
                var attributeType = attribute.GetType();
                if (!string.Equals(
                    attributeType.FullName,
                    "System.Runtime.CompilerServices.TupleElementNameAttribute",
                    StringComparison.Ordinal))
                {
                    continue;
                }

                var transformNameProperty = attributeType.GetProperty("TransformName", BindingFlags.Instance | BindingFlags.Public);
                return transformNameProperty?.GetValue(attribute) as string;
            }

            return null;
        }

        /// <summary>
        /// Checks whether the type is a System.ValueTuple struct.
        /// </summary>
        private static bool IsValueTupleType(Type type)
        {
            if (!type.IsValueType)
            {
                return false;
            }

            var fullName = type.FullName;
            return fullName != null
                && fullName.StartsWith("System.ValueTuple`", StringComparison.Ordinal);
        }
    }
}
