using Campfire.Data.Queries;
using Campfire.Web.Tests.Controllers.Messages;

namespace Campfire.Web.Tests.Upstream.U05;

/// <summary>
/// Upstream commit 0d5998f (#318): Query sidebar directs and shared rooms separately
/// (ports reference/test/controllers/users/sidebars_controller_test.rb).
/// </summary>
public sealed class SidebarsQueryTests : IDisposable
{
    const long david = 127326141;
    const string davidSession = "AxJs94fteQ5Autv2VrKsH68c";

    readonly MessagesApp messages = new(new DateTimeOffset(2026, 3, 2, 16, 0, 0, TimeSpan.Zero));

    public void Dispose() => messages.Dispose();

    [Fact]
    public async Task Directs_are_ordered_by_room_recency_not_name()
    {
        await messages.Database.ReadAsync(session =>
        {
            var directs = Memberships.VisibleForUserWithDirectRooms(session, david);
            Assert.NotEmpty(directs);
            // Verify that directs are ordered by updated_at descending
            for (var i = 0; i < directs.Count - 1; i++)
            {
                Assert.True(directs[i].Room.UpdatedAt >= directs[i + 1].Room.UpdatedAt,
                    $"Direct room {directs[i].Room.Id} ({directs[i].Room.UpdatedAt}) should be >= {directs[i + 1].Room.Id} ({directs[i + 1].Room.UpdatedAt})");
            }
            return true;
        }, TestContext.Current.CancellationToken);

        var response = await messages.SendAsync("GET", "/users/me/sidebar", messages.SignedIn(davidSession));
        Assert.Equal(200, response.Status);
    }

    [Fact]
    public async Task Shared_rooms_stay_ordered_by_name()
    {
        await messages.Database.ReadAsync(session =>
        {
            var shared = Memberships.VisibleForUserWithOrderedRoomWithoutDirectRooms(session, david);
            Assert.NotEmpty(shared);
            // Verify that shared rooms are ordered by name ascending
            for (var i = 0; i < shared.Count - 1; i++)
            {
                var comp = string.Compare(shared[i].Room.Name, shared[i + 1].Room.Name, StringComparison.OrdinalIgnoreCase);
                Assert.True(comp <= 0,
                    $"Shared room '{shared[i].Room.Name}' should be <= '{shared[i + 1].Room.Name}'");
            }
            return true;
        }, TestContext.Current.CancellationToken);

        var response = await messages.SendAsync("GET", "/users/me/sidebar", messages.SignedIn(davidSession));
        Assert.Equal(200, response.Status);
    }
}
