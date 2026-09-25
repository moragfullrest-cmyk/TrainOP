namespace TrainOP.Generators
{
    /// <summary>
    /// Captures one wagon parameter's type and binding flags within a delegate signature.
    /// </summary>
    internal readonly struct WagonTypeSlot
    {
        /// <summary>
        /// Creates a wagon type slot from display strings and binding flags.
        /// </summary>
        public WagonTypeSlot(
            string typeDisplay,
            bool isByReference,
            bool isOptional,
            string pullTypeDisplay,
            bool isOut = false,
            bool isRefReadonly = false,
            bool isIn = false,
            bool isParams = false)
        {
            TypeDisplay = typeDisplay;
            IsByReference = isByReference;
            IsOptional = isOptional;
            PullTypeDisplay = pullTypeDisplay;
            IsOut = isOut;
            IsRefReadonly = isRefReadonly;
            IsIn = isIn;
            IsParams = isParams;
        }

        public string TypeDisplay { get; }

        public bool IsByReference { get; }

        public bool IsOptional { get; }

        public string PullTypeDisplay { get; }

        public bool IsOut { get; }

        public bool IsRefReadonly { get; }

        public bool IsIn { get; }

        public bool IsParams { get; }
    }
}
