using TrainOP.Generators.Wagons;

namespace TrainOP.Generators.Handlers
{
    /// <summary>
    /// One parameter position in the generated delegate / handler-call order
    /// (Station vs ServiceStation use different orders).
    /// </summary>
    internal readonly struct HandlerCallSlot
    {
        /// <summary>
        /// Creates a non-wagon call slot (manifest, signal, or token).
        /// </summary>
        public static HandlerCallSlot Special(HandlerInputKind kind)
        {
            return new HandlerCallSlot(kind, wagonIndex: -1, wagon: null);
        }

        /// <summary>
        /// Creates a wagon call slot at the given wagon index.
        /// </summary>
        public static HandlerCallSlot ForWagon(int wagonIndex, WagonBinding wagon)
        {
            return new HandlerCallSlot(HandlerInputKind.Wagon, wagonIndex, wagon);
        }

        private HandlerCallSlot(HandlerInputKind kind, int wagonIndex, WagonBinding wagon)
        {
            Kind = kind;
            WagonIndex = wagonIndex;
            Wagon = wagon;
        }

        /// <summary>What this call position represents.</summary>
        public HandlerInputKind Kind { get; }

        /// <summary>Index into <see cref="HandlerInputParameters.Wagons"/>, or -1 for special slots.</summary>
        public int WagonIndex { get; }

        /// <summary>Wagon binding when <see cref="Kind"/> is <see cref="HandlerInputKind.Wagon"/>.</summary>
        public WagonBinding Wagon { get; }
    }
}
