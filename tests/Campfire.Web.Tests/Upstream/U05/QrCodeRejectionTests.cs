using System.Text;
using Campfire.Web.Tests.Controllers.Messages;

namespace Campfire.Web.Tests.Upstream.U05;

/// <summary>
/// Upstream commit 59e1c6d (#335): Reject invalid QR code requests before rendering
/// (ports reference/test/controllers/qr_code_controller_test.rb).
/// </summary>
public sealed class QrCodeRejectionTests : IDisposable
{
    readonly MessagesApp messages = new(new DateTimeOffset(2026, 3, 2, 16, 0, 0, TimeSpan.Zero));

    public void Dispose() => messages.Dispose();

    [Fact]
    public async Task Show_rejects_an_id_that_is_not_base64()
    {
        var response = await messages.SendAsync("GET", "/qr_code/not-base64!", new Dictionary<string, string>());

        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task Show_rejects_a_url_too_long_for_a_qr_code()
    {
        var longUrl = "http://example.com/" + new string('a', 3000);
        var id = Convert.ToBase64String(Encoding.UTF8.GetBytes(longUrl)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var response = await messages.SendAsync("GET", $"/qr_code/{id}", new Dictionary<string, string>());

        Assert.Equal(400, response.Status);
    }

    [Fact]
    public async Task Show_renders_valid_qr_code()
    {
        var validUrl = "http://example.com";
        var id = Convert.ToBase64String(Encoding.UTF8.GetBytes(validUrl)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var response = await messages.SendAsync("GET", $"/qr_code/{id}", new Dictionary<string, string>());

        Assert.Equal(200, response.Status);
        Assert.Contains("image/svg+xml", response.Headers["Content-Type"].ToString(), StringComparison.Ordinal);
        Assert.Equal("max-age=31556952, public", response.Headers["Cache-Control"].ToString());
    }
}
