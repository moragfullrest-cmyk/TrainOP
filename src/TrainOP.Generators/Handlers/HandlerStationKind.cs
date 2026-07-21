using System;

namespace TrainOP.Generators.Handlers
{
    /// <summary>
    /// Distinguishes regular Station handlers from ServiceStation recovery handlers.
    /// </summary>
    internal enum HandlerStationKind
    {
        Station,
        ServiceStation
    }

    /// <summary>
    /// TrainRoute extension method names for Station and ServiceStation handlers.
    /// </summary>
    internal static class TrainRouteMethodNames
    {
        public const string Station = nameof(HandlerStationKind.Station);
        public const string ServiceStation = nameof(HandlerStationKind.ServiceStation);
    }

    /// <summary>
    /// Converts between <see cref="HandlerStationKind"/> and route extension method names.
    /// </summary>
    internal static class HandlerStationKindExtensions
    {
        /// <summary>
        /// Returns the TrainRoute extension method name for the given kind.
        /// </summary>
        public static string ToMethodName(this HandlerStationKind kind)
        {
            return kind == HandlerStationKind.ServiceStation
                ? TrainRouteMethodNames.ServiceStation
                : TrainRouteMethodNames.Station;
        }

        /// <summary>
        /// True when the kind is <see cref="HandlerStationKind.ServiceStation"/>.
        /// </summary>
        public static bool IsServiceStation(this HandlerStationKind kind)
        {
            return kind == HandlerStationKind.ServiceStation;
        }

        /// <summary>
        /// Parses a route extension method name into a station kind.
        /// </summary>
        public static bool TryParseMethodName(string methodName, out HandlerStationKind kind)
        {
            if (string.Equals(methodName, TrainRouteMethodNames.ServiceStation, StringComparison.Ordinal))
            {
                kind = HandlerStationKind.ServiceStation;
                return true;
            }

            if (string.Equals(methodName, TrainRouteMethodNames.Station, StringComparison.Ordinal))
            {
                kind = HandlerStationKind.Station;
                return true;
            }

            kind = default;
            return false;
        }

        /// <summary>
        /// True when <paramref name="methodName"/> is <c>Station</c> or <c>ServiceStation</c>.
        /// </summary>
        public static bool IsStationOrServiceStationMethodName(string methodName)
        {
            return TryParseMethodName(methodName, out _);
        }
    }
}
