using System.Net;
using System.Security.Cryptography;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MedicalApp.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace MedicalApp.Common.Storage.Garage;

/// <summary>
/// <see cref="IObjectStorage"/> over Garage's S3 API with path-style addressing.
/// Uses only operations validated against the pinned Garage version.
/// </summary>
public sealed class GarageObjectStorage : IObjectStorage, IDisposable
{
    private readonly Lazy<(AmazonS3Client Internal, AmazonS3Client Public, string Bucket, Protocol PublicProtocol)> _clients;

    public GarageObjectStorage(IOptions<GarageStorageOptions> options)
    {
        _clients = new Lazy<(AmazonS3Client, AmazonS3Client, string, Protocol)>(() => CreateClients(options.Value));
    }

    private AmazonS3Client Internal => _clients.Value.Internal;
    private string Bucket => _clients.Value.Bucket;

    public Task<Uri> CreateUploadUrlAsync(string key, string contentType, TimeSpan lifetime, CancellationToken cancellationToken) =>
        SignAsync(new GetPreSignedUrlRequest
        {
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType
        }, lifetime);

    public Task<Uri> CreateDownloadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken) =>
        SignAsync(new GetPreSignedUrlRequest { Key = key, Verb = HttpVerb.GET }, lifetime);

    public async Task<StoredObjectInfo?> GetInfoAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await Internal.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = Bucket, Key = key }, cancellationToken);
            return new StoredObjectInfo(key, metadata.ContentLength, metadata.Headers.ContentType, metadata.ETag);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<long?> DownloadAsync(string key, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        GetObjectResponse response;
        try
        {
            response = await Internal.GetObjectAsync(new GetObjectRequest { BucketName = Bucket, Key = key }, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        using (response)
        {
            // Read the stream itself rather than trusting the reported length.
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await response.ResponseStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;
                if (total > maxBytes)
                    throw new ObjectTooLargeException(key, maxBytes);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return total;
        }
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken)
    {
        if (!content.CanSeek)
            throw new ArgumentException("The content stream must be seekable.", nameof(content));

        content.Position = 0;
        var md5 = Convert.ToBase64String(await MD5.HashDataAsync(content, cancellationToken));
        content.Position = 0;
        await Internal.PutObjectAsync(new PutObjectRequest
        {
            BucketName = Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            MD5Digest = md5,
            AutoCloseStream = false
        }, cancellationToken);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await Internal.DeleteObjectAsync(new DeleteObjectRequest { BucketName = Bucket, Key = key }, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
        }
    }

    private async Task<Uri> SignAsync(GetPreSignedUrlRequest request, TimeSpan lifetime)
    {
        var (_, publicClient, bucket, protocol) = _clients.Value;
        request.BucketName = bucket;
        request.Expires = DateTime.UtcNow.Add(lifetime);
        request.Protocol = protocol;
        return new Uri(await publicClient.GetPreSignedURLAsync(request));
    }

    private static (AmazonS3Client, AmazonS3Client, string, Protocol) CreateClients(GarageStorageOptions options)
    {
        static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"{GarageStorageOptions.SectionName}:{name} is required.")
            : value;

        var serviceUrl = Required(options.ServiceUrl, nameof(options.ServiceUrl));
        var publicUrl = string.IsNullOrWhiteSpace(options.PublicServiceUrl) ? serviceUrl : options.PublicServiceUrl;
        var credentials = new BasicAWSCredentials(
            Required(options.AccessKeyId, nameof(options.AccessKeyId)),
            Required(options.SecretAccessKey, nameof(options.SecretAccessKey)));

        AmazonS3Client Create(string url) => new(credentials, new AmazonS3Config
        {
            ServiceURL = url,
            ForcePathStyle = true,
            AuthenticationRegion = options.Region,
            // Garage does not return flexible checksums. Only send them when an operation requires one.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        });

        var protocol = new Uri(publicUrl).Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS;
        return (Create(serviceUrl), Create(publicUrl), Required(options.Bucket, nameof(options.Bucket)), protocol);
    }

    public void Dispose()
    {
        if (!_clients.IsValueCreated)
            return;
        _clients.Value.Internal.Dispose();
        _clients.Value.Public.Dispose();
    }
}
