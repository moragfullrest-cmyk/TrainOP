using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace TrainOP.Generators
{
    /// <summary>
    /// Stage 3: turn attached signature groups into canonical ∥ chain-aware branch plans (+ TOP007).
    /// </summary>
    internal static class BranchPlanStage
    {
        /// <summary>
        /// Builds ordered branch plans from attached signature groups.
        /// </summary>
        public static ImmutableArray<BranchPlan> Build(
            IEnumerable<DelegateSignatureGroup> groups,
            SourceProductionContext context)
        {
            return Build(groups, context.ReportDiagnostic);
        }

        /// <summary>
        /// Builds ordered branch plans with an explicit diagnostic sink (generator or unit tests).
        /// </summary>
        public static ImmutableArray<BranchPlan> Build(
            IEnumerable<DelegateSignatureGroup> groups,
            Action<Diagnostic> reportDiagnostic = null)
        {
            if (groups == null)
            {
                return ImmutableArray<BranchPlan>.Empty;
            }

            return groups
                .Select(group => group.ToBranchPlan(reportDiagnostic))
                .OrderBy(plan => plan.Schema.DelegateTypeId, StringComparer.Ordinal)
                .ToImmutableArray();
        }
    }
}
