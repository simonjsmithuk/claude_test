using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;

namespace DataViewer.Tests.S3.Helpers;

/// <summary>
/// A partial fake/stub of <see cref="AmazonS3Client"/> that allows tests to
/// control the responses for <c>ListObjectsV2</c> and <c>GetObject</c> without
/// making real network calls.
/// </summary>
/// <remarks>
/// <para>
/// Because <see cref="AmazonS3Client"/> is a concrete class with no general-purpose
/// virtual dispatch on its service methods, this helper uses a delegate-based
/// interception approach: callers register response factories before the operation
/// is invoked.
/// </para>
/// <para>
/// This class is <strong>not</strong> intended to replace a proper AWS SDK mock;
/// it targets only the surface area exercised by <c>S3Service</c>.
/// </para>
/// </remarks>
internal sealed class FakeAmazonS3 : AmazonS3Client
{
    // ── Response queues / delegates ───────────────────────────────────────────

    private readonly Queue<Func<ListObjectsV2Request, Task<ListObjectsV2Response>>>
        _listResponders = new();

    private readonly Queue<Func<GetObjectRequest, Task<GetObjectResponse>>>
        _getResponders = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the fake client with dummy credentials so the base-class constructor
    /// does not throw due to missing environment credentials.
    /// </summary>
    public FakeAmazonS3()
        : base(
            new BasicAWSCredentials("FAKEACCESSKEY000001", "fakeSecretKey+000000000000000001"),
            Amazon.RegionEndpoint.EUWest2)
    { }

    // ── Setup helpers ─────────────────────────────────────────────────────────

    /// <summary>Enqueues a handler for the next <c>ListObjectsV2Async</c> call.</summary>
    public void EnqueueListResponse(
        Func<ListObjectsV2Request, Task<ListObjectsV2Response>> handler)
        => _listResponders.Enqueue(handler);

    /// <summary>Enqueues a static response for the next <c>ListObjectsV2Async</c> call.</summary>
    public void EnqueueListResponse(ListObjectsV2Response response)
        => _listResponders.Enqueue(_ => Task.FromResult(response));

    /// <summary>Enqueues a handler for the next <c>GetObjectAsync</c> call.</summary>
    public void EnqueueGetResponse(
        Func<GetObjectRequest, Task<GetObjectResponse>> handler)
        => _getResponders.Enqueue(handler);

    /// <summary>Enqueues a static response for the next <c>GetObjectAsync</c> call.</summary>
    public void EnqueueGetResponse(GetObjectResponse response)
        => _getResponders.Enqueue(_ => Task.FromResult(response));

    // ── Overrides ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override Task<ListObjectsV2Response> ListObjectsV2Async(
        ListObjectsV2Request request,
        CancellationToken cancellationToken = default)
    {
        if (_listResponders.Count == 0)
            throw new InvalidOperationException(
                "FakeAmazonS3: no ListObjectsV2 responder registered. " +
                "Call EnqueueListResponse() before the operation.");

        return _listResponders.Dequeue()(request);
    }

    /// <inheritdoc/>
    public override Task<GetObjectResponse> GetObjectAsync(
        GetObjectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_getResponders.Count == 0)
            throw new InvalidOperationException(
                "FakeAmazonS3: no GetObject responder registered. " +
                "Call EnqueueGetResponse() before the operation.");

        return _getResponders.Dequeue()(request);
    }
}
