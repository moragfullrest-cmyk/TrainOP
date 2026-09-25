using Microsoft.CodeAnalysis;

namespace TrainOP.Generators.Wagons
{
    /// <summary>
    /// Binds a named wagon to its type, source location, and pull semantics.
    /// </summary>
    internal sealed class WagonBinding
    {
        /// <summary>
        /// Creates a wagon binding with type, location, and optional ref/nullable metadata.
        /// </summary>
        public WagonBinding(
            string name,
            string typeDisplay,
            ITypeSymbol typeSymbol,
            Location location,
            bool isByReference = false,
            bool isOptional = false,
            string pullTypeDisplay = null,
            bool isOut = false,
            bool isRefReadonly = false,
            bool isIn = false,
            bool isParams = false)
        {
            Name = name;
            TypeDisplay = typeDisplay;
            TypeSymbol = typeSymbol;
            Location = location;
            IsByReference = isByReference;
            IsOptional = isOptional;
            PullTypeDisplay = pullTypeDisplay ?? typeDisplay;
            IsOut = isOut;
            IsRefReadonly = isRefReadonly;
            IsIn = isIn;
            IsParams = isParams;
        }

        public string Name { get; }

        public string TypeDisplay { get; }

        public ITypeSymbol TypeSymbol { get; }

        public Location Location { get; }

        /// <summary><c>ref</c> parameter: write the local back after the handler.</summary>
        public bool IsByReference { get; }

        public bool IsOptional { get; }

        public string PullTypeDisplay { get; }

        /// <summary><c>out</c> parameter: not pulled; the local is written back and may create the wagon.</summary>
        public bool IsOut { get; }

        /// <summary><c>ref readonly</c> parameter: pulled, not written back, not unloaded.</summary>
        public bool IsRefReadonly { get; }

        /// <summary><c>in</c> parameter: same manifest rules as <c>ref readonly</c>.</summary>
        public bool IsIn { get; }

        /// <summary><c>params</c> collection: one wagon, passed by value. Legal only as the last delegate parameter.</summary>
        public bool IsParams { get; }

        /// <summary>True for <c>ref readonly</c> and <c>in</c>.</summary>
        public bool IsReadOnlyPass => IsRefReadonly || IsIn;

        /// <summary>True when the local is stored in <c>refLocalValues</c> and loaded after a successful return.</summary>
        public bool WritesBack => IsByReference || IsOut;

        /// <summary>True when a partial or void return must not unload this wagon.</summary>
        public bool RetainsSlot => IsByReference || IsReadOnlyPass || IsOut;

        /// <summary>Delegate parameter prefix, including the trailing space.</summary>
        public string ParameterModifier
        {
            get
            {
                if (IsOut)
                {
                    return "out ";
                }

                if (IsRefReadonly)
                {
                    return "ref readonly ";
                }

                if (IsIn)
                {
                    return "in ";
                }

                if (IsByReference)
                {
                    return "ref ";
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// Delegate declaration prefix. <c>params</c> is included only when this wagon is the last parameter.
        /// </summary>
        public string GetDeclarationModifier(bool emitParams)
        {
            if (emitParams && IsParams)
            {
                return "params " + ParameterModifier;
            }

            return ParameterModifier;
        }

        /// <summary>Call-argument prefix. <c>ref readonly</c> and <c>in</c> are passed with <c>in</c>.</summary>
        public string ArgumentModifier => IsReadOnlyPass ? "in " : ParameterModifier;
    }
}
