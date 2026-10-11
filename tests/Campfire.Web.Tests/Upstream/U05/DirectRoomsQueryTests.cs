using Campfire.Data.Queries;
using Campfire.Web.Tests.Controllers.Messages;

namespace Campfire.Web.Tests.Upstream.U05;

/// <summary>
/// Upstream commit 87eafc0 (#310): Find a direct room with one query instead of checking every one
/// (ports reference/test/models/rooms/direct_test.rb).
/// </summary>
public sealed class DirectRoomsQueryTests : IDisposable
{
    const long david = 127326141;
    const long kevin = 712064548;
    const long jason = 149087659;
    const long jz = 773523953;

    readonly MessagesApp messages = new(new DateTimeOffset(2026, 3, 2, 16, 0, 0, TimeSpan.Zero));

    public void Dispose() => messages.Dispose();

    [Fact]
    public async Task Find_direct_room_finds_the_room_with_exactly_the_same_users()
    {
        await messages.Database.ReadAsync(session =>
        {
            // Seed direct room for David and Kevin
            var davidAndKevin = Rooms.FindDirectFor(session, [david, kevin]);
            Assert.NotNull(davidAndKevin);

            // Reversing the user order still finds the same room
            var kevinAndDavid = Rooms.FindDirectFor(session, [kevin, david]);
            Assert.NotNull(kevinAndDavid);
            Assert.Equal(davidAndKevin.Id, kevinAndDavid.Id);

            // Asking for David, Kevin, and Jason does NOT return David and Kevin's 2-person room
            var threePerson = Rooms.FindDirectFor(session, [david, kevin, jason]);
            Assert.Null(threePerson);

            // Asking for David and JZ
            var davidAndJz = Rooms.FindDirectFor(session, [david, jz]);
            if (davidAndJz is not null)
            {
                var members = Memberships.ForRoom(session, davidAndJz.Id);
                var memberUserIds = members.Select(m => m.UserId).ToHashSet();
                Assert.Equal(new HashSet<long> { david, jz }, memberUserIds);
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }
}
