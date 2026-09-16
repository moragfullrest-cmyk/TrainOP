using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Handlers;

namespace TrainOP.Generators
{
    /// <summary>
    /// Accumulates handler bindings that share the same delegate type signature.
    /// Chain context is attached later via <see cref="AttachChainContext"/>.
    /// </summary>
    internal sealed class DelegateSignatureGroup
    {
        private readonly DelegateTypeSignature _typeSignature;
        private readonly List<ReturnShape> _returnShapes = new List<ReturnShape>();
        private readonly List<ChainSiteBinding> _chainBindings = new List<ChainSiteBinding>();
        private readonly List<StationEntry> _entries = new List<StationEntry>();
        private StationHandlerBinding _canonicalBinding;

        /// <summary>
        /// Creates a group keyed by delegate type signature.
        /// </summary>
        public DelegateSignatureGroup(DelegateTypeSignature typeSignature)
        {
            _typeSignature = typeSignature;
        }

        /// <summary>
        /// Adds a handler binding and return shape without chain context.
        /// </summary>
        public void Add(
            StationHandlerBinding handlerBinding,
            Location location,
            Location invocationLocation)
        {
            _entries.Add(new StationEntry(handlerBinding, location, invocationLocation, chainBinding: null));

            if (_canonicalBinding == null)
            {
                _canonicalBinding = handlerBinding;
            }

            AddReturnShape(handlerBinding.ReturnShape);
        }

        /// <summary>
        /// Joins this group with chain-index bindings by invocation location key.
        /// Expands one entry per resolved chain binding (preserves prior multi-binding semantics).
        /// </summary>
        public void AttachChainContext(
            IReadOnlyDictionary<string, ImmutableArray<ChainSiteBinding>> chainIndex)
        {
            if (_entries.Count == 0)
            {
                return;
            }

            var attached = new List<StationEntry>(_entries.Count);
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (ChainSiteBindingLookup.TryResolveAll(chainIndex, entry.InvocationLocation, out var chainBindings)
                    && chainBindings.Length > 0)
                {
                    for (var j = 0; j < chainBindings.Length; j++)
                    {
                        var chainBinding = chainBindings[j];
                        attached.Add(new StationEntry(
                            entry.HandlerBinding,
                            entry.Location,
                            entry.InvocationLocation,
                            chainBinding));
                        AddUniqueChainBinding(chainBinding);
                    }
                }
                else
                {
                    attached.Add(entry);
                }
            }

            _entries.Clear();
            _entries.AddRange(attached);
        }

        /// <summary>
        /// Builds a branch plan (merged schema + TOP007) using <see cref="ChainDispatchPolicy"/>.
        /// </summary>
        public BranchPlan ToBranchPlan(SourceProductionContext context)
        {
            return ToBranchPlan(context.ReportDiagnostic);
        }

        /// <summary>
        /// Builds a branch plan with an explicit diagnostic sink (generator context or tests).
        /// </summary>
        public BranchPlan ToBranchPlan(Action<Diagnostic> reportDiagnostic)
        {
            var merged = new MergedStationSchema(_canonicalBinding, _typeSignature.TypeId);
            for (var i = 0; i < _returnShapes.Count; i++)
            {
                merged.AddReturnShape(_returnShapes[i]);
            }

            if (ChainDispatchPolicy.RequiresChainDispatch(
                _chainBindings,
                _returnShapes,
                CollectEntryWagonNameKeys()))
            {
                ReportNonChainConflicts(reportDiagnostic);
                merged.SetChainBindings(_chainBindings);
            }
            else
            {
                ReportCanonicalConflicts(reportDiagnostic);
            }

            return new BranchPlan(merged);
        }

        private string[] CollectEntryWagonNameKeys()
        {
            var keys = new string[_entries.Count];
            for (var i = 0; i < _entries.Count; i++)
            {
                keys[i] = HandlerInputParameters.FormatWagonNames(_entries[i].HandlerBinding.Wagons);
            }

            return keys;
        }

        private void AddUniqueChainBinding(ChainSiteBinding chainBinding)
        {
            if (chainBinding == null)
            {
                return;
            }

            if (_chainBindings.Exists(existing =>
                existing.ChainId == chainBinding.ChainId
                && ChainSiteBindingLookup.BuildLocationKey(existing.InvocationLocation)
                    == ChainSiteBindingLookup.BuildLocationKey(chainBinding.InvocationLocation)))
            {
                return;
            }

            _chainBindings.Add(chainBinding);
        }

        private void ReportCanonicalConflicts(Action<Diagnostic> reportDiagnostic)
        {
            if (_canonicalBinding == null || reportDiagnostic == null)
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
                    reportDiagnostic(Diagnostic.Create(
                        TrainRouteDiagnostics.ConflictingWagonNames,
                        entry.Location,
                        HandlerInputParameters.FormatWagonNames(entry.HandlerBinding.Wagons),
                        HandlerInputParameters.FormatWagonNames(_canonicalBinding.Wagons)));
                }
            }
        }

        private void ReportNonChainConflicts(Action<Diagnostic> reportDiagnostic)
        {
            if (reportDiagnostic == null)
            {
                return;
            }

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
                        reportDiagnostic(Diagnostic.Create(
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
                Location invocationLocation,
                ChainSiteBinding chainBinding)
            {
                HandlerBinding = handlerBinding;
                Location = location;
                InvocationLocation = invocationLocation;
                ChainBinding = chainBinding;
            }

            public StationHandlerBinding HandlerBinding { get; }

            public Location Location { get; }

            public Location InvocationLocation { get; }

            public ChainSiteBinding ChainBinding { get; }
        }
    }
}
