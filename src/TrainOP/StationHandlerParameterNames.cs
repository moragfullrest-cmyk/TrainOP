using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace TrainOP
{
    /// <summary>
    /// Resolves wagon parameter names from a station handler delegate via reflection.
    /// Used as chain-dispatch fallback when Roslyn interceptors are unavailable.
    /// </summary>
    public static class StationHandlerParameterNames
    {
        /// <summary>
        /// Returns wagon input names from <paramref name="handler"/> parameters,
        /// skipping CancellationToken, RedSignal, SignalIssue, and CargoManifest.
        /// </summary>
        public static string[] GetWagonInputNames(Delegate handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var parameters = handler.Method.GetParameters();
            var names = new List<string>(parameters.Length);
            for (var i = 0; i < parameters.Length; i++)
            {
                if (IsNonWagonParameter(parameters[i]))
                {
                    continue;
                }

                names.Add(parameters[i].Name ?? ("arg" + i));
            }

            return names.ToArray();
        }

        /// <summary>
        /// Returns by-ref flags for wagon parameters of <paramref name="handler"/>,
        /// skipping non-wagon parameters in the same order as <see cref="GetWagonInputNames"/>.
        /// </summary>
        public static bool[] GetWagonRefFlags(Delegate handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            var parameters = handler.Method.GetParameters();
            var flags = new List<bool>(parameters.Length);
            for (var i = 0; i < parameters.Length; i++)
            {
                if (IsNonWagonParameter(parameters[i]))
                {
                    continue;
                }

                flags.Add(parameters[i].ParameterType.IsByRef);
            }

            return flags.ToArray();
        }

        private static bool IsNonWagonParameter(ParameterInfo parameter)
        {
            var type = parameter.ParameterType;
            if (type.IsByRef)
            {
                type = type.GetElementType() ?? type;
            }

            return type == typeof(CancellationToken)
                || type == typeof(RedSignal)
                || type == typeof(SignalIssue)
                || type == typeof(CargoManifest);
        }
    }
}
