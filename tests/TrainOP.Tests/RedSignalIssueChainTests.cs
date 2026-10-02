using System;
using System.Collections.Generic;
using Xunit;

namespace TrainOP.Tests
{
    public sealed class RedSignalIssueChainTests
    {
        [Fact]
        public void red_signal_single_issue_exposes_one_entry_chain()
        {
            //Arrange
            var issue = new SignalIssue("ERR", "failed", "Validate");

            //Act
            var red = new RedSignal(issue);

            //Assert
            Assert.Same(issue, red.Issue);
            Assert.Single(red.Issues);
            Assert.Same(issue, red.Issues[0]);
            Assert.Empty(issue.Details);
        }

        [Fact]
        public void travel_nested_branch_preserves_sub_route_issue_chain()
        {
            //Arrange
            var route = new TrainRoute()
                .Station("Seed", () => new { channel = "fail" })
                .Station("Branch", (string channel) => DispatchBranch(channel));

            //Act
            var report = route.Travel();

            //Assert
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("BRANCH_FAILED", red.Issue.Code);
            Assert.Equal("Branch", red.Issue.StationName);
            Assert.Single(red.Issues);
            Assert.Same(red.Issue, red.Issues[0]);
            Assert.Equal("SUB_INVALID", red.Issue.Details["code"]);
            Assert.Equal("SubValidate", red.Issue.Details["station"]);
            Assert.Equal(report.FailureIssues, red.Issues);
        }

        [Fact]
        public void red_signal_issues_keep_array_order_and_stamp_only_empty_station_names()
        {
            //Arrange
            var boom = new InvalidOperationException("boom");
            var route = new TrainRoute()
                .Station("Validate", () => RailwaySignals.Red(new[]
                {
                    new SignalIssue("FIRST", "first reason", ""),
                    new SignalIssue("SECOND", "second reason", "SubValidate", boom),
                }));

            //Act
            var report = route.Travel();

            //Assert
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal(2, red.Issues.Count);
            Assert.Same(red.Issues[0], red.Issue);
            Assert.Equal("FIRST", red.Issue.Code);
            Assert.Equal("Validate", red.Issue.StationName);
            Assert.Equal("SECOND", red.Issues[1].Code);
            Assert.Equal("SubValidate", red.Issues[1].StationName);
            Assert.Same(boom, red.Issues[1].Exception);
        }

        [Fact]
        public void red_with_null_details_stores_an_empty_dictionary()
        {
            //Arrange
            var route = new TrainRoute()
                .Station("Validate", () => RailwaySignals.Red("INVALID", "nope", null));

            //Act
            var report = route.Travel();

            //Assert
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Empty(red.Issue.Details);
        }

        [Fact]
        public void red_issue_array_rejects_an_empty_array()
        {
            //Act
            var exception = Assert.Throws<ArgumentException>(() => RailwaySignals.Red(new SignalIssue[0]));

            //Assert
            Assert.Equal("issues", exception.ParamName);
        }

        [Fact]
        public void service_station_receives_full_issue_chain_via_red_signal()
        {
            //Arrange
            SignalIssue[] capturedIssues = null;
            var route = new TrainRoute()
                .Station("Seed", () => new { channel = "fail" })
                .Station("Branch", (string channel) => DispatchBranch(channel))
                .ServiceStation("Recovery", (RedSignal red) =>
                {
                    capturedIssues = new SignalIssue[red.Issues.Count];
                    for (var i = 0; i < red.Issues.Count; i++)
                    {
                        capturedIssues[i] = red.Issues[i];
                    }

                    return RailwaySignals.Red("CANNOT_RECOVER", "declined");
                });

            //Act
            var report = route.Travel();

            //Assert
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("CANNOT_RECOVER", red.Issue.Code);
            Assert.Equal("Recovery", red.Issue.StationName);
            Assert.Single(red.Issues);
            Assert.NotNull(capturedIssues);
            Assert.Single(capturedIssues);
            Assert.Equal("BRANCH_FAILED", capturedIssues[0].Code);
            Assert.Equal("SUB_INVALID", capturedIssues[0].Details["code"]);
        }

        [Fact]
        public void service_station_receives_issue_and_issues_without_red_signal_parameter()
        {
            //Arrange
            SignalIssue capturedIssue = null;
            IReadOnlyList<SignalIssue> capturedIssues = null;
            var route = new TrainRoute()
                .Station("Seed", () => new { channel = "fail" })
                .Station("Branch", (string channel) => DispatchBranch(channel))
                .ServiceStation("Recovery", (string channel, SignalIssue issue, IReadOnlyList<SignalIssue> issues) =>
                {
                    capturedIssue = issue;
                    capturedIssues = issues;
                    return RailwaySignals.Red("CANNOT_RECOVER", "declined");
                });

            //Act
            var report = route.Travel();

            //Assert
            var red = Assert.IsType<RedSignal>(report.TerminalSignal);
            Assert.Equal("CANNOT_RECOVER", red.Issue.Code);
            Assert.Single(red.Issues);
            Assert.NotNull(capturedIssue);
            Assert.Equal("BRANCH_FAILED", capturedIssue.Code);
            Assert.NotNull(capturedIssues);
            Assert.Single(capturedIssues);
            Assert.Same(capturedIssue, capturedIssues[0]);
        }

        private static object DispatchBranch(string channel)
        {
            var subRoute = new TrainRoute()
                .Station("Seed", () => new { channel })
                .Station("SubValidate", (string channel) =>
                    RailwaySignals.Red("SUB_INVALID", "sub-route stop"));

            var subReport = subRoute.Travel();
            if (!subReport.ReachedDestination)
            {
                var subIssue = subReport.FailureIssues[0];
                return RailwaySignals.Red(
                    "BRANCH_FAILED",
                    "branch did not complete",
                    new Dictionary<string, object>
                    {
                        ["code"] = subIssue.Code,
                        ["station"] = subIssue.StationName,
                    });
            }

            return new { channel };
        }
    }
}
