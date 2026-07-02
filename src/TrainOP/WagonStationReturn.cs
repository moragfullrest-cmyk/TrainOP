using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace TrainOP
{
    /// <summary>
    /// Reads wagon values from station handler return values (anonymous types, tuples, and plain objects).
    /// </summary>
    public static class WagonStationReturn
    {
        public static bool TryGetMemberValue<T>(T source, string memberName, out object value)
        {
            if (source == null)
            {
                value = null;
                return false;
            }

            return TryGetMemberValue(typeof(T), source, memberName, out value);
        }

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

            return TryGetValueTupleMemberValue(source, type, memberName, flags, out value);
        }

        /// <summary>
        /// Reads a value tuple element by position (Item1, Item2, ...).
        /// Tuple element names are compile-time only; use this when names are unavailable at runtime.
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

        public static bool TryGetTupleElement(object source, int ordinal, out object value)
        {
            value = null;
            if (source == null || ordinal < 0)
            {
                return false;
            }

            var type = source.GetType();
            if (!IsValueTupleType(type))
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

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var itemField = type.GetField("Item" + (ordinal + 1), flags);
            if (itemField == null)
            {
                return false;
            }

            value = itemField.GetValue(source);
            return true;
        }

        public static bool TryGetTupleElementMatchingManifestWagon(
            object source,
            CargoManifest manifest,
            string wagonName,
            ISet<int> usedOrdinals,
            out object value)
        {
            value = null;
            if (source == null || manifest == null || string.IsNullOrWhiteSpace(wagonName) || !manifest.HasCar(wagonName))
            {
                return false;
            }

            if (!IsValueTupleType(source.GetType()))
            {
                return false;
            }

            var expectedType = manifest.InspectCars()[wagonName].GetType();
            if (!TryGetTupleLength(source, out var length))
            {
                return false;
            }

            var matches = new List<int>();
            for (var ordinal = 0; ordinal < length; ordinal++)
            {
                if (usedOrdinals != null && usedOrdinals.Contains(ordinal))
                {
                    continue;
                }

                if (!TryGetTupleElement(source, ordinal, out var candidate) || candidate == null)
                {
                    continue;
                }

                if (expectedType.IsInstanceOfType(candidate))
                {
                    matches.Add(ordinal);
                }
            }

            if (matches.Count != 1)
            {
                return false;
            }

            var selected = matches[0];
            if (!TryGetTupleElement(source, selected, out value))
            {
                return false;
            }

            usedOrdinals?.Add(selected);
            return true;
        }

        private static bool TryGetTupleLength(object source, out int length)
        {
            length = 0;
            if (source == null)
            {
                return false;
            }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_0_OR_GREATER
            if (source is ITuple tuple)
            {
                length = tuple.Length;
                return true;
            }
#endif

            var type = source.GetType();
            if (!IsValueTupleType(type))
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            for (var i = 1; i <= 16; i++)
            {
                if (type.GetField("Item" + i, flags) == null)
                {
                    length = i - 1;
                    return length > 0;
                }
            }

            return false;
        }

        private static bool TryGetValueTupleMemberValue(
            object source,
            Type type,
            string memberName,
            BindingFlags flags,
            out object value)
        {
            value = null;
            var names = GetTupleElementNames(type);
            if (names == null)
            {
                return false;
            }

            for (var i = 0; i < names.Count; i++)
            {
                if (!string.Equals(names[i], memberName, StringComparison.Ordinal))
                {
                    continue;
                }

                var itemField = type.GetField("Item" + (i + 1), flags);
                if (itemField == null)
                {
                    return false;
                }

                value = itemField.GetValue(source);
                return true;
            }

            return false;
        }

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

        private static IList<string> GetTupleElementNames(Type type)
        {
            return type.GetCustomAttribute<TupleElementNamesAttribute>()?.TransformNames;
        }
    }
}
