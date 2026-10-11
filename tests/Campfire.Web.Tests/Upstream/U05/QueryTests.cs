using Campfire.Data.Pagination;
using Campfire.Data.Queries;
using Campfire.Data.Records;
using static Campfire.Web.Tests.Upstream.U05.Seed;

namespace Campfire.Web.Tests.Upstream.U05;

/// <summary>
/// <c>paged?</c> without counting (d1f09b4), the refresh's replaced messages through the
/// updated-at index (1e0d353), a direct room with one query (87eafc0) and <c>User.visible_to</c>
/// (0f2a1da): reference/test/models/message_test.rb, message/pagination_test.rb,
/// rooms/direct_test.rb and user/bannable_test.rb's cases, on the default parity seed.
/// </summary>
public sealed class QueryTests : IDisposable
{
    readonly Controllers.Messages.MessagesApp app = App();

    public void Dispose() => app.Dispose();

    [Fact]
    public async Task Paged_tells_whether_a_room_has_more_than_a_page_of_messages()
    {
        Execute(app, $"""
            INSERT INTO rooms (name, type, creator_id, created_at, updated_at) VALUES ('Paging', 'Rooms::Closed', {David}, '2026-03-02 16:00:00', '2026-03-02 16:00:00');
            WITH RECURSIVE n(i) AS (SELECT 0 UNION ALL SELECT i + 1 FROM n WHERE i < {MessagePages.PageSize - 1})
            INSERT INTO messages (room_id, creator_id, client_message_id, created_at, updated_at)
            SELECT (SELECT MAX(id) FROM rooms), {David}, 'paging-' || i, '2026-03-02 16:00:00', '2026-03-02 16:00:00' FROM n;
            """);
        var room = (long)app.Scalar("SELECT MAX(id) FROM rooms")!;

        Assert.False(await app.Database.ReadAsync(session => MessagePages.IsPaged(session, room), Cancellation));

        Execute(app, $"INSERT INTO messages (room_id, creator_id, client_message_id, created_at, updated_at) VALUES ({room}, {David}, 'paging-next', '2026-03-02 16:00:00', '2026-03-02 16:00:00')");
        Assert.True(await app.Database.ReadAsync(session => MessagePages.IsPaged(session, room), Cancellation));
    }

    // The reference stubs PAGE_SIZE to 2 to see the newest page; the port's page is 40, so this
    // keeps the order: touched out of order, the messages still come back in creation order.
    [Fact]
    public async Task Messages_updated_since_a_time_are_the_newest_page_of_them_in_creation_order()
    {
        var oldest = await app.Database.ReadAsync(session => MessagePages.FirstPage(session, Designers), Cancellation);
        var (first, second, third) = (oldest[0].Id, oldest[1].Id, oldest[2].Id);
        foreach (var (id, at) in new[] { (third, "16:01:00"), (first, "16:01:01"), (second, "16:01:02") })
        {
            Execute(app, $"UPDATE messages SET updated_at = '2026-03-02 {at}' WHERE id = {id}");
        }

        var since = new DateTimeOffset(2026, 3, 2, 16, 0, 30, TimeSpan.Zero);
        var updated = await app.Database.ReadAsync(session => MessagePages.PageUpdatedSince(session, Designers, since, []), Cancellation);

        Assert.Equal([first, second, third], updated.Select(message => message.Id));
    }

    [Fact]
    public async Task Find_the_room_with_exactly_the_same_users()
    {
        var group = await FindOrCreateDirectAsync(David, Kevin, Jason);

        Assert.Equal(DavidAndKevin, await FindOrCreateDirectAsync(Kevin, David));
        Assert.Equal(group, await FindOrCreateDirectAsync(Jason, Kevin, David));
        Assert.Equal([David, Jz], await MembersAsync(await FindOrCreateDirectAsync(David, Jz)));
        Assert.Equal([Jason, Kevin], await MembersAsync(await FindOrCreateDirectAsync(Jason, Kevin)));
    }

    [Fact]
    public async Task Banned_users_are_visible_to_administrators_only()
    {
        Execute(app, $"UPDATE users SET status = 2 WHERE id = {Kevin}");

        var (toDavid, toJz) = await app.Database.ReadAsync(session => (
            Users.VisibleToOrderedWithRole(session, Users.Find(session, David)!, UserRole.Member),
            Users.VisibleToOrderedWithRole(session, Users.Find(session, Jz)!, UserRole.Member)), Cancellation);

        Assert.Contains(toDavid, user => user.Id == Kevin);
        Assert.DoesNotContain(toJz, user => user.Id == Kevin);
    }

    // `Rooms::Direct.find_or_create_for(users)`, as Rooms::DirectsController#create runs it.
    Task<long> FindOrCreateDirectAsync(params long[] users) =>
        app.Database.WriteAsync(tx =>
            (Rooms.FindDirectFor(tx.Session, users) ?? Rooms.CreateFor(tx.Session, RoomType.Direct, null, David, users, app.Now)).Id, Cancellation);

    async Task<List<long>> MembersAsync(long room) =>
        [.. (await app.Database.ReadAsync(session => Memberships.UserIdsInRoom(session, room), Cancellation)).Order()];
}
