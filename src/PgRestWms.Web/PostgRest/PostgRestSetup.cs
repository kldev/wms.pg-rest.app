using Microsoft.Extensions.Options;

namespace PgRestWms.Web.PostgRest;

public static class PostgRestSetup
{
    public static WebApplicationBuilder AddPostgRest(this WebApplicationBuilder builder)
    {
        builder
            .Services.AddOptions<PostgRestOptions>()
            .BindConfiguration(PostgRestOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        builder.Services.AddHttpClient<PostgRestClient>(
            (services, http) =>
            {
                var options = services.GetRequiredService<IOptions<PostgRestOptions>>().Value;
                http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                http.Timeout = options.Timeout;
            }
        );
        return builder;
    }
}
