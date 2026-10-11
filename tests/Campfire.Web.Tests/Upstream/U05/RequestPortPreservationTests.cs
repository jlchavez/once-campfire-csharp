using Campfire.Web.Routing;

namespace Campfire.Web.Tests.Upstream.U05;

/// <summary>
/// Upstream commit 765711f: Preserve request ports in message copy links
/// (ports reference/test/controllers/messages_controller_test.rb).
/// </summary>
public sealed class RequestPortPreservationTests
{
    const long roomId = 123;
    const long messageId = 456;

    [Theory]
    [InlineData("http", "once.campfire.test", 38683, "http://once.campfire.test:38683/rooms/123/@456")]
    [InlineData("https", "once.campfire.test", 8443, "https://once.campfire.test:8443/rooms/123/@456")]
    [InlineData("http", "once.campfire.test", 80, "http://once.campfire.test/rooms/123/@456")]
    [InlineData("https", "once.campfire.test", 443, "https://once.campfire.test/rooms/123/@456")]
    [InlineData("http", "once.campfire.test", null, "http://once.campfire.test/rooms/123/@456")]
    [InlineData("https", "once.campfire.test", null, "https://once.campfire.test/rooms/123/@456")]
    public void RoomAtMessageUrl_preserves_request_ports(string protocol, string host, int? port, string expectedUrl)
    {
        var origin = new UrlBase(protocol, host, port is 80 or 443 ? null : port);
        var url = Routes.RoomAtMessageUrl(origin, roomId, messageId);

        Assert.Equal(expectedUrl, url);
    }
}
