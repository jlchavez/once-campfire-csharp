using System.Text.Json.Nodes;
using Campfire.Storage.Media;

namespace Campfire.Web.Tests.Controllers;

/// <summary>
/// An upload is analyzed as it's attached (load_defaults 8.2's <c>analyze: :immediately</c>).
/// Without libvips, <c>ImageAnalyzer::Vips</c> reads no dimensions, as the reference's does without
/// ruby-vips, so a recorded image blob's width and height are left out of both sides.
/// </summary>
static class AnalyzedMetadata
{
    public static void WithoutLibVips(string table, JsonObject row)
    {
        if (LibVips.IsAvailable || table != "active_storage_blobs"
            || row["content_type"]?.GetValue<string>() is not { } type || !type.StartsWith("image/", StringComparison.Ordinal)
            || row["metadata"]?.GetValue<string>() is not { } text || JsonNode.Parse(text) is not JsonObject metadata)
        {
            return;
        }
        if (metadata.Remove("width") | metadata.Remove("height"))
        {
            row["metadata"] = metadata.ToJsonString();
        }
    }
}
