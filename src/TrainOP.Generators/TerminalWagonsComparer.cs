using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Compares terminal wagon sets for factory return-path validation.
    /// </summary>
    internal static class TerminalWagonsComparer
    {
        /// <summary>
        /// Determines whether two terminal wagon sets are equivalent (same names and compatible types; order ignored).
        /// </summary>
        public static bool AreEquivalent(
            ImmutableArray<WagonBinding> left,
            ImmutableArray<WagonBinding> right)
        {
            if (left.IsDefaultOrEmpty && right.IsDefaultOrEmpty)
            {
                return true;
            }

            if (left.IsDefaultOrEmpty || right.IsDefaultOrEmpty)
            {
                return false;
            }

            var leftByName = left.ToDictionary(w => w.Name, StringComparer.Ordinal);
            var rightByName = right.ToDictionary(w => w.Name, StringComparer.Ordinal);
            if (leftByName.Count != rightByName.Count)
            {
                return false;
            }

            foreach (var pair in leftByName)
            {
                if (!rightByName.TryGetValue(pair.Key, out var other))
                {
                    return false;
                }

                if (!ChainGraphSimulator.TypesCompatible(pair.Value.TypeSymbol, other.TypeSymbol))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Builds a human-readable description of how two terminal sets differ.
        /// </summary>
        public static string DescribeDifference(
            ImmutableArray<WagonBinding> left,
            ImmutableArray<WagonBinding> right)
        {
            left = Normalize(left);
            right = Normalize(right);

            var leftNames = new HashSet<string>(left.Select(w => w.Name), StringComparer.Ordinal);
            var rightNames = new HashSet<string>(right.Select(w => w.Name), StringComparer.Ordinal);

            var onlyLeft = leftNames.Except(rightNames, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            var onlyRight = rightNames.Except(leftNames, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();

            var builder = new StringBuilder();
            if (onlyLeft.Length > 0)
            {
                builder.Append("wagons only in earlier path: ").Append(string.Join(", ", onlyLeft));
            }

            if (onlyRight.Length > 0)
            {
                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append("wagons only in later path: ").Append(string.Join(", ", onlyRight));
            }

            foreach (var name in leftNames.Intersect(rightNames, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
            {
                var leftWagon = left.First(w => string.Equals(w.Name, name, StringComparison.Ordinal));
                var rightWagon = right.First(w => string.Equals(w.Name, name, StringComparison.Ordinal));
                if (ChainGraphSimulator.TypesCompatible(leftWagon.TypeSymbol, rightWagon.TypeSymbol))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append("wagon '")
                    .Append(name)
                    .Append("' has conflicting types ('")
                    .Append(leftWagon.TypeDisplay)
                    .Append("' vs '")
                    .Append(rightWagon.TypeDisplay)
                    .Append("')");
            }

            return builder.Length == 0 ? "terminal wagon sets differ" : builder.ToString();
        }

        /// <summary>
        /// Returns terminal wagons sorted by wagon name for stable schema emit and comparison.
        /// </summary>
        public static ImmutableArray<WagonBinding> Normalize(ImmutableArray<WagonBinding> wagons)
        {
            if (wagons.IsDefaultOrEmpty)
            {
                return ImmutableArray<WagonBinding>.Empty;
            }

            return wagons.OrderBy(w => w.Name, StringComparer.Ordinal).ToImmutableArray();
        }
    }
}
