using System.Globalization;
using System.Text.Json.Nodes;
using Campfire.Storage.Blobs;

namespace Campfire.Storage.Media;

/// <summary>The analyzer <c>blob.analyzer_class</c> picks.</summary>
public enum AnalyzerKind
{
    Null,
    Image,
    Video,
    Audio,
}

/// <summary>
/// The Active Storage analyzers Campfire runs, in <c>config.active_storage.analyzers</c> order
/// (activestorage/lib/active_storage/engine.rb): <c>ImageAnalyzer::Vips</c>, <c>ImageAnalyzer::ImageMagick</c>
/// (never, since the variant processor is <c>:vips</c>), <c>VideoAnalyzer</c> and <c>AudioAnalyzer</c>,
/// then <c>NullAnalyzer</c>. Each reads a local copy of the blob (<see cref="BlobTempfile"/>).
/// </summary>
public static class BlobAnalyzer
{
    /// <summary><c>ActiveStorage.paths[:ffprobe] || "ffprobe"</c>.</summary>
    public const string FfprobePath = "ffprobe";

    /// <summary><c>analyzer_class</c>: the first analyzer that <c>accept?</c>s the blob.</summary>
    public static AnalyzerKind For(Blob blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        return blob.IsImage ? AnalyzerKind.Image : blob.IsVideo ? AnalyzerKind.Video : blob.IsAudio ? AnalyzerKind.Audio : AnalyzerKind.Null;
    }

    /// <summary>
    /// <c>analyzer_class.analyze_later?</c>: every analyzer but the null one runs in <c>AnalyzeJob</c>;
    /// the null one runs inline from <c>analyze_later</c>.
    /// </summary>
    public static bool AnalyzesLater(Blob blob) => For(blob) != AnalyzerKind.Null;

    /// <summary>
    /// <c>metadata.merge(extract_metadata_via_analyzer)</c>, the metadata <c>blob.analyze</c> saves:
    /// the blob's metadata with the analyzer's keys set (in place when present, as the indifferent
    /// hash merges) and <c>analyzed: true</c>. <paramref name="path"/> is the blob's local copy.
    /// </summary>
    public static JsonObject Analyze(Blob blob, string path, TimeSpan? ffprobeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(blob);
        var merged = (JsonObject)blob.Metadata.DeepClone();
        foreach (var (key, value) in Metadata(blob, path, ffprobeTimeout))
        {
            merged[key] = value?.DeepClone();
        }
        merged["analyzed"] = true;
        return merged;
    }

    /// <summary><c>analyzer.metadata</c>.</summary>
    public static JsonObject Metadata(Blob blob, string path, TimeSpan? ffprobeTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return For(blob) switch
        {
            AnalyzerKind.Image => ImageMetadata(path),
            AnalyzerKind.Video => VideoMetadata(Probe(path, ffprobeTimeout)),
            AnalyzerKind.Audio => AudioMetadata(Probe(path, ffprobeTimeout)),
            _ => [],
        };
    }

    /// <summary>
    /// <c>ImageAnalyzer::Vips#metadata</c>: <c>new_from_file(path, access: :sequential)</c>, its width
    /// and height swapped for EXIF orientations that turn it on its side. A file libvips can't read
    /// (or a Vips error) gives <c>{}</c>.
    /// </summary>
    public static JsonObject ImageMetadata(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (LibVips.IsAvailable)
        {
            LibVips.EnsureInitialized();
            return VipsImaging.ImageMetadata(path);
        }
        return FallbackImageMetadata(path);
    }

    static JsonObject FallbackImageMetadata(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var header = new byte[30];
            var read = stream.Read(header, 0, header.Length);
            if (read < 16) return [];

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A && read >= 24)
            {
                var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return new JsonObject { ["width"] = width, ["height"] = height };
            }

            // GIF: GIF87a or GIF89a
            if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && read >= 10)
            {
                var width = header[6] | (header[7] << 8);
                var height = header[8] | (header[9] << 8);
                return new JsonObject { ["width"] = width, ["height"] = height };
            }

            // JPEG: FF D8
            if (header[0] == 0xFF && header[1] == 0xD8)
            {
                stream.Position = 2;
                while (stream.Position < stream.Length)
                {
                    var markerPrefix = stream.ReadByte();
                    if (markerPrefix == -1) break;
                    if (markerPrefix != 0xFF) continue;
                    var marker = stream.ReadByte();
                    while (marker == 0xFF) marker = stream.ReadByte();
                    if (marker == -1) break;

                    if (marker is 0xD8 or 0xD9 or (>= 0xD0 and <= 0xD7)) continue;

                    var lenHi = stream.ReadByte();
                    var lenLo = stream.ReadByte();
                    if (lenHi == -1 || lenLo == -1) break;
                    var length = (lenHi << 8) | lenLo;

                    if (marker is 0xC0 or 0xC1 or 0xC2)
                    {
                        var precision = stream.ReadByte();
                        var hHi = stream.ReadByte();
                        var hLo = stream.ReadByte();
                        var wHi = stream.ReadByte();
                        var wLo = stream.ReadByte();
                        if (precision != -1 && hHi != -1 && hLo != -1 && wHi != -1 && wLo != -1)
                        {
                            var height = (hHi << 8) | hLo;
                            var width = (wHi << 8) | wLo;
                            return new JsonObject { ["width"] = width, ["height"] = height };
                        }
                        break;
                    }

                    stream.Position += length - 2;
                }
            }
        }
        catch
        {
        }
        return [];
    }

    /// <summary>
    /// <c>probe_from(file)</c>: <c>ffprobe -print_format json -show_streams -show_format -v error &lt;path&gt;</c>,
    /// parsed. <c>{}</c> when ffprobe isn't installed; its stderr goes to ours, as <c>IO.popen</c> leaves it.
    /// </summary>
    public static JsonObject Probe(string path, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var output = new MemoryStream();
        SubprocessResult result;
        try
        {
            result = Subprocess.Run([FfprobePath, "-print_format", "json", "-show_streams", "-show_format", "-v", "error", path], output, timeout);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }
        if (result.TimedOut)
        {
            throw new MediaException($"{FfprobePath} timed out after {timeout}");
        }
        if (result.Stderr.Length > 0)
        {
            Console.Error.Write(result.Stderr);
        }
        try
        {
            return JsonNode.Parse(output.ToArray()) as JsonObject ?? throw new MediaException($"{FfprobePath} output is not a JSON object");
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new MediaException($"{FfprobePath} output: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// <c>VideoAnalyzer#metadata</c>: <c>{ width:, height:, duration:, angle:, display_aspect_ratio:,
    /// audio:, video: }.compact</c>. Dimensions and duration are Floats, so they're written <c>320.0</c>.
    /// </summary>
    public static JsonObject VideoMetadata(JsonObject probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var video = Stream(probe, "video");
        var audio = Stream(probe, "audio");

        var angle = Angle(video);
        var aspect = DisplayAspectRatio(video);
        var encodedWidth = Field(video, "width") is { } w ? RubyFloat(w) : (double?)null;
        var encodedHeight = Field(video, "height") is { } h ? RubyFloat(h) : (double?)null;
        double? computedHeight = encodedWidth is { } ew && aspect is { } a ? ew * ((double)a.Denominator / a.Numerator) : null;
        var rotated = angle is 90 or 270 or -90 or -270;
        var (width, height) = rotated ? (computedHeight ?? encodedHeight, encodedWidth) : (encodedWidth, computedHeight ?? encodedHeight);
        var duration = (Field(video, "duration") ?? Field(probe["format"] as JsonObject, "duration")) is { } d ? RubyFloat(d) : (double?)null;

        var metadata = new JsonObject();
        AddIfPresent(metadata, "width", width);
        AddIfPresent(metadata, "height", height);
        AddIfPresent(metadata, "duration", duration);
        AddIfPresent(metadata, "angle", angle);
        if (aspect is { } ratio)
        {
            metadata["display_aspect_ratio"] = new JsonArray(ratio.Numerator, ratio.Denominator);
        }
        metadata["audio"] = audio is { Count: > 0 };
        metadata["video"] = video is { Count: > 0 };
        return metadata;
    }

    /// <summary><c>AudioAnalyzer#metadata</c>: <c>{ duration:, bit_rate:, sample_rate:, tags: }.compact</c>.</summary>
    public static JsonObject AudioMetadata(JsonObject probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var audio = Stream(probe, "audio");
        var metadata = new JsonObject();
        AddIfPresent(metadata, "duration", Field(audio, "duration") is { } d ? RubyFloat(d) : null);
        AddIfPresent(metadata, "bit_rate", Field(audio, "bit_rate") is { } b ? RubyInteger(b) : null);
        AddIfPresent(metadata, "sample_rate", Field(audio, "sample_rate") is { } s ? RubyInteger(s) : null);
        if (Field(audio, "tags") is { } tags)
        {
            // Hash(tags): a hash as it is, [] and nil as {}.
            metadata["tags"] = tags switch
            {
                JsonObject hash => hash.DeepClone(),
                JsonArray { Count: 0 } => new JsonObject(),
                _ => throw new MediaException($"can't convert {tags.ToJsonString()} into Hash"),
            };
        }
        return metadata;
    }

    /// <summary><c>tags["rotate"]</c>, else the Display Matrix side data's <c>rotation</c>, as an Integer.</summary>
    static long? Angle(JsonObject? video)
    {
        if (Field(video, "tags") is JsonObject tags && Field(tags, "rotate") is { } rotate)
        {
            return RubyInteger(rotate);
        }
        var displayMatrix = (Field(video, "side_data_list") as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(data => (data["side_data_type"] as JsonValue)?.GetValueKind() == System.Text.Json.JsonValueKind.String
                && data["side_data_type"]!.GetValue<string>() == "Display Matrix");
        return Field(displayMatrix, "rotation") is { } rotation ? RubyInteger(rotation) : null;
    }

    /// <summary><c>"16:9"</c> → <c>[16, 9]</c>, or nil when the numerator is 0.</summary>
    static (long Numerator, long Denominator)? DisplayAspectRatio(JsonObject? video)
    {
        if (Field(video, "display_aspect_ratio") is not JsonValue descriptor || descriptor.GetValueKind() != System.Text.Json.JsonValueKind.String)
        {
            return Field(video, "display_aspect_ratio") is null ? null : throw new MediaException("display_aspect_ratio is not a string");
        }
        var terms = descriptor.GetValue<string>().Split(':', 2);
        var numerator = RubyInteger(JsonValue.Create(terms[0]));
        var denominator = terms.Length > 1 ? RubyInteger(JsonValue.Create(terms[1])) : throw new MediaException("can't convert nil into Integer");
        return numerator == 0 ? null : (numerator, denominator);
    }

    /// <summary><c>streams.detect { |stream| stream["codec_type"] == type }</c>.</summary>
    static JsonObject? Stream(JsonObject probe, string codecType) =>
        (probe["streams"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(s => s["codec_type"] is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String && v.GetValue<string>() == codecType);

    /// <summary><c>hash[name]</c>, treating JSON null as absent (Ruby's <c>nil</c>).</summary>
    static JsonNode? Field(JsonObject? hash, string name) => hash?[name];

    static void AddIfPresent(JsonObject metadata, string key, double? value)
    {
        if (value is { } v)
        {
            metadata[key] = v;
        }
    }

    static void AddIfPresent(JsonObject metadata, string key, long? value)
    {
        if (value is { } v)
        {
            metadata[key] = v;
        }
    }

    /// <summary>Ruby's <c>Float(value)</c> for the numbers and numeric strings ffprobe writes.</summary>
    static double RubyFloat(JsonNode value) => value switch
    {
        JsonValue v when v.GetValueKind() == System.Text.Json.JsonValueKind.Number => v.GetValue<double>(),
        JsonValue v when v.GetValueKind() == System.Text.Json.JsonValueKind.String
            && double.TryParse(v.GetValue<string>().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => throw new MediaException($"invalid value for Float(): {value.ToJsonString()}"),
    };

    /// <summary>Ruby's <c>Integer(value)</c>: integers as they are, Floats truncated, strings parsed.</summary>
    static long RubyInteger(JsonNode value)
    {
        if (value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number)
        {
            return v.TryGetValue<long>(out var n) ? n : (long)Math.Truncate(v.GetValue<double>());
        }
        if (value is JsonValue s && s.GetValueKind() == System.Text.Json.JsonValueKind.String
            && long.TryParse(s.GetValue<string>().Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        throw new MediaException($"invalid value for Integer(): {value.ToJsonString()}");
    }
}
