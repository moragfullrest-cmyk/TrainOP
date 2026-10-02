using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using TrainOP.Generators.Chain;
using TrainOP.Generators.Wagons;

namespace TrainOP.Generators
{
    /// <summary>
    /// Emits one interpreter segment for a run of pure, capture-free stations and the attach method every chain core calls.
    /// </summary>
    internal static class RouteSegmentEmitter
    {
        /// <summary>
        /// Appends <c>AttachRouteSegments</c> and one private method per pure run of at least two stations.
        /// </summary>
        internal static void Emit(CodegenWriter writer, ImmutableArray<MergedStationSchema> schemas)
        {
            var runs = Collect(schemas);
            writer.AppendLine();
            writer.AppendLine("private static TrainRoute AttachRouteSegments(TrainRoute route, string chainKey, int chainStationIndex)");
            using (writer.Block())
            {
                for (var i = 0; i < runs.Count; i++)
                {
                    var run = runs[i];
                    writer.AppendIndented("if (string.Equals(chainKey, \"")
                        .Append(StringHelpers.Escape(run.ChainId))
                        .Append("\", StringComparison.Ordinal) && chainStationIndex == ")
                        .Append(run.LastIndex)
                        .Append(")");
                    writer.EndLine();
                    using (writer.Block())
                    {
                        writer.AppendIndented("route = route.AttachSegment(")
                            .Append(run.StartIndex)
                            .Append(", ")
                            .Append(run.Steps.Count)
                            .Append(", ")
                            .Append(run.MethodName)
                            .Append(");");
                        writer.EndLine();
                    }
                }

                writer.AppendLine("return route;");
            }

            for (var i = 0; i < runs.Count; i++)
            {
                writer.AppendLine();
                EmitSegmentMethod(writer, runs[i]);
            }
        }

        private static List<SegmentRun> Collect(ImmutableArray<MergedStationSchema> schemas)
        {
            var byChain = new Dictionary<string, List<ChainSiteBinding>>(StringComparer.Ordinal);
            if (!schemas.IsDefault)
            {
                for (var i = 0; i < schemas.Length; i++)
                {
                    var bindings = schemas[i]?.ChainBindings;
                    if (bindings == null)
                    {
                        continue;
                    }

                    for (var j = 0; j < bindings.Count; j++)
                    {
                        var binding = bindings[j];
                        if (binding == null || string.IsNullOrEmpty(binding.ChainId))
                        {
                            continue;
                        }

                        if (!byChain.TryGetValue(binding.ChainId, out var list))
                        {
                            list = new List<ChainSiteBinding>();
                            byChain.Add(binding.ChainId, list);
                        }

                        list.Add(binding);
                    }
                }
            }

            var runs = new List<SegmentRun>();
            foreach (var pair in byChain)
            {
                var stations = pair.Value;
                stations.Sort(CompareStationIndex);
                var current = new List<ChainSiteBinding>();
                var live = new HashSet<string>(StringComparer.Ordinal);
                var previousIndex = 0;
                var havePrevious = false;
                for (var i = 0; i < stations.Count; i++)
                {
                    var binding = stations[i];
                    if (havePrevious && binding.StationIndex == previousIndex)
                    {
                        continue;
                    }

                    var consecutive = havePrevious && binding.StationIndex == previousIndex + 1;
                    if (current.Count > 0 && !consecutive)
                    {
                        AddRun(runs, pair.Key, current);
                        current.Clear();
                        live.Clear();
                    }

                    previousIndex = binding.StationIndex;
                    havePrevious = true;
                    if (!IsPure(binding))
                    {
                        AddRun(runs, pair.Key, current);
                        current.Clear();
                        live.Clear();
                        continue;
                    }

                    // A follower joins only by reading wagons this run already produced.
                    // () => ("b", 2m) does not, so ItemN stays a hop and allocates Item3.
                    if (current.Count > 0 && !ReadsProducedWagons(binding, live))
                    {
                        AddRun(runs, pair.Key, current);
                        current.Clear();
                        live.Clear();
                    }

                    current.Add(binding);
                    RememberLiveWagons(live, binding);
                }

                AddRun(runs, pair.Key, current);
            }

            runs.Sort(CompareRuns);
            for (var i = 0; i < runs.Count; i++)
            {
                runs[i].MethodName = "Segment_" + i + "_" + runs[i].StartIndex + "_"
                    + StringHelpers.SanitizeIdentifier(runs[i].ChainId);
            }

            return runs;
        }

        private static void AddRun(List<SegmentRun> runs, string chainId, List<ChainSiteBinding> current)
        {
            if (current.Count < 2)
            {
                return;
            }

            runs.Add(new SegmentRun
            {
                ChainId = chainId,
                StartIndex = current[0].StationIndex,
                Steps = new List<ChainSiteBinding>(current)
            });
        }

        private static bool IsPure(ChainSiteBinding binding)
        {
            var schema = binding.Schema;
            if (schema == null
                || string.IsNullOrEmpty(binding.StationName)
                || schema.ReturnShape == null
                || schema.ReturnShape.HasDefaultItemNTupleElements
                || string.IsNullOrWhiteSpace(schema.StraightExpression)
                || schema.IsAsync
                || schema.IsServiceStation
                || schema.HasCancellationToken
                || schema.HasRefWagons
                || schema.IncludeManifest
                || schema.IncludeRedSignal
                || schema.IncludeSignalIssue
                || schema.IncludeSignalIssues
                || schema.IncludeVisitJournal)
            {
                return false;
            }

            var members = binding.ReturnMembers;
            if (members == null || members.Length == 0)
            {
                return false;
            }

            var wagons = schema.Wagons;
            for (var i = 0; i < wagons.Length; i++)
            {
                if (wagons[i].IsOptional)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when every input is a wagon an earlier step of this run already returned.
        /// A station with no inputs merges at the hop boundary instead.
        /// </summary>
        private static bool ReadsProducedWagons(ChainSiteBinding binding, HashSet<string> live)
        {
            var wagons = binding.Schema.Wagons;
            if (wagons.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < wagons.Length; i++)
            {
                if (!live.Contains(wagons[i].Name))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Keeps names the step returns and drops inputs the return omits.
        /// </summary>
        private static void RememberLiveWagons(HashSet<string> live, ChainSiteBinding binding)
        {
            var returned = new HashSet<string>(StringComparer.Ordinal);
            var members = binding.ReturnMembers;
            for (var i = 0; i < members.Length; i++)
            {
                returned.Add(members[i]);
                live.Add(members[i]);
            }

            var wagons = binding.Schema.Wagons;
            for (var i = 0; i < wagons.Length; i++)
            {
                var wagon = wagons[i];
                if (wagon.IsReadOnlyPass || returned.Contains(wagon.Name))
                {
                    continue;
                }

                live.Remove(wagon.Name);
            }
        }

        private static void EmitSegmentMethod(CodegenWriter writer, SegmentRun run)
        {
            var slots = CollectSlots(run.Steps);
            writer.AppendIndented("private static Signal ")
                .Append(run.MethodName)
                .Append("(CargoManifest segManifest, CancellationToken segToken, SegmentVisitLog segVisits, int segIndex)");
            writer.EndLine();
            using (writer.Block())
            {
                for (var i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    writer.AppendIndented(slot.TypeDisplay)
                        .Append(" ")
                        .Append(slot.Name)
                        .Append(" = default(")
                        .Append(slot.TypeDisplay)
                        .Append(");");
                    writer.EndLine();
                    writer.AppendLine("bool segLive" + i + " = false;");
                    writer.AppendLine("bool segWrote" + i + " = false;");
                    writer.AppendLine("bool segPulled" + i + " = false;");
                }

                writer.AppendLine("void FlushSegment()");
                using (writer.Block())
                {
                    for (var i = 0; i < slots.Count; i++)
                    {
                        writer.AppendLine("if (segLive" + i + " && segWrote" + i + ")");
                        using (writer.Block())
                        {
                            writer.AppendIndented("segManifest.LoadWagonUnchecked(\"")
                                .Append(StringHelpers.Escape(slots[i].Name))
                                .Append("\", ")
                                .Append(slots[i].Name)
                                .Append(");");
                            writer.EndLine();
                        }

                        writer.AppendLine("if (segPulled" + i + " && !segLive" + i + ")");
                        using (writer.Block())
                        {
                            writer.AppendIndented("segManifest.UnloadWagonUnchecked(\"")
                                .Append(StringHelpers.Escape(slots[i].Name))
                                .Append("\");");
                            writer.EndLine();
                        }
                    }
                }

                for (var step = 0; step < run.Steps.Count; step++)
                {
                    EmitStep(writer, run.Steps[step], step, run.Steps, slots);
                }

                writer.AppendLine("FlushSegment();");
                writer.AppendLine("return RailwaySignals.Green();");
            }
        }

        private static void EmitStep(
            CodegenWriter writer,
            ChainSiteBinding binding,
            int step,
            List<ChainSiteBinding> steps,
            List<WagonSlot> slots)
        {
            var indexByName = IndexByName(slots);
            writer.AppendLine("if (segToken.CanBeCanceled)");
            using (writer.Block())
            {
                writer.AppendLine("segToken.ThrowIfCancellationRequested();");
            }

            writer.AppendLine("long segStarted" + step + " = 0;");
            writer.AppendLine("if (segVisits != null)");
            using (writer.Block())
            {
                writer.AppendLine("segStarted" + step + " = global::System.Diagnostics.Stopwatch.GetTimestamp();");
            }

            writer.AppendLine("try");
            using (writer.Block())
            {
                var wagons = binding.Schema.Wagons;
                for (var i = 0; i < wagons.Length; i++)
                {
                    if (!indexByName.TryGetValue(wagons[i].Name, out var slotIndex))
                    {
                        continue;
                    }

                    writer.AppendLine("if (!segLive" + slotIndex + ")");
                    using (writer.Block())
                    {
                        writer.AppendLine("if (segPulled" + slotIndex + " || segWrote" + slotIndex + ")");
                        using (writer.Block())
                        {
                            writer.AppendIndented("throw new global::System.Collections.Generic.KeyNotFoundException(\"Wagon '")
                                .Append(StringHelpers.Escape(wagons[i].Name))
                                .Append("' was not found in the manifest.\");");
                            writer.EndLine();
                        }

                        writer.AppendIndented(wagons[i].Name)
                            .Append(" = segManifest.PullWagonUnchecked<")
                            .Append(slots[slotIndex].TypeDisplay)
                            .Append(">(\"")
                            .Append(StringHelpers.Escape(wagons[i].Name))
                            .Append("\");");
                        writer.EndLine();
                        writer.AppendLine("segPulled" + slotIndex + " = true;");
                        writer.AppendLine("segLive" + slotIndex + " = true;");
                    }
                }

                writer.AppendIndented("var segStep")
                    .Append(step)
                    .Append(" = ")
                    .Append(binding.Schema.StraightExpression)
                    .Append(";");
                writer.EndLine();

                var returns = binding.ReturnMembers;
                var returned = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < returns.Length; i++)
                {
                    var name = returns[i];
                    returned.Add(name);
                    if (!indexByName.TryGetValue(name, out var slotIndex))
                    {
                        continue;
                    }

                    writer.AppendIndented(name)
                        .Append(" = segStep")
                        .Append(step)
                        .Append(".")
                        .Append(name)
                        .Append(";");
                    writer.EndLine();
                    writer.AppendLine("segWrote" + slotIndex + " = true;");
                    writer.AppendLine("segLive" + slotIndex + " = true;");
                }

                for (var i = 0; i < wagons.Length; i++)
                {
                    var wagon = wagons[i];
                    if (wagon.IsReadOnlyPass || returned.Contains(wagon.Name))
                    {
                        continue;
                    }

                    if (!indexByName.TryGetValue(wagon.Name, out var slotIndex))
                    {
                        continue;
                    }

                    writer.AppendLine("segLive" + slotIndex + " = false;");
                }

                writer.AppendLine("if (segVisits != null)");
                using (writer.Block())
                {
                    writer.AppendIndented("segVisits.Record(\"")
                        .Append(StringHelpers.Escape(binding.StationName))
                        .Append("\", HopOutcome.Green, segIndex + ")
                        .Append(step)
                        .Append(", StationVisit.ElapsedSince(segStarted")
                        .Append(step)
                        .Append("));");
                    writer.EndLine();
                }
            }

            writer.AppendLine("catch (global::System.OperationCanceledException)");
            using (writer.Block())
            {
                writer.AppendLine("throw;");
            }

            writer.AppendLine("catch (global::System.Exception segException" + step + ")");
            using (writer.Block())
            {
                writer.AppendLine("FlushSegment();");
                writer.AppendLine("if (segVisits != null)");
                using (writer.Block())
                {
                    writer.AppendIndented("segVisits.Record(\"")
                        .Append(StringHelpers.Escape(binding.StationName))
                        .Append("\", HopOutcome.Red, segIndex + ")
                        .Append(step)
                        .Append(", StationVisit.ElapsedSince(segStarted")
                        .Append(step)
                        .Append("));");
                    writer.EndLine();
                    for (var later = step + 1; later < steps.Count; later++)
                    {
                        writer.AppendIndented("segVisits.Record(\"")
                            .Append(StringHelpers.Escape(steps[later].StationName))
                            .Append("\", HopOutcome.Skipped, segIndex + ")
                            .Append(later)
                            .Append(", global::System.TimeSpan.Zero);");
                        writer.EndLine();
                    }
                }

                writer.AppendIndented("return RailwaySignals.Red(new SignalIssue(\"STATION_EXCEPTION\", \"Unhandled station exception: \" + segException")
                    .Append(step)
                    .Append(".Message, \"")
                    .Append(StringHelpers.Escape(binding.StationName))
                    .Append("\", segException")
                    .Append(step)
                    .Append("));");
                writer.EndLine();
            }
        }

        private static List<WagonSlot> CollectSlots(List<ChainSiteBinding> steps)
        {
            var slots = new List<WagonSlot>();
            var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var step = 0; step < steps.Count; step++)
            {
                var binding = steps[step];
                var wagons = binding.Schema.Wagons;
                for (var i = 0; i < wagons.Length; i++)
                {
                    AddSlot(slots, indexByName, wagons[i].Name, wagons[i].TypeDisplay);
                }

                var members = binding.Schema.ReturnShape?.Members ?? ImmutableArray<WagonBinding>.Empty;
                var returns = binding.ReturnMembers;
                for (var i = 0; i < returns.Length; i++)
                {
                    AddSlot(slots, indexByName, returns[i], TypeOfReturn(members, returns[i]));
                }
            }

            return slots;
        }

        private static void AddSlot(List<WagonSlot> slots, Dictionary<string, int> indexByName, string name, string typeDisplay)
        {
            if (string.IsNullOrEmpty(name) || indexByName.ContainsKey(name))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(typeDisplay))
            {
                typeDisplay = "global::System.Object";
            }

            indexByName.Add(name, slots.Count);
            slots.Add(new WagonSlot { Name = name, TypeDisplay = typeDisplay });
        }

        private static string TypeOfReturn(ImmutableArray<WagonBinding> members, string name)
        {
            for (var i = 0; i < members.Length; i++)
            {
                if (string.Equals(members[i].Name, name, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(members[i].TypeDisplay))
                {
                    return members[i].TypeDisplay;
                }
            }

            return "global::System.Object";
        }

        private static Dictionary<string, int> IndexByName(List<WagonSlot> slots)
        {
            var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < slots.Count; i++)
            {
                indexByName[slots[i].Name] = i;
            }

            return indexByName;
        }

        private static int CompareStationIndex(ChainSiteBinding left, ChainSiteBinding right)
        {
            return left.StationIndex.CompareTo(right.StationIndex);
        }

        private static int CompareRuns(SegmentRun left, SegmentRun right)
        {
            var chain = string.CompareOrdinal(left.ChainId, right.ChainId);
            return chain != 0 ? chain : left.StartIndex.CompareTo(right.StartIndex);
        }

        private sealed class SegmentRun
        {
            public string ChainId;

            public int StartIndex;

            public List<ChainSiteBinding> Steps;

            public string MethodName;

            public int LastIndex => StartIndex + Steps.Count - 1;
        }

        private sealed class WagonSlot
        {
            public string Name;

            public string TypeDisplay;
        }
    }
}
