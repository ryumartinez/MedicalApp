using System.Net.Http.Headers;
using GarageInitializer;

static string Required(string name) => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"{name} is required.");

var adminEndpoint = new Uri(Required("GARAGE_ADMIN_ENDPOINT"));
var s3Endpoint = new Uri(Required("GARAGE_S3_ENDPOINT"));
if (!adminEndpoint.IsLoopback || !s3Endpoint.IsLoopback)
    throw new InvalidOperationException("The local Garage initializer requires loopback endpoints.");

var settings = new GarageSettings(
    Required("GARAGE_BUCKET"),
    Required("GARAGE_API_ACCESS_KEY_ID"),
    Required("GARAGE_API_SECRET_ACCESS_KEY"),
    Required("GARAGE_CORS_ORIGINS").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

using var admin = new HttpClient { BaseAddress = adminEndpoint };
admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Required("GARAGE_ADMIN_TOKEN"));
await GarageReconciler.ReconcileAsync(admin, s3Endpoint, settings);

Console.WriteLine("The Garage layout, private bucket, scoped key, and CORS rules are ready.");
