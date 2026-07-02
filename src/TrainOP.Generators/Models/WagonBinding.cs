using Microsoft.CodeAnalysis;

namespace TrainOP.Generators.Models
{
    internal sealed class WagonBinding
    {
        public WagonBinding(
            string name,
            string typeDisplay,
            ITypeSymbol typeSymbol,
            Location location,
            bool isByReference = false,
            bool isOptional = false,
            string pullTypeDisplay = null)
        {
            Name = name;
            TypeDisplay = typeDisplay;
            TypeSymbol = typeSymbol;
            Location = location;
            IsByReference = isByReference;
            IsOptional = isOptional;
            PullTypeDisplay = pullTypeDisplay ?? typeDisplay;
        }

        public string Name { get; }

        public string TypeDisplay { get; }

        public ITypeSymbol TypeSymbol { get; }

        public Location Location { get; }

        public bool IsByReference { get; }

        public bool IsOptional { get; }

        public string PullTypeDisplay { get; }
    }
}
