using Mira.Server.GitHub;
using Mira.Server.Services;

namespace Mira.Server.Tests;

public sealed class RepoPageTests
{
    private static GqlPageItem Item(int number, bool? draft = null, int? milestone = null) =>
        new(number, $"t{number}", $"https://github.com/o/r/{number}", DateTimeOffset.UnixEpoch, draft,
            new GqlActor("ann"), new GqlCount(2), new GqlActorList([new GqlActor("bob"), null]),
            milestone is null ? null : new GqlMilestoneRef(milestone.Value));

    [Fact]
    public void Build_maps_milestones_prs_and_issues_with_real_totals()
    {
        var repo = new GqlRepoPageRepo(
            "o/r", "https://github.com/o/r", "  una descrizione ", true,
            new GqlMilestoneList([
                new GqlMilestone(1, "v1", " ", null, "OPEN", "u1", 40.5, new GqlCount(3), new GqlCount(2)),
                new GqlMilestone(2, "v0", "vecchia", DateTimeOffset.UnixEpoch, "CLOSED", "u2", 100, new GqlCount(0), new GqlCount(5)),
                null,
            ]),
            new GqlPagedItems(120, [Item(7, draft: true), null]),
            new GqlPagedItems(80, [Item(9, milestone: 1)]));

        var page = RepoPageService.Build(repo);

        Assert.Equal("una descrizione", page.Description);
        Assert.True(page.Private);
        Assert.Equal(2, page.Milestones.Count);
        Assert.Null(page.Milestones[0].Description);
        Assert.Equal("open", page.Milestones[0].State);
        Assert.Equal("closed", page.Milestones[1].State);
        Assert.Equal(3, page.Milestones[0].OpenIssues);
        Assert.Equal(120, page.PrTotal);
        Assert.Single(page.Prs);
        Assert.True(page.Prs[0].Draft);
        Assert.Equal(80, page.IssueTotal);
        Assert.Equal(1, page.Issues[0].Milestone);
        Assert.Equal(["bob"], page.Issues[0].Assignees);
    }

    [Theory]
    [InlineData("Stellatinus", true)]
    [InlineData("Mira.web-2", true)]
    [InlineData("..", true)]
    [InlineData("a/b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Repo_parts_are_validated(string? value, bool valid) =>
        Assert.Equal(valid, RepoPageService.IsValidRepoPart(value));
}
