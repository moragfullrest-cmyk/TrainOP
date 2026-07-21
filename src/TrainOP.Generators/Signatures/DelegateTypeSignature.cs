using System;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using TrainOP.Generators.Handlers;
namespace TrainOP.Generators
{
    /// <summary>
    /// Describes a handler delegate type signature used to group generated extension methods.
    /// </summary>
    internal sealed class DelegateTypeSignature
    {
        /// <summary>
        /// Creates a delegate type signature from handler flags and wagon type slots.
        /// </summary>
        public DelegateTypeSignature(
            bool isServiceStation,
            bool includeRedSignal,
            bool includeSignalIssue,
            bool includeManifest,
            bool isAsync,
            bool hasCancellationToken,
            bool isVoid,
            bool useGenericReturn,
            string returnTypeKey,
            ImmutableArray<WagonTypeSlot> wagonTypes)
        {
            IsServiceStation = isServiceStation;
            IncludeRedSignal = includeRedSignal;
            IncludeSignalIssue = includeSignalIssue;
            IncludeManifest = includeManifest;
            IsAsync = isAsync;
            HasCancellationToken = hasCancellationToken;
            IsVoid = isVoid;
            UseGenericReturn = useGenericReturn;
            ReturnTypeKey = returnTypeKey;
            WagonTypes = wagonTypes;
            TypeId = BuildTypeId(this);
        }

        public bool IsServiceStation { get; }

        public bool IncludeRedSignal { get; }

        public bool IncludeSignalIssue { get; }

        public bool IncludeManifest { get; }

        public bool IsAsync { get; }

        public bool HasCancellationToken { get; }

        public bool IsVoid { get; }

        public bool UseGenericReturn { get; }

        public string ReturnTypeKey { get; }

        public ImmutableArray<WagonTypeSlot> WagonTypes { get; }

        public string TypeId { get; }

        /// <summary>
        /// Builds a delegate type signature from a handler binding.
        /// </summary>
        public static DelegateTypeSignature From(StationHandlerBinding handlerBinding)
        {
            var wagonTypes = ImmutableArray.CreateBuilder<WagonTypeSlot>(handlerBinding.Wagons.Length);
            for (var i = 0; i < handlerBinding.Wagons.Length; i++)
            {
                var wagon = handlerBinding.Wagons[i];
                wagonTypes.Add(new WagonTypeSlot(
                    wagon.TypeDisplay,
                    wagon.IsByReference,
                    wagon.IsOptional,
                    wagon.PullTypeDisplay));
            }

            return new DelegateTypeSignature(
                handlerBinding.IsServiceStation,
                handlerBinding.IncludeRedSignal,
                handlerBinding.IncludeSignalIssue,
                handlerBinding.IncludeManifest,
                handlerBinding.IsAsync,
                handlerBinding.HasCancellationToken,
                handlerBinding.ReturnShape.IsVoid,
                handlerBinding.ReturnShape.UseGenericReturn,
                BuildReturnTypeKey(handlerBinding.ReturnShape),
                wagonTypes.ToImmutable());
        }

        private static string BuildReturnTypeKey(ReturnShape returnShape)
        {
            if (returnShape.IsVoid)
            {
                return "void";
            }

            if (returnShape.IsExplicitSignalReturn)
            {
                return ReturnTypeDisplayHelper.SignalReturnTypeDisplay;
            }

            if (returnShape.UseGenericReturn
                || returnShape.IsUnknown
                || string.Equals(returnShape.ReturnTypeDisplay, "global::System.Object", StringComparison.Ordinal))
            {
                return "object";
            }

            if (returnShape.IsValueTuple)
            {
                return "tuple:" + HandlerFuncTypeResolver.ResolveCanonicalTupleReturnTypeDisplay(returnShape);
            }

            return returnShape.ReturnTypeDisplay ?? "object";
        }

        /// <summary>
        /// Builds a short hash-based identifier from a delegate type signature.
        /// </summary>
        private static string BuildTypeId(DelegateTypeSignature signature)
        {
            var builder = new StringBuilder();
            builder.Append(signature.IsServiceStation ? "S1" : "S0");
            builder.Append(signature.IncludeRedSignal ? "R1" : "R0");
            builder.Append(signature.IncludeSignalIssue ? "I1" : "I0");
            builder.Append(signature.IncludeManifest ? "M1" : "M0");
            builder.Append(signature.IsAsync ? "A1" : "A0");
            builder.Append(signature.HasCancellationToken ? "C1" : "C0");
            builder.Append(signature.IsVoid ? "V1" : "V0");
            builder.Append(signature.UseGenericReturn ? "G1" : "G0");
            builder.Append('|').Append(signature.ReturnTypeKey ?? string.Empty);
            for (var i = 0; i < signature.WagonTypes.Length; i++)
            {
                var wagon = signature.WagonTypes[i];
                builder.Append('|').Append(wagon.TypeDisplay);
                builder.Append(':').Append(wagon.IsByReference ? "R1" : "R0");
                builder.Append(':').Append(wagon.IsOptional ? "O1" : "O0");
                builder.Append(':').Append(wagon.PullTypeDisplay);
            }

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(builder.ToString());
                var hash = sha.ComputeHash(bytes);
                var result = new StringBuilder(16);
                for (var i = 0; i < 8; i++)
                {
                    result.Append(hash[i].ToString("x2"));
                }

                return result.ToString();
            }
        }
    }
}
