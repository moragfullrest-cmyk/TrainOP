using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Cross-schema deduplication and metadata consolidation state for one emission pass.
    /// </summary>
    internal sealed class EmissionState
    {
        private readonly HashSet<string> _emittedSignatures;
        private readonly HashSet<string> _emittedMetadataKeys;

        /// <summary>
        /// Creates emission state for one generated extensions file.
        /// </summary>
        public EmissionState()
        {
            _emittedSignatures = new HashSet<string>(StringComparer.Ordinal);
            _emittedMetadataKeys = new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>True after <see cref="ChainBindingTypes.BindingTypeName"/> struct was emitted.</summary>
        public bool EmittedChainBindingStruct { get; set; }

        /// <summary>
        /// Registers a delegate signature group emission key; returns false when already emitted.
        /// </summary>
        public bool TryAddSignature(string emissionKey)
        {
            return _emittedSignatures.Add(emissionKey);
        }

        /// <summary>
        /// Registers a metadata consolidation key; returns false when metadata was already emitted.
        /// </summary>
        public bool TryAddMetadataKey(string metadataKey)
        {
            return _emittedMetadataKeys.Add(metadataKey);
        }

        /// <summary>
        /// Builds a stable key for shared metadata fields emitted once per delegate type id and wagon-name set.
        /// </summary>
        public static string BuildMetadataKey(MergedStationSchema merged)
        {
            return merged.DelegateTypeId + "|" + HandlerInputParameters.FormatWagonNames(merged.CanonicalBinding.Wagons);
        }

        /// <summary>
        /// Merges return-shape metadata for schemas that share the same delegate type id and wagon names.
        /// </summary>
        public static Dictionary<string, MergedStationSchema> BuildMetadataConsolidation(
            ImmutableArray<MergedStationSchema> schemas)
        {
            var result = new Dictionary<string, MergedStationSchema>(StringComparer.Ordinal);
            for (var i = 0; i < schemas.Length; i++)
            {
                var merged = schemas[i];
                if (merged.UsesChainDispatch)
                {
                    continue;
                }

                var metadataKey = BuildMetadataKey(merged);
                if (!result.TryGetValue(metadataKey, out var consolidated))
                {
                    consolidated = new MergedStationSchema(merged.CanonicalBinding, merged.DelegateTypeId);
                    consolidated.MergeFrom(merged);
                    result[metadataKey] = consolidated;
                    continue;
                }

                consolidated.MergeFrom(merged);
            }

            return result;
        }
    }
}
