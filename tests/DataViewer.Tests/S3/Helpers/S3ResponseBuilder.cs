using Amazon.S3.Model;
using System.Net;
using System.Text;

namespace DataViewer.Tests.S3.Helpers;

/// <summary>
/// Factory helpers for building <see cref="ListObjectsV2Response"/> and
/// <see cref="GetObjectResponse"/> test fixtures.
/// </summary>
internal static class S3ResponseBuilder
{
    // ── ListObjectsV2 ─────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a single-page <see cref="ListObjectsV2Response"/> with the given keys.
    /// </summary>
    /// <param name="keys">Object keys to include in the page.</param>
    /// <param name="isTruncated">
    ///   <see langword="true"/> when there is a subsequent page (sets
    ///   <see cref="ListObjectsV2Response.NextContinuationToken"/>).
    /// </param>
    /// <param name="nextToken">Continuation token for the next page.</param>
    public static ListObjectsV2Response ListPage(
        IEnumerable<string> keys,
        bool isTruncated = false,
        string? nextToken = null)
    {
        var response = new ListObjectsV2Response
        {
            HttpStatusCode = HttpStatusCode.OK,
            IsTruncated = isTruncated,
            NextContinuationToken = nextToken,
        };

        foreach (var key in keys)
        {
            response.S3Objects.Add(new S3Object
            {
                Key = key,
                Size = 1024,
                LastModified = DateTime.UtcNow,
                ETag = $"\"{Guid.NewGuid():N}\"",
            });
        }

        return response;
    }

    /// <summary>
    /// Creates an empty single-page response (bucket has no matching objects).
    /// </summary>
    public static ListObjectsV2Response EmptyList() =>
        ListPage(Array.Empty<string>());

    /// <summary>
    /// Creates a two-page sequence of responses for pagination tests.
    /// Returns (page1, page2) where page1 is truncated and references page2's token.
    /// </summary>
    public static (ListObjectsV2Response page1, ListObjectsV2Response page2) TwoPageList(
        IEnumerable<string> firstPageKeys,
        IEnumerable<string> secondPageKeys,
        string continuationToken = "token-page-2")
    {
        var page1 = ListPage(firstPageKeys, isTruncated: true, nextToken: continuationToken);
        var page2 = ListPage(secondPageKeys, isTruncated: false);
        return (page1, page2);
    }

    /// <summary>
    /// Builds a single S3 object entry for use inside a list response.
    /// </summary>
    public static S3Object AnObject(string key, long sizeBytes = 512) => new()
    {
        Key          = key,
        Size         = sizeBytes,
        LastModified = DateTime.UtcNow,
        ETag         = $"\"{Guid.NewGuid():N}\"",
    };

    // ── GetObject ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="GetObjectResponse"/> whose body stream contains
    /// <paramref name="content"/>.
    /// </summary>
    public static GetObjectResponse GetObjectWithContent(string content)
    {
        var bytes  = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new GetObjectResponse
        {
            HttpStatusCode = HttpStatusCode.OK,
            ResponseStream = stream,
            ContentLength  = bytes.Length,
        };
    }

    /// <summary>
    /// Creates a <see cref="GetObjectResponse"/> whose body stream contains
    /// <paramref name="bytes"/>.
    /// </summary>
    public static GetObjectResponse GetObjectWithBytes(byte[] bytes)
    {
        var stream = new MemoryStream(bytes);
        return new GetObjectResponse
        {
            HttpStatusCode = HttpStatusCode.OK,
            ResponseStream = stream,
            ContentLength  = bytes.Length,
        };
    }

    // ── AmazonS3Exception factories ───────────────────────────────────────────

    /// <summary>
    /// Creates an <see cref="Amazon.S3.AmazonS3Exception"/> with
    /// <c>ErrorCode = "NoSuchKey"</c>.
    /// </summary>
    public static Amazon.S3.AmazonS3Exception NoSuchKeyException(string key = "missing/key.gz") =>
        new(
            message:        $"The specified key does not exist. (Key={key})",
            errorType:      Amazon.Runtime.ErrorType.Sender,
            errorCode:      "NoSuchKey",
            requestId:      "FAKEREQID",
            statusCode:     HttpStatusCode.NotFound);

    /// <summary>
    /// Creates an <see cref="Amazon.S3.AmazonS3Exception"/> with
    /// <c>ErrorCode = "AccessDenied"</c>.
    /// </summary>
    public static Amazon.S3.AmazonS3Exception AccessDeniedException() =>
        new(
            message:        "Access Denied",
            errorType:      Amazon.Runtime.ErrorType.Sender,
            errorCode:      "AccessDenied",
            requestId:      "FAKEREQID",
            statusCode:     HttpStatusCode.Forbidden);

    /// <summary>
    /// Creates an <see cref="Amazon.S3.AmazonS3Exception"/> with
    /// <c>ErrorCode = "NoSuchBucket"</c>.
    /// </summary>
    public static Amazon.S3.AmazonS3Exception NoSuchBucketException() =>
        new(
            message:        "The specified bucket does not exist.",
            errorType:      Amazon.Runtime.ErrorType.Sender,
            errorCode:      "NoSuchBucket",
            requestId:      "FAKEREQID",
            statusCode:     HttpStatusCode.NotFound);

    /// <summary>
    /// Creates an <see cref="Amazon.S3.AmazonS3Exception"/> with
    /// <c>ErrorCode = "InvalidAccessKeyId"</c>.
    /// </summary>
    public static Amazon.S3.AmazonS3Exception InvalidAccessKeyIdException() =>
        new(
            message:        "The AWS Access Key Id you provided does not exist.",
            errorType:      Amazon.Runtime.ErrorType.Sender,
            errorCode:      "InvalidAccessKeyId",
            requestId:      "FAKEREQID",
            statusCode:     HttpStatusCode.Forbidden);

    /// <summary>
    /// Creates a generic <see cref="Amazon.Runtime.AmazonServiceException"/> for
    /// generic service-side failures.
    /// </summary>
    public static Amazon.Runtime.AmazonServiceException AmazonServiceException(
        string message = "Service unavailable") =>
        new(message);
}
