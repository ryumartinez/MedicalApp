using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicalApp.Common.Persistence;

public static class JsonColumnExtensions
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Stores an immutable value as one jsonb document. Used for question definitions and answers,
    /// which are always read and replaced as a whole.
    /// </summary>
    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> builder) where T : class
    {
        builder.HasConversion(
            value => JsonSerializer.Serialize(value, Options),
            json => JsonSerializer.Deserialize<T>(json, Options)!,
            new ValueComparer<T>(
                (left, right) => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options),
                value => JsonSerializer.Serialize(value, Options).GetHashCode(),
                value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!));
        builder.HasColumnType("jsonb");
        return builder;
    }

    /// <summary>
    /// Maps a uint property to PostgreSQL's xmin system column as an optimistic concurrency token.
    /// </summary>
    public static PropertyBuilder<uint> IsXminRevision(this PropertyBuilder<uint> builder) =>
        builder.HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
}
