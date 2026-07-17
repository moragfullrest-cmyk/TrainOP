using System;
using System.Collections.Generic;

namespace TrainOP.Generators
{
    /// <summary>
    /// Compares delegate type signatures for equality when grouping handler bindings.
    /// </summary>
    internal sealed class DelegateTypeSignatureComparer : IEqualityComparer<DelegateTypeSignature>
    {
        public static DelegateTypeSignatureComparer Instance { get; } = new DelegateTypeSignatureComparer();

        /// <summary>
        /// Determines whether two delegate type signatures are equal.
        /// </summary>
        public bool Equals(DelegateTypeSignature x, DelegateTypeSignature y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x == null || y == null)
            {
                return false;
            }

            if (x.IsServiceStation != y.IsServiceStation
                || x.IncludeRedSignal != y.IncludeRedSignal
                || x.IncludeSignalIssue != y.IncludeSignalIssue
                || x.IncludeManifest != y.IncludeManifest
                || x.IsAsync != y.IsAsync
                || x.HasCancellationToken != y.HasCancellationToken
                || x.IsVoid != y.IsVoid
                || x.UseGenericReturn != y.UseGenericReturn
                || !string.Equals(x.ReturnTypeKey, y.ReturnTypeKey, StringComparison.Ordinal)
                || x.WagonTypes.Length != y.WagonTypes.Length)
            {
                return false;
            }

            for (var i = 0; i < x.WagonTypes.Length; i++)
            {
                var left = x.WagonTypes[i];
                var right = y.WagonTypes[i];
                if (!string.Equals(left.TypeDisplay, right.TypeDisplay, StringComparison.Ordinal)
                    || left.IsByReference != right.IsByReference
                    || left.IsOptional != right.IsOptional
                    || !string.Equals(left.PullTypeDisplay, right.PullTypeDisplay, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Computes a hash code for a delegate type signature.
        /// </summary>
        public int GetHashCode(DelegateTypeSignature obj)
        {
            if (obj == null)
            {
                return 0;
            }

            unchecked
            {
                var hash = 17;
                hash = (hash * 31) + obj.IsServiceStation.GetHashCode();
                hash = (hash * 31) + obj.IncludeRedSignal.GetHashCode();
                hash = (hash * 31) + obj.IncludeSignalIssue.GetHashCode();
                hash = (hash * 31) + obj.IncludeManifest.GetHashCode();
                hash = (hash * 31) + obj.IsAsync.GetHashCode();
                hash = (hash * 31) + obj.HasCancellationToken.GetHashCode();
                hash = (hash * 31) + obj.IsVoid.GetHashCode();
                hash = (hash * 31) + obj.UseGenericReturn.GetHashCode();
                hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(obj.ReturnTypeKey ?? string.Empty);
                foreach (var wagon in obj.WagonTypes)
                {
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(wagon.TypeDisplay);
                    hash = (hash * 31) + wagon.IsByReference.GetHashCode();
                    hash = (hash * 31) + wagon.IsOptional.GetHashCode();
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(wagon.PullTypeDisplay);
                }

                return hash;
            }
        }
    }
}
