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
            Assert.Equal(2, red.Issues.Count);
            Assert.Equal("SUB_INVALID", red.Issues[0].Code);
            Assert.Equal("SubValidate", red.Issues[0].StationName);
            Assert.Equal("BRANCH_FAILED", red.Issues[1].Code);
            Assert.Equal(report.FailureIssues, red.Issues);
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
            Assert.NotNull(capturedIssues);
            Assert.Equal(2, capturedIssues.Length);
            Assert.Equal("SUB_INVALID", capturedIssues[0].Code);
            Assert.Equal("BRANCH_FAILED", capturedIssues[1].Code);
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
            Assert.Equal("CANNOT_RECOVER", Assert.IsType<RedSignal>(report.TerminalSignal).Issue.Code);
            Assert.NotNull(capturedIssue);
            Assert.Equal("BRANCH_FAILED", capturedIssue.Code);
            Assert.NotNull(capturedIssues);
            Assert.Equal(2, capturedIssues.Count);
            Assert.Equal("SUB_INVALID", capturedIssues[0].Code);
            Assert.Same(capturedIssue, capturedIssues[1]);
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
                return RailwaySignals.Red(
                    "BRANCH_FAILED",
                    "branch did not complete",
                    subReport.FailureIssues);
            }

            return new { channel };
        }
    }
}
