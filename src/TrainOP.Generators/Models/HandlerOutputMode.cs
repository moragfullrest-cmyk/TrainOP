namespace TrainOP.Generators.Models
{
    /// <summary>
    /// High-level classification of what a handler returns for merge and codegen.
    /// </summary>
    internal enum HandlerOutputMode
    {
        /// <summary>No return value (void / non-generic Task).</summary>
        Void,

        /// <summary>Return type could not be inferred statically.</summary>
        Unknown,

        /// <summary>Returns a full <c>CargoManifest</c>.</summary>
        CargoManifest,

        /// <summary>Returns a runtime signal object without known members.</summary>
        RuntimeSignal,

        /// <summary>Returns an explicit signal type (Green/Red family).</summary>
        ExplicitSignal,

        /// <summary>Returns known named members (object properties/fields or tuple elements).</summary>
        KnownMembers
    }
}
