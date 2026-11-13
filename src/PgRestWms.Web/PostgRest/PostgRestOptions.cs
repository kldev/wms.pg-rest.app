using System.ComponentModel.DataAnnotations;
using JetBrains.Annotations;

namespace PgRestWms.Web.PostgRest;

/// <summary>Where PostgREST is. Environment variable: <c>PostgRest__BaseUrl</c>.</summary>
// Set by the configuration binder (BindConfiguration).
[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.Members)]
public sealed class PostgRestOptions
{
    public const string SectionName = "PostgRest";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = "";

    /// <summary>Per request to PostgREST. Environment variable: <c>PostgRest__Timeout</c>.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:05:00")]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
