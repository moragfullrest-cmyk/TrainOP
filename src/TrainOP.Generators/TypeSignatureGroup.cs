using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Accumulates handler bindings that share the same delegate type signature.
    /// </summary>
    internal sealed class TypeSignatureGroup
    {
        private readonly DelegateTypeSignature _typeSignature;
        private readonly List<ReturnShape> _returnShapes = new List<ReturnShape>();
        private readonly List<ChainSiteBinding> _chainBindings = new List<ChainSiteBinding>();
        private readonly List<StationEntry> _entries = new List<StationEntry>();
        private StationHandlerBinding _canonicalBinding;

        /// <summary>
        /// Creates a group keyed by delegate type signature.
        /// </summary>
        public TypeSignatureGroup(DelegateTypeSignature typeSignature)
        {
            _typeSignature = typeSignature;
        }

        /// <summary>
        /// Adds a handler binding to the group and reports conflicting wagon names.
        /// </summary>
        public void Add(
            StationHandlerBinding handlerBinding,
            Location location,
            ChainSiteBinding chainBinding,
            SourceProductionContext context)
        {
            _entries.Add(new StationEntry(handlerBinding, location, chainBinding));
            if (chainBinding != null
                && !_chainBindings.Exists(existing =>
                    existing.ChainId == chainBinding.ChainId
                    && ChainStationCallIndex.BuildLocationKey(existing.InvocationLocation)
                        == ChainStationCallIndex.BuildLocationKey(chainBinding.InvocationLocation)))
            {
                _chainBindings.Add(chainBinding);
            }

            if (_canonicalBinding == null)
            {
                _canonicalBinding = handlerBinding;
            }

            AddReturnShape(handlerBinding.ReturnShape);
        }

        /// <summary>
        /// Produces a merged schema with combined return-shape metadata for code generation.
        /// </summary>
        public MergedStationSchema ToMerged(SourceProductionContext context)
        {
            var merged = new MergedStationSchema(_canonicalBinding, _typeSignature.TypeId);
            for (var i = 0; i < _returnShapes.Count; i++)
            {
                merged.AddReturnShape(_returnShapes[i]);
            }

            if (RequiresChainDispatch())
            {
                ReportNonChainConflicts(context);
                merged.SetChainBindings(_chainBindings);
            }
            else
            {
                ReportCanonicalConflicts(context);
            }

            return merged;
        }

        private void ReportCanonicalConflicts(SourceProductionContext context)
        {
            if (_canonicalBinding == null)
            {
                return;
            }

            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.ChainBinding != null)
                {
                    continue;
                }

                if (!HandlerInputParameters.WagonNamesMatch(_canonicalBinding.Wagons, entry.HandlerBinding.Wagons))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        TrainRouteDiagnostics.ConflictingWagonNames,
                        entry.Location,
                        HandlerInputParameters.FormatWagonNames(entry.HandlerBinding.Wagons),
                        HandlerInputParameters.FormatWagonNames(_canonicalBinding.Wagons)));
                }
            }
        }

        private bool RequiresChainDispatch()
        {
            if (_chainBindings.Count == 0)
            {
                return false;
            }

            var wagonNameSets = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < _entries.Count; i++)
            {
                wagonNameSets.Add(HandlerInputParameters.FormatWagonNames(_entries[i].HandlerBinding.Wagons));
            }

            return wagonNameSets.Count > 1;
        }

        private void ReportNonChainConflicts(SourceProductionContext context)
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                var left = _entries[i];
                if (left.ChainBinding != null)
                {
                    continue;
                }

                for (var j = 0; j < _entries.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    var right = _entries[j];
                    if (!HandlerInputParameters.WagonNamesMatch(left.HandlerBinding.Wagons, right.HandlerBinding.Wagons))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            TrainRouteDiagnostics.ConflictingWagonNames,
                            left.Location,
                            HandlerInputParameters.FormatWagonNames(left.HandlerBinding.Wagons),
                            HandlerInputParameters.FormatWagonNames(right.HandlerBinding.Wagons)));
                        break;
                    }
                }
            }
        }

        private void AddReturnShape(ReturnShape returnShape)
        {
            for (var i = 0; i < _returnShapes.Count; i++)
            {
                if (MergedStationSchema.ReturnShapesEqual(_returnShapes[i], returnShape))
                {
                    return;
                }
            }

            _returnShapes.Add(returnShape);
        }

        private sealed class StationEntry
        {
            public StationEntry(
                StationHandlerBinding handlerBinding,
                Location location,
                ChainSiteBinding chainBinding)
            {
                HandlerBinding = handlerBinding;
                Location = location;
                ChainBinding = chainBinding;
            }

            public StationHandlerBinding HandlerBinding { get; }

            public Location Location { get; }

            public ChainSiteBinding ChainBinding { get; }
        }
    }
}
