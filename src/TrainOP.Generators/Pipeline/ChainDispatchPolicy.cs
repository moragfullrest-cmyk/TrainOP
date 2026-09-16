using System;
using System.Collections.Generic;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Single owner of canonical vs chain-aware dispatch decisions (TOP007 path + emit).
    /// </summary>
    /// <remarks>
    /// Grouping attach (<see cref="RequiresChainDispatch"/>) uses entry wagon-name sets;
    /// emit (<see cref="UsesChainDispatch"/>) uses chain-binding wagon-name sets and excludes service stations.
    /// Both share <see cref="NeedsChainDispatchCore"/>.
    /// </remarks>
    internal static class ChainDispatchPolicy
    {
        /// <summary>
        /// Whether a signature group should attach chain bindings and use the non-chain TOP007 reporter.
        /// </summary>
        public static bool RequiresChainDispatch(
            IReadOnlyList<ChainSiteBinding> chainBindings,
            IReadOnlyList<ReturnShape> returnShapes,
            IReadOnlyList<string> entryWagonNameKeys)
        {
            if (chainBindings == null || chainBindings.Count == 0)
            {
                return false;
            }

            return NeedsChainDispatchCore(returnShapes, entryWagonNameKeys);
        }

        /// <summary>
        /// Whether emit should use chain-aware Station extensions (caller dispatch tables).
        /// </summary>
        public static bool UsesChainDispatch(
            IReadOnlyList<ChainSiteBinding> chainBindings,
            IReadOnlyList<ReturnShape> returnShapes,
            bool isServiceStation)
        {
            if (isServiceStation || chainBindings == null || chainBindings.Count == 0)
            {
                return false;
            }

            var wagonNameKeys = new string[chainBindings.Count];
            for (var i = 0; i < chainBindings.Count; i++)
            {
                wagonNameKeys[i] = HandlerInputParameters.FormatWagonNames(chainBindings[i].Schema.Wagons);
            }

            return NeedsChainDispatchCore(returnShapes, wagonNameKeys);
        }

        private static bool NeedsChainDispatchCore(
            IReadOnlyList<ReturnShape> returnShapes,
            IReadOnlyList<string> wagonNameKeys)
        {
            // Anonymous / object returns consolidate into one ReturnMembers_* list.
            // Named vs default-ItemN (and other typed shape splits) need per-site metadata.
            if (HandlerOutputParameters.RequiresPerSiteReturnMetadata(returnShapes))
            {
                return true;
            }

            if (wagonNameKeys == null || wagonNameKeys.Count == 0)
            {
                return false;
            }

            var wagonNameSets = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < wagonNameKeys.Count; i++)
            {
                wagonNameSets.Add(wagonNameKeys[i] ?? string.Empty);
            }

            return wagonNameSets.Count > 1;
        }
    }
}
