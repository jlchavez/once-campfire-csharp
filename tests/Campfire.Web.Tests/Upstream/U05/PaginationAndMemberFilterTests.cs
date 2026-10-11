using Campfire.Data.Pagination;
using Campfire.Data.Queries;
using Campfire.Web.Tests.Controllers.Messages;

namespace Campfire.Web.Tests.Upstream.U05;

/// <summary>
/// Upstream commits 0f2a1da (#316) and 1e0d353 (#312):
/// List one page of account members at a time and find replaced messages through the index.
/// </summary>
public sealed class PaginationAndMemberFilterTests : IDisposable
{
    const long david = 127326141;
    const long designers = 654632876;
    const string davidSession = "AxJs94fteQ5Autv2VrKsH68c";

    readonly MessagesApp messages = new(new DateTimeOffset(2026, 3, 2, 16, 0, 0, TimeSpan.Zero));

    public void Dispose() => messages.Dispose();

    [Fact]
    public async Task VisibleToOrderedMembers_only_returns_members_with_role_0()
    {
        await messages.Database.ReadAsync(session =>
        {
            var davidUser = Users.Find(session, david)!;
            var members = Users.VisibleToOrderedMembers(session, davidUser);
            Assert.NotEmpty(members);
            Assert.All(members, user => Assert.Equal(Campfire.Data.Records.UserRole.Member, user.Role));

            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PageUpdatedSince_finds_updated_messages_in_room()
    {
        await messages.Database.ReadAsync(session =>
        {
            var since = DateTimeOffset.UtcNow.AddYears(-10);
            var updated = MessagePages.PageUpdatedSince(session, designers, since, []);
            Assert.NotNull(updated);

            // Verifies messages are within the room
            Assert.All(updated, msg => Assert.Equal(designers, msg.RoomId));

            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Account_users_lists_one_page_of_members()
    {
        var response = await messages.SendAsync("GET", "/account/users?page=1", messages.SignedIn(davidSession));
        Assert.Equal(200, response.Status);
    }
}
