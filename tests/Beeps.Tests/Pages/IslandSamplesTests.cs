using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Beeps.Features.Accounts;
using Beeps.Features.Admin;
using Beeps.Features.Agents;
using Beeps.Features.Beeps;
using Beeps.Features.Media;
using Beeps.Features.Moderation;
using Beeps.Features.Notifications;
using Beeps.Features.Profiles;
using Beeps.Features.Search;
using Beeps.Features.Shared;
using Beeps.Features.Timelines;
using Beeps.Pages.Islands;
using Beeps.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Beeps.Tests.Pages;

/// <summary>
/// Signed-out pages are drawn twice: by the server (Razor), and again by the app's Preact components over it (PLAN 6.9).
/// The two must be the same markup, or the page changes as the script takes over. These tests draw the pages with fixed
/// data and keep, for every island, what the server drew and the props it was drawn from, in <c>Pages/IslandSamples</c>.
/// The client's tests (<c>Client/app/islands.test.tsx</c>) draw the same props with the components and compare. Signed
/// in the server draws no markup in islands, and an app page is its layout (an app shell) and its data; their props are
/// kept all the same, in the order the page has them, for the components to be drawn from.
/// </summary>
/// <remarks>
/// A changed template (or props) changes the samples: run the tests with <c>BEEPS_UPDATE_SAMPLES=1</c> to write them
/// anew, check the difference, then <c>npm test</c> to see the components still match.
/// </remarks>
public sealed partial class IslandSamplesTests : IDisposable
{
    private static readonly string SamplesPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/Beeps.Tests/Pages/IslandSamples"));

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly BeepsWebApplicationFactory factory = new() { Time = new FixedTimeProvider(TestData.Now) };

    private readonly User tom;
    private readonly ActorRow alice = TestBeeps.Human("alice", Id(1));
    private readonly ActorRow aliceAgent;
    private readonly BeepRow question;
    private readonly BeepRow answer;
    private readonly BeepRow pictures;
    private readonly BeepRow mine;

    public IslandSamplesTests()
    {
        tom = WebAuth.ArrangeUser(factory);
        tom.Actor.Bio = "about me";
        aliceAgent = new ActorRow(Id(2), ActorKind.Agent, "alice.claude", "alice's Claude", alice.Id);
        var tomRow = new ActorRow(tom.Id, ActorKind.Human, "tom", "tom", null);
        question = Beep(10, "Who knows #beeps? @tom", alice, likes: 3, replies: 1);
        answer = Beep(11, "I do!\nSecond line", aliceAgent, replyTo: question.Id, root: question.Id, liked: true, likes: 1);
        pictures = Beep(12, "", alice);
        mine = Beep(13, "<b>mine</b>", tomRow) with { UnderReview = true };
        TestBeeps.Arrange(factory, question, answer, pictures, mine);
        factory.Beeps.GetMentionsAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<ActorRow>> { [question.Id] = [tomRow] });
        factory.Beeps.GetMediaAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, IReadOnlyList<MediaRow>>
        {
            [pictures.Id] = [new MediaRow(Id(20), "pic/one", 800, 600), new MediaRow(Id(21), "pic/two", 600, 800)],
        });
        factory.Counters.GetAsync(default!, TestSupport.Ct).ReturnsForAnyArgs(new Dictionary<Guid, BeepCounts>());
        var all = new CursorPage<BeepRow>([mine, pictures, answer, question], Id(99));
        factory.Timelines.HomeAsync(tom.Id, Arg.Any<PageRequest>(), tom.Id, Arg.Any<CancellationToken>()).Returns(all);
        factory.Timelines.PublicAsync(Arg.Any<PageRequest>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([pictures, question], null));
        factory.Timelines.HashtagAsync("beeps", Arg.Any<PageRequest>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([], null));
        factory.Timelines.ProfileAsync(alice.Id, Arg.Any<ProfileTimeline>(), Arg.Any<PageRequest>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([pictures, question], Id(98)));
        factory.Timelines.ProfileAsync(aliceAgent.Id, Arg.Any<ProfileTimeline>(), Arg.Any<PageRequest>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([answer], null));
        factory.Beeps.ListThreadAsync(question.Id, Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(new CursorPage<BeepRow>([question, answer], Id(97)));
        factory.Profiles.FindByHandleAsync("alice", Arg.Any<CancellationToken>()).Returns(new ProfileRow(alice, "Hello, I am Alice.", null, TestData.Now, null));
        factory.Profiles.FindByHandleAsync("alice.claude", Arg.Any<CancellationToken>()).Returns(new ProfileRow(aliceAgent, null, "Claude Opus", TestData.Now, alice));
        factory.Profiles.ListAgentsAsync(alice.Id, Arg.Any<CancellationToken>()).Returns([aliceAgent]);
        factory.Profiles.ListAgentsAsync(aliceAgent.Id, Arg.Any<CancellationToken>()).Returns([]);
        factory.Profiles.IsFollowingAsync(tom.Id, alice.Id, Arg.Any<CancellationToken>()).Returns(true);
        factory.Notifications.ListAsync(tom.Id, Arg.Any<PageRequest>(), false, Arg.Any<CancellationToken>()).Returns(new CursorPage<NotificationRow>(
            [
                new NotificationRow(Id(30), NotificationType.Reply, aliceAgent, answer.Id, TestData.Now, null),
                new NotificationRow(Id(31), NotificationType.Mention, alice, question.Id, TestData.Now.AddDays(-2), TestData.Now),
                new NotificationRow(Id(32), NotificationType.Like, alice, mine.Id, TestData.Now.AddDays(-9), TestData.Now, Others: 2, Earlier: [aliceAgent]),
            ],
            Id(96)));
        factory.SearchIndex.SearchBeepsAsync("beeps", null, 0, SearchService.DefaultLimit, Arg.Any<CancellationToken>()).Returns(new SearchHits([question.Id, answer.Id], 30));
        factory.SearchIndex.SearchActorsAsync("alice", 0, SearchService.DefaultLimit, Arg.Any<CancellationToken>()).Returns(new SearchHits([alice.Id, aliceAgent.Id], 2));
        factory.SearchStore.GetActorsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new ActorSearchRow(alice, "Hello, I am Alice."), new ActorSearchRow(aliceAgent, null)]);
        factory.Agents.ListAgentsAsync(tom.Id, Arg.Any<CancellationToken>()).Returns(
        [
            new AgentRow(Id(40), "tom.claude", "tom's Claude", "Claude Opus", TestData.Now, null, "beeps_abc123", TestData.Now, TestData.Now.AddHours(-3), "I help."),
            new AgentRow(Id(41), null, null, null, TestData.Now, null, "beeps_def456", TestData.Now, null),
            new AgentRow(Id(42), "tom.gpt", "tom's GPT", "GPT", TestData.Now, TestData.Now, null, null, null),
        ]);
    }

    public static TheoryData<string, string> Pages => new()
    {
        { "home", "/" },
        { "public", "/public" },
        { "tag", "/tags/beeps" },
        { "thread", "/beeps/00000000-0000-7000-8000-00000000000b" },
        { "profile", "/alice" },
        { "profile-agent", "/alice.claude/replies" },
        { "notifications", "/notifications" },
        { "search", "/search" },
        { "search-beeps", "/search?q=beeps" },
        { "search-people", "/search?q=alice&type=users" },
        { "compose", "/compose" },
        { "compose-reply", "/compose?replyTo=00000000-0000-7000-8000-00000000000a" },
        { "report", "/beeps/00000000-0000-7000-8000-00000000000a/report" },
        { "settings", "/settings" },
        { "settings-profile", "/settings/profile" },
        { "settings-account", "/settings/account" },
        { "settings-email", "/settings/email" },
        { "settings-password", "/settings/password" },
        { "settings-display", "/settings/display" },
        { "agents", "/agents" },
        { "agent", "/agents/00000000-0000-7000-8000-000000000028" },
        { "agent-waiting", "/agents/00000000-0000-7000-8000-000000000029" },
        { "connect", "/onboarding/connect/00000000-0000-7000-8000-000000000029" },
        { "connected", "/onboarding/connect/00000000-0000-7000-8000-000000000028" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task ThePageMatchesItsSample(string name, string url)
    {
        using var client = await WebAuth.SignInAsync(factory, tom);

        var html = await DrawnAsync(client, url);

        await MatchSampleAsync(name, url, html);
    }

    public static TheoryData<string, string> SignedOutPages => new()
    {
        { "signed-out-home", "/" },
        { "signed-out-public", "/public" },
        { "signed-out-tag", "/tags/beeps" },
        { "signed-out-thread", "/beeps/00000000-0000-7000-8000-00000000000b" },
        { "signed-out-profile", "/alice" },
        { "signed-out-search", "/search?q=beeps" },
        { "signed-out-login", "/login" },
        { "signed-out-register", "/register" },
    };

    [Theory]
    [MemberData(nameof(SignedOutPages))]
    public async Task ASignedOutPageMatchesItsSample(string name, string url)
    {
        using var client = WebAuth.NewClient(factory);

        var html = await client.GetStringAsync(new Uri(url, UriKind.Relative), TestSupport.Ct);

        // Nothing personal, nothing that would keep the page out of the output cache (the way-in pages post, and are not cached).
        if (!name.EndsWith("login", StringComparison.Ordinal) && !name.EndsWith("register", StringComparison.Ordinal))
        {
            Assert.DoesNotContain("__RequestVerificationToken", html, StringComparison.Ordinal);
        }
        await MatchSampleAsync(name, url, html);
    }

    [Fact]
    public async Task TheVerificationPageMatchesItsSample()
    {
        var ann = WebAuth.ArrangeUser(factory, "ann", verified: false);
        using var client = await WebAuth.SignInAsync(factory, ann);

        var html = await DrawnAsync(client, "/onboarding/verify");

        await MatchSampleAsync("verify", "/onboarding/verify", html);
    }

    [Fact]
    public async Task ARefusedFormMatchesItsSample()
    {
        using var client = await WebAuth.SignInAsync(factory, tom);

        using var compose = await WebAuth.PostFormAsync(client, "/compose", "/compose", ("text", ""));
        using var profile = await WebAuth.PostFormAsync(client, "/settings", "/settings?handler=Profile", ("DisplayName", ""), ("Bio", "about me"));

        await MatchSampleAsync("compose-refused", "/compose", await compose.Content.ReadAsStringAsync(TestSupport.Ct));
        await MatchSampleAsync("settings-refused", "/settings?handler=Profile", await profile.Content.ReadAsStringAsync(TestSupport.Ct));
    }

    [Fact]
    public async Task ANewKeyMatchesItsSample()
    {
        using var client = await WebAuth.SignInAsync(factory, tom);

        using var created = await WebAuth.PostFormAsync(client, "/agents", "/agents?handler=Create");
        using var onboarding = await WebAuth.PostFormAsync(client, "/onboarding/agent", "/onboarding/agent");

        await MatchSampleAsync("agents-new-key", "/agents?handler=Create", await created.Content.ReadAsStringAsync(TestSupport.Ct));
        await MatchSampleAsync("onboarding-key", "/onboarding/agent", await onboarding.Content.ReadAsStringAsync(TestSupport.Ct));
    }

    public static TheoryData<string> AdminPages => ["overview", "users", "user", "agents", "review", "review-beep", "system", "settings", "audit"];

    [Theory]
    [MemberData(nameof(AdminPages))]
    public async Task AnAdminPageMatchesItsSample(string page)
    {
        ArrangeAdmin();
        using var client = await WebAuth.SignInAsync(factory, tom);
        var url = page switch
        {
            "overview" => "/admin",
            "user" => $"/admin/users/{tom.Id:D}",
            "review-beep" => $"/admin/beeps/{Id(50):D}",
            _ => $"/admin/{page}",
        };

        var html = await DrawnAsync(client, url);

        await MatchSampleAsync($"admin-{page}", url, html);
    }

    public void Dispose() => factory.Dispose();

    private void ArrangeAdmin()
    {
        tom.IsAdmin = true;
        factory.Admin.IsActiveAdminAsync(tom.Id, Arg.Any<CancellationToken>()).Returns(true);
        var tomRow = new AdminUserRow(tom.Id, "tom", "tom", "tom@example.com", true, true, TestData.Now, null, null);
        factory.Admin.GetUserAsync(tom.Id, Arg.Any<CancellationToken>()).Returns(tomRow);
        factory.Admin.ListUsersAsync(null, null, null, AdminService.PageSize, Arg.Any<CancellationToken>()).Returns(new CursorPage<AdminUserRow>(
            [tomRow, new AdminUserRow(alice.Id, "alice", "Alice", "alice@example.com", false, false, TestData.Now, TestData.Now, null)],
            Id(95)));
        factory.Admin.ListAgentsAsync(null, null, AdminService.PageSize, Arg.Any<CancellationToken>()).Returns(new CursorPage<AdminAgentRow>(
            [
                new AdminAgentRow(aliceAgent.Id, "alice.claude", "alice's Claude", "Claude Opus", alice.Id, "alice", TestData.Now, null, null, "beeps_abc123", TestData.Now),
                new AdminAgentRow(Id(43), null, null, null, tom.Id, "tom", TestData.Now, TestData.Now, null, null, null),
            ],
            null));
        factory.System.CountAsync(default, TestSupport.Ct).ReturnsForAnyArgs(new SiteCounts(12, 34, 5678, 1, 2, 3, 4, 5, 6));
        factory.Moderation.CountQueueAsync(Arg.Any<CancellationToken>()).Returns(9);
        var result = new ModerationResultRow(Id(51), "google/gemma", "via-provider", ModerationDecision.Hide, [ModerationCategories.Spam], ModerationSeverity.Medium, "Looks like spam.", 420, 99, TestData.Now);
        factory.Moderation.ListQueueAsync(null, ReviewService.PageSize, Arg.Any<CancellationToken>()).Returns(new CursorPage<ReviewQueueRow>(
            [new ReviewQueueRow(Id(50), "<b>buy</b> now", aliceAgent, ModerationStatus.Hidden, TestData.Now, 2, result)],
            null));
        factory.Moderation.GetReviewBeepAsync(Id(50), Arg.Any<CancellationToken>()).Returns(new ReviewBeepRow(
            Id(50),
            "<b>buy</b> now",
            aliceAgent,
            "alice",
            question.Id,
            ModerationStatus.Hidden,
            TestData.Now,
            null,
            new ReviewContextRow(question.Id, question.Text, alice, null),
            [new MediaRow(Id(52), "abc", 800, 600)],
            [result],
            [new ReportRow(Id(53), tom.Id, "tom", ModerationCategories.Spam, "same link every hour", TestData.Now, null)]));
        factory.System.ListDeadJobsAsync(null, SystemService.PageSize, Arg.Any<CancellationToken>()).Returns(new CursorPage<DeadJobRow>(
            [new DeadJobRow(Id(54), "media.process", 8, "IOException: disk full", TestData.Now, TestData.Now)],
            null));
        factory.System.ListScheduledJobsAsync(Arg.Any<CancellationToken>()).Returns(
            [new ScheduledJobRow("accounts.cleanup", TestData.Now, TestData.Now, "NpgsqlException: timeout"), new ScheduledJobRow("media.cleanup", null, null, null)]);
        factory.Admin.ListInvitesAsync(default, TestSupport.Ct).ReturnsForAnyArgs(
            [new InviteRow(Id(55), "ABCD", TestData.Now, TestData.Now.AddDays(7), null, null), new InviteRow(Id(56), "EFGH", TestData.Now, TestData.Now.AddDays(7), TestData.Now, "alice")]);
        factory.Admin.ListAuditLogAsync(null, AdminService.PageSize, Arg.Any<CancellationToken>()).Returns(new CursorPage<AuditLogRow>(
            [
                new AuditLogRow(Id(57), tom.Id, "tom", AuditActions.DisableAgent, AuditTargets.Agent, aliceAgent.Id, "alice.claude", null, TestData.Now, alice.Id),
                new AuditLogRow(Id(58), tom.Id, "tom", AuditActions.DeleteBeep, AuditTargets.Beep, Id(50), null, "Hidden, reports resolved: 1", TestData.Now),
            ],
            Id(94)));
    }

    /// <summary>Ids that come out the same every run: <c>00000000-0000-7000-8000-0000000000NN</c>.</summary>
    private static Guid Id(int n) => Guid.Parse($"00000000-0000-7000-8000-{n:x12}");

    private static BeepRow Beep(int n, string text, ActorRow author, Guid? replyTo = null, Guid? root = null, bool liked = false, int likes = 0, int replies = 0) =>
        new(Id(n), text, author, replyTo, root ?? Id(n), TestData.Now.AddMinutes(-n), likes, replies, liked);

    /// <summary>
    /// An app page as the app shows it signed in: the app shell, its content column from the page's data, with each
    /// island's props where the page has the island, and the app's config.
    /// </summary>
    private static async Task<string> DrawnAsync(HttpClient client, string url)
    {
        var shell = await client.GetStringAsync(new Uri(url, UriKind.Relative), TestSupport.Ct);
        using var data = JsonDocument.Parse(await Islands.PageAsync(client, url));
        var islands = data.RootElement.GetProperty("islands");
        var page = data.RootElement.GetProperty("page");
        var main = EmptyIslandPattern().Replace(
            page.GetProperty("main").GetString()!,
            m => $"<div class=\"island\" data-island=\"{m.Groups["name"].Value}\"><script data-island-props=\"\" type=\"application/json\">{islands.GetProperty(m.Groups["name"].Value).GetRawText()}</script></div>");
        Assert.Matches(ShellMainPattern(), shell);
        return ShellMainPattern().Replace(shell, m => m.Groups[1].Value + main + m.Groups[2].Value, 1)
            .Replace("</body>", $"<script type=\"application/json\" id=\"app-config\">{page.GetProperty("config").GetRawText()}</script></body>", StringComparison.Ordinal);
    }

    private async Task MatchSampleAsync(string name, string url, string html)
    {
        var config = JsonNode.Parse(AppConfigPattern().Match(html).Groups[1].Value)!;
        // The interface text the config names (ClientStrings), as the app fetches it.
        var culture = StringsCulturePattern().Match(config["stringsUrl"]!.GetValue<string>()).Groups[1].Value;
        var sample = new JsonObject
        {
            ["url"] = url,
            ["config"] = config,
            ["strings"] = JsonNode.Parse(factory.Services.GetRequiredService<ClientStrings>().For(culture).Json),
            ["islands"] = new JsonArray([.. Extract(html)]),
        };
        // What differs from run to run (new users' ids, tokens) is replaced the same way throughout the sample.
        var text = Stable(sample.ToJsonString(Pretty)) + "\n";
        var path = Path.Combine(SamplesPath, $"{name}.json");
        if (Environment.GetEnvironmentVariable("BEEPS_UPDATE_SAMPLES") == "1")
        {
            Directory.CreateDirectory(SamplesPath);
            await File.WriteAllTextAsync(path, text, TestSupport.Ct);
            return;
        }

        Assert.True(File.Exists(path), $"No sample {name}.json: run the tests with BEEPS_UPDATE_SAMPLES=1.");
        var saved = await File.ReadAllTextAsync(path, TestSupport.Ct);
        Assert.True(saved == text, $"The page's islands changed ({name}.json): run the tests with BEEPS_UPDATE_SAMPLES=1, check the difference, then npm test.");
    }

    /// <summary>Each island: its name, its envelope, and the markup the server drew inside it.</summary>
    private static IEnumerable<JsonNode> Extract(string html)
    {
        foreach (Match match in IslandStartPattern().Matches(html))
        {
            var tag = match.Groups["tag"].Value;
            var start = match.Index + match.Length;
            var depth = 1;
            var position = start;
            var tags = new Regex($"<(/?){tag}\\b");
            while (depth > 0)
            {
                var next = tags.Match(html, position);
                Assert.True(next.Success, $"The island \"{match.Groups["name"].Value}\" is not closed.");
                depth += next.Groups[1].Value == "/" ? -1 : 1;
                position = next.Index + next.Length;
                if (depth == 0)
                {
                    position = next.Index;
                }
            }

            var envelope = JsonNode.Parse(match.Groups["json"].Value)!.AsObject();
            var inner = html[start..position].Trim();
            // A skeleton stands in until the component draws the island (_Skeleton.cshtml): not markup it draws itself.
            var skeleton = SkeletonPattern().Match(inner);
            var island = new JsonObject
            {
                ["name"] = match.Groups["name"].Value,
                ["props"] = envelope["props"]?.DeepClone(),
                ["errors"] = envelope["errors"]?.DeepClone(),
                ["html"] = skeleton.Success ? "" : inner,
            };
            if (skeleton.Success)
            {
                island["skeleton"] = skeleton.Groups[1].Value;
            }

            yield return island;
        }
    }

    private static string Stable(string text)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        text = GuidPattern().Replace(text, m =>
        {
            // The fixed ids stay as they are; others are numbered as they come.
            if (m.Value.StartsWith("00000000-0000-7000-8000-", StringComparison.Ordinal))
            {
                return m.Value;
            }

            if (!ids.TryGetValue(m.Value, out var stable))
            {
                stable = $"10000000-0000-7000-8000-{ids.Count + 1:x12}";
                ids[m.Value] = stable;
            }

            return stable;
        });
        text = TokenPattern().Replace(text, m => m.Groups[1].Value + "TOKEN" + m.Groups[2].Value);
        // A new key, and the prefix it is listed by.
        foreach (var key in KeyPattern().Matches(text).Select(m => m.Value).Distinct().ToList())
        {
            text = text.Replace(key, "beeps_NEWKEY", StringComparison.Ordinal).Replace(AgentKeys.DisplayPrefix(key), "beeps_NEW", StringComparison.Ordinal);
        }

        return text;
    }


    [GeneratedRegex("<script type=\"application/json\" id=\"app-config\">(.*?)</script>")]
    private static partial Regex AppConfigPattern();

    [GeneratedRegex("<div class=\"island\" data-island=\"(?<name>[a-z-]+)\"></div>")]
    private static partial Regex EmptyIslandPattern();

    [GeneratedRegex("(<main id=\"main\" class=\"shell-main\" data-app-shell>).*?(</main>)", RegexOptions.Singleline)]
    private static partial Regex ShellMainPattern();

    [GeneratedRegex("^/strings/([A-Za-z-]+)\\.json\\?v=")]
    private static partial Regex StringsCulturePattern();

    [GeneratedRegex("^<div class=\"skeleton-page\" data-skeleton=\"([a-z]+)\"")]
    private static partial Regex SkeletonPattern();

    [GeneratedRegex("<(?<tag>div|span) class=\"island\" data-island=\"(?<name>[a-z-]+)\"><script data-island-props=\"\" type=\"application/json\">(?<json>.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex IslandStartPattern();

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase)]
    private static partial Regex GuidPattern();

    // In the markup (value="…") and in the JSON (\"token\": \"…\", escaped once more inside the sample).
    [GeneratedRegex("(__RequestVerificationToken\\\\\" type=\\\\\"hidden\\\\\" value=\\\\\"|\"token\": \")[^\"\\\\]+(\\\\?\")")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("beeps_[A-Za-z0-9_-]{20,}")]
    private static partial Regex KeyPattern();
}
