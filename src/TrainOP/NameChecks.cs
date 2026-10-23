using System;

namespace TrainOP
{
    internal static class NameChecks
    {
        internal static void RequireWagonName(string wagonName, string paramName)
        {
            if (string.IsNullOrWhiteSpace(wagonName))
            {
                throw new ArgumentException("Wagon name cannot be empty.", paramName);
            }
        }

        internal static void RequireStationName(string stationName, string paramName)
        {
            if (string.IsNullOrWhiteSpace(stationName))
            {
                throw new ArgumentException("Station name cannot be empty.", paramName);
            }
        }
    }
}
