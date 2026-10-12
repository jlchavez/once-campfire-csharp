using Campfire.Storage.Media;

namespace Campfire.Storage.Blobs;

/// <summary>
/// A new blob whose file is already in the service but whose row may not be committed yet.
/// Disposing it deletes the file unless the row was committed (<see cref="BlobStorage"/> marks it
/// kept after commit), so a rolled-back or abandoned upload leaves no orphan file behind.
/// </summary>
public sealed class StagedBlob : IDisposable
{
    readonly DiskService service;

    internal StagedBlob(DiskService service, NewBlob blob)
    {
        this.service = service;
        Blob = blob;
    }

    public NewBlob Blob { get; private set; }

    /// <summary>
    /// <c>blob.analyze_without_saving</c> from the staged file: the analyzer's metadata and
    /// <c>analyzed: true</c> merged into the blob's, before its row is saved. Once only. This is
    /// <c>Attached::Changes::CreateOne#analyze</c>, which <c>config.active_storage.analyze =
    /// :immediately</c> (load_defaults 8.2) runs before validation for a fresh upload; its
    /// attachment then enqueues no AnalyzeJob, since <c>analyze_blob_later</c> skips analyzed blobs.
    /// </summary>
    public void AnalyzeWithoutSaving()
    {
        if (Blob.Metadata["analyzed"] is not null)
        {
            return;
        }
        var unsaved = new Blob(0, Blob.Key, Blob.Filename, Blob.ContentType, Blob.Metadata, Blob.ServiceName, Blob.ByteSize, Blob.Checksum, default);
        Blob = Blob with { Metadata = BlobAnalyzer.Analyze(unsaved, service.PathFor(Blob.Key)) };
    }

    public bool Kept { get; private set; }

    /// <summary>The row is committed: keep the file.</summary>
    public void Keep() => Kept = true;

    public void Dispose()
    {
        if (!Kept)
        {
            service.Delete(Blob.Key);
        }
    }
}
