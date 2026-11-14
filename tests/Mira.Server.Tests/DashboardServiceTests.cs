using System.Net;
using Mira.Server.GitHub;
using Mira.Server.Services;
using Mira.Server.Tests.Support;

namespace Mira.Server.Tests;

public class DashboardServiceTests
{
    private const string AliceToken = "t-alice";
    private const string BobToken = "t-bob";

    private static HttpResponseMessage UserFor(HttpRequestMessage request) =>
        Gh.Json($$"""{"login":"{{(request.Headers.Authorization!.Parameter == BobToken ? "bob" : "alice")}}"}""");

    /// <summary>Un GitHub che risponde a ogni richiesta della dashboard; il test cambia solo ciò che gli interessa.</summary>
    private static Router Github() => new Router()
        .On(path => path == "user", UserFor)
        .OnGraphQl("repositories(", () => Gh.Json(Fixtures.Repositories([])))
        .OnGraphQl("reviewDecision", () => Gh.Json(Fixtures.NoReviews))
        .On("notifications", () => Gh.Json("[]"))
        .On("search/issues", () => Gh.Json(Fixtures.Search(0)));

    private static DashboardService Service(Router router, out FakeHandler handler, params (string, string)[] accounts)
    {
        handler = router.Handler();
        return new DashboardService(new InMemorySecretStore(accounts.Length == 0 ? [("alice", AliceToken)] : accounts), Gh.Api(handler));
    }

    [Fact]
    public async Task Each_search_feeds_its_own_section()
    {
        var router = Github()
            .On("review-requested:alice", () => Gh.Json(Fixtures.Search(1, Fixtures.Issue(1, "da rivedere", pr: true))))
            .On("mentions:alice", () => Gh.Json(Fixtures.Search(1, Fixtures.Issue(2, "citato"))));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync(force: false, CancellationToken.None);

        var review = Assert.Single(snapshot.Items, i => i.Section == "review");
        Assert.Equal("pr", review.Type);
        Assert.Equal("da rivedere", review.Title);
        Assert.Equal("alice", review.Account);
        Assert.Equal("acme/app", review.Repo);
        Assert.Equal("issue", Assert.Single(snapshot.Items, i => i.Section == "mentions").Type);
    }

    [Fact]
    public async Task Items_from_all_accounts_are_merged_and_sorted_newest_first()
    {
        var router = Github().On("review-requested:", () => Gh.Json(Fixtures.Search(1,
            Fixtures.Issue(1, "vecchia", pr: true, updatedAt: "2026-11-01T00:00:00Z"))))
            .On(path => path.Contains("assignee:bob"), _ => Gh.Json(Fixtures.Search(1,
                Fixtures.Issue(2, "recente", updatedAt: "2026-11-12T00:00:00Z"))));
        var service = Service(router, out _, ("alice", AliceToken), ("bob", BobToken));

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal(new[] { "alice", "bob" }, snapshot.Accounts.Select(a => a.Login));
        var titles = snapshot.Items.Select(i => i.Title).ToList();
        Assert.True(titles.IndexOf("recente") < titles.IndexOf("vecchia"));
    }

    [Fact]
    public async Task The_real_total_is_kept_when_github_has_more_results_than_the_page()
    {
        var router = Github().On("review-requested:alice", () => Gh.Json(Fixtures.Search(213, Fixtures.Issue(1, "x", pr: true))));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal(213, snapshot.Totals.Single(t => t.Section == "review").Total);
        Assert.Single(snapshot.Items, i => i.Section == "review");
    }

    [Fact]
    public async Task The_total_is_never_lower_than_the_items_actually_shown()
    {
        var router = Github().On("review-requested:alice", () => Gh.Json(Fixtures.Search(0, Fixtures.Issue(1, "x", pr: true))));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal(1, snapshot.Totals.Single(t => t.Section == "review").Total);
    }

    [Fact]
    public async Task A_second_call_within_the_cache_window_does_not_call_github_again()
    {
        var service = Service(Github(), out var handler);
        await service.GetAsync(false, CancellationToken.None);
        var firstRound = handler.Count;

        var again = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal(firstRound, handler.Count);
        Assert.NotNull(again);
    }

    [Fact]
    public async Task A_forced_refresh_right_after_a_load_is_still_served_from_the_cache()
    {
        // Rete di sicurezza contro chi preme «Aggiorna» a ripetizione: il minimo è di 5 secondi.
        var service = Service(Github(), out var handler);
        await service.GetAsync(false, CancellationToken.None);
        var firstRound = handler.Count;

        await service.GetAsync(force: true, CancellationToken.None);

        Assert.Equal(firstRound, handler.Count);
    }

    [Fact]
    public async Task Invalidate_makes_the_next_call_reload_everything()
    {
        var service = Service(Github(), out var handler);
        await service.GetAsync(false, CancellationToken.None);
        var firstRound = handler.Count;

        service.Invalidate();
        await service.GetAsync(false, CancellationToken.None);

        Assert.Equal(firstRound * 2, handler.Count);
    }

    [Fact]
    public async Task An_account_with_an_expired_token_is_marked_invalid_without_an_extra_error()
    {
        var router = Github().On(path => path == "user", _ => Gh.Status(HttpStatusCode.Unauthorized));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal("invalid", Assert.Single(snapshot.Accounts).State);
        Assert.Empty(snapshot.Errors);
        Assert.Empty(snapshot.Items);
    }

    [Fact]
    public async Task One_account_failing_does_not_hide_the_other()
    {
        var router = Github()
            .On(path => path == "user", request => request.Headers.Authorization!.Parameter == AliceToken
                ? Gh.Status(HttpStatusCode.InternalServerError)
                : UserFor(request))
            .On("assignee:bob", () => Gh.Json(Fixtures.Search(1, Fixtures.Issue(7, "di bob"))));
        var service = Service(router, out _, ("alice", AliceToken), ("bob", BobToken));

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        var error = Assert.Single(snapshot.Errors);
        Assert.Equal(("alice", "account"), (error.Account, error.Section));
        Assert.Equal("github-http", error.Error.Code);
        Assert.Contains(snapshot.Items, i => i.Title == "di bob");
    }

    [Fact]
    public async Task A_failing_section_is_reported_while_the_others_still_load()
    {
        var router = Github()
            .On("review-requested:alice", () => Gh.Status(HttpStatusCode.Forbidden))
            .On("assignee:alice", () => Gh.Json(Fixtures.Search(1, Fixtures.Issue(3, "assegnata"))));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        var error = Assert.Single(snapshot.Errors);
        Assert.Equal("review", error.Section);
        Assert.Equal("rate-limited", error.Error.Code);
        Assert.Contains(snapshot.Items, i => i.Section == "assigned");
        Assert.DoesNotContain(snapshot.Totals, t => t.Section == "review");
    }

    [Fact]
    public async Task Graphql_errors_without_data_are_reported_for_both_repository_sections()
    {
        var router = Github().On("graphql", () => Gh.Json("""{"data":null,"errors":[{"type":"RATE_LIMITED","message":"limite"}]}"""));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Equal(new[] { "repos", "reviews", "starred" }, snapshot.Errors.Select(e => e.Section).Order());
        Assert.All(snapshot.Errors, e => Assert.Equal("rate-limited", e.Error.Code));
    }

    [Fact]
    public async Task Repositories_carry_their_counts_and_a_logo_that_the_browser_can_reach()
    {
        var router = Github().OnGraphQl("repositories(", () => Gh.Json(Fixtures.Repositories(
        [
            Fixtures.Repo(1, "pubblico", customImage: true),
            Fixtures.Repo(2, "privato", isPrivate: true, customImage: true),
            Fixtures.Repo(3, "senza-immagine"),
        ])));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        var repos = snapshot.Items.Where(i => i.Section == "repos").ToDictionary(i => i.Title);
        Assert.Equal("https://og.example/pubblico.png", repos["pubblico"].LogoUrl);
        // Per un repository privato l'anteprima non si vede senza accesso: si usa l'avatar del proprietario.
        Assert.Equal("https://avatars.example/alice", repos["privato"].LogoUrl);
        Assert.Equal("https://avatars.example/alice", repos["senza-immagine"].LogoUrl);
        Assert.Equal((4, 2, 1), (repos["pubblico"].OpenIssues, repos["pubblico"].OpenPrs, repos["pubblico"].UnassignedIssues));
        Assert.Equal(3, snapshot.Totals.Single(t => t.Section == "repos").Total);
    }

    [Fact]
    public async Task A_full_page_of_repositories_means_the_total_is_not_known()
    {
        var hundred = Enumerable.Range(1, 100).Select(i => Fixtures.Repo(i, $"r{i}")).ToArray();
        var router = Github().OnGraphQl("repositories(", () => Gh.Json(Fixtures.Repositories(hundred)));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Null(snapshot.Totals.Single(t => t.Section == "repos").Total);
    }

    [Fact]
    public async Task Notification_links_point_to_the_web_page_not_to_the_api()
    {
        var router = Github().On("notifications", () => Gh.Json("[" + string.Join(",",
            Fixtures.Notification("1", "una PR", "PullRequest", "https://api.github.com/repos/acme/app/pulls/42"),
            Fixtures.Notification("2", "una issue", "Issue", "https://api.github.com/repos/acme/app/issues/7"),
            Fixtures.Notification("3", "un commit", "Commit", "https://api.github.com/repos/acme/app/commits/abc123"),
            Fixtures.Notification("4", "una release", "Release", "https://api.github.com/repos/acme/app/releases/9"),
            Fixtures.Notification("5", "senza indirizzo", "CheckSuite", null)) + "]"));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        var urls = snapshot.Items.Where(i => i.Section == "notifications").ToDictionary(i => i.Title, i => (i.Url, i.Type));
        Assert.Equal(("https://github.com/acme/app/pull/42", "pr"), urls["una PR"]);
        Assert.Equal(("https://github.com/acme/app/issues/7", "issue"), urls["una issue"]);
        Assert.Equal(("https://github.com/acme/app/commit/abc123", "commit"), urls["un commit"]);
        Assert.Equal(("https://github.com/acme/app/releases", "release"), urls["una release"]);
        Assert.Equal(("https://github.com/acme/app", "other"), urls["senza indirizzo"]);
    }

    [Fact]
    public async Task A_full_page_of_notifications_means_the_total_is_not_known()
    {
        var fifty = Enumerable.Range(1, 50).Select(i => Fixtures.Notification(i.ToString(), $"n{i}", "Issue", null));
        var router = Github().On("notifications", () => Gh.Json("[" + string.Join(",", fifty) + "]"));
        var service = Service(router, out _);

        var snapshot = await service.GetAsync(false, CancellationToken.None);

        Assert.Null(snapshot.Totals.Single(t => t.Section == "notifications").Total);
    }

    [Fact]
    public void An_issue_becomes_an_item_with_its_type_draft_flag_and_assignees()
    {
        var issue = System.Text.Json.JsonSerializer.Deserialize<GhIssue>(
            Fixtures.Issue(9, "bozza", repo: "acme/web", pr: true, draft: true, comments: 4, assignees: ["alice", "bob"]),
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true })!;

        var item = DashboardService.ToItem("alice", "review", issue);

        Assert.Equal("alice:review:9", item.Id);
        Assert.Equal(("pr", "acme/web", true, 4), (item.Type, item.Repo, item.Draft, item.Comments));
        Assert.Equal(new[] { "alice", "bob" }, item.Assignees);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  breve  ", "breve")]
    public void A_short_description_is_trimmed_and_an_empty_one_is_dropped(string? body, string? expected)
    {
        var item = DashboardService.ToItem("a", "assigned", new GhIssue(1, 1, "t", "u", "open", null, 0, DateTimeOffset.UnixEpoch, null, null, null, body, null));

        Assert.Equal(expected, item.Description);
    }

    [Fact]
    public void A_long_description_is_cut_at_500_characters_with_an_ellipsis()
    {
        var item = DashboardService.ToItem("a", "assigned", new GhIssue(1, 1, "t", "u", "open", null, 0, DateTimeOffset.UnixEpoch, null, null, null, new string('x', 900), null));

        Assert.Equal(501, item.Description!.Length);
        Assert.EndsWith("…", item.Description);
    }
}

public class ReviewStateTests
{
    private const string Pr = "https://github.com/acme/app/pull/12";

    private static Router Github(string pullRequestsJson) => new Router()
        .On(path => path == "user", _ => Gh.Json(Gh.UserJson))
        .OnGraphQl("repositories(", () => Gh.Json(Fixtures.Repositories([])))
        .OnGraphQl("reviewDecision", () => Gh.Json(pullRequestsJson))
        .On("notifications", () => Gh.Json("[]"))
        .On("search/issues", () => Gh.Json(Fixtures.Search(0)))
        .On("author:alice", () => Gh.Json(Fixtures.Search(1, Fixtures.Issue(12, "la mia PR", repo: "acme/app", pr: true))));

    private static async Task<Mira.Server.Models.DashboardSnapshot> LoadAsync(Router router)
    {
        var service = new DashboardService(new InMemorySecretStore(("alice", "t")), Gh.Api(router.Handler()));
        return await service.GetAsync(false, CancellationToken.None);
    }

    [Fact]
    public async Task The_review_state_is_attached_to_the_users_own_prs_by_address()
    {
        var snapshot = await LoadAsync(Github(Fixtures.PullRequests(Fixtures.PullRequestState(Pr, "CHANGES_REQUESTED",
            reviews: [Fixtures.Review("APPROVED", "carla", "2026-11-09T10:00:00Z"), Fixtures.Review("CHANGES_REQUESTED", "dario", "2026-11-10T10:00:00Z")],
            comments: [Fixtures.Comment("dario", "2026-11-10T12:00:00Z")], commentCount: 7))));

        var review = Assert.Single(snapshot.Items, i => i.Section == "mine-prs" && i.Url == Pr).Review!;
        Assert.Equal("CHANGES_REQUESTED", review.Decision);
        Assert.Equal(new[] { "carla", "dario" }, review.Reviews.Select(r => r.Login));
        Assert.Equal(new[] { "APPROVED", "CHANGES_REQUESTED" }, review.Reviews.Select(r => r.State));
        Assert.Equal("dario", Assert.Single(review.Comments).Login);
        Assert.Equal(7, review.CommentCount);
    }

    [Fact]
    public async Task A_pr_without_a_required_review_has_no_decision()
    {
        var snapshot = await LoadAsync(Github(Fixtures.PullRequests(Fixtures.PullRequestState(Pr, decision: null))));

        var review = Assert.Single(snapshot.Items, i => i.Section == "mine-prs").Review!;
        Assert.Null(review.Decision);
        Assert.Empty(review.Reviews);
    }

    [Fact]
    public async Task A_deleted_author_does_not_break_anything()
    {
        var snapshot = await LoadAsync(Github(Fixtures.PullRequests(Fixtures.PullRequestState(Pr, "APPROVED",
            reviews: [Fixtures.Review("APPROVED", login: null)], comments: [Fixtures.Comment(login: null)]))));

        var review = Assert.Single(snapshot.Items, i => i.Section == "mine-prs").Review!;
        Assert.Null(Assert.Single(review.Reviews).Login);
        Assert.Null(Assert.Single(review.Comments).Login);
    }

    [Fact]
    public async Task Only_the_users_own_prs_get_a_review_state()
    {
        var router = Github(Fixtures.PullRequests(Fixtures.PullRequestState(Pr, "APPROVED")))
            .On("review-requested:alice", () => Gh.Json(Fixtures.Search(1, Fixtures.Issue(12, "la mia PR", repo: "acme/app", pr: true))));

        var snapshot = await LoadAsync(router);

        Assert.NotNull(Assert.Single(snapshot.Items, i => i.Section == "mine-prs").Review);
        Assert.Null(Assert.Single(snapshot.Items, i => i.Section == "review").Review);
    }

    [Fact]
    public async Task A_pr_that_graphql_does_not_know_is_left_without_a_review_state()
    {
        var snapshot = await LoadAsync(Github(Fixtures.NoReviews));

        Assert.Null(Assert.Single(snapshot.Items, i => i.Section == "mine-prs").Review);
        Assert.Empty(snapshot.Errors);
    }

    [Fact]
    public async Task If_the_review_request_fails_the_prs_still_show_and_the_problem_is_reported()
    {
        var router = Github(Fixtures.NoReviews).OnGraphQl("reviewDecision", () => Gh.Status(HttpStatusCode.Forbidden));

        var snapshot = await LoadAsync(router);

        Assert.Contains(snapshot.Items, i => i.Section == "mine-prs");
        var error = Assert.Single(snapshot.Errors);
        Assert.Equal(("alice", "reviews", "rate-limited"), (error.Account, error.Section, error.Error.Code));
    }

    [Fact]
    public async Task Graphql_errors_without_data_are_reported_as_a_review_problem()
    {
        var snapshot = await LoadAsync(Github("""{"data":null,"errors":[{"type":"RATE_LIMITED","message":"limite"}]}"""));

        var error = Assert.Single(snapshot.Errors);
        Assert.Equal(("reviews", "rate-limited"), (error.Section, error.Error.Code));
    }

    [Fact]
    public async Task The_review_query_asks_github_for_the_users_open_prs_only()
    {
        var router = Github(Fixtures.NoReviews);
        var handler = router.Handler();
        var service = new DashboardService(new InMemorySecretStore(("alice", "t")), Gh.Api(handler));

        await service.GetAsync(false, CancellationToken.None);

        var query = Assert.Single(handler.Requests, r => r.Body?.Contains("reviewDecision") == true).Body!;
        Assert.Contains("states: OPEN", query);
        Assert.Contains("viewer", query);
    }
}
