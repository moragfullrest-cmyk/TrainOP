using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Chain;

namespace TrainOP.Generators
{
    /// <summary>
    /// Joins signature groups with route-graph chain index by invocation location key.
    /// </summary>
    internal static class AttachChainContextStage
    {
        /// <summary>
        /// Attaches chain-site bindings to each group. Call once after
        /// <see cref="SignatureGroupingStage.Group"/> and <see cref="BuildChainsStage.Build"/>.
        /// </summary>
        public static void Attach(
            IEnumerable<DelegateSignatureGroup> groups,
            IReadOnlyDictionary<string, ImmutableArray<ChainSiteBinding>> chainIndex)
        {
            if (groups == null)
            {
                return;
            }

            foreach (var group in groups)
            {
                group?.AttachChainContext(chainIndex);
            }
        }
    }
}
