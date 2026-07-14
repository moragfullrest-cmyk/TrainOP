using TrainOP.Generators.Models;

namespace TrainOP.Generators
{
    /// <summary>
    /// Merges validated forking route branches into a shared upstream wagon state.
    /// </summary>
    internal static class BranchRouteJoinMerger
    {
        /// <summary>
        /// Validates <paramref name="joinSet"/> and returns whether branches can be merged.
        /// Merged terminal wagons are available on <paramref name="validation"/> when merge succeeds.
        /// </summary>
        public static bool TryMerge(BranchRouteJoinSet joinSet, out BranchRouteJoinValidation validation)
        {
            validation = BranchRouteJoinValidator.Validate(joinSet);
            return validation.CanMerge;
        }
    }
}
