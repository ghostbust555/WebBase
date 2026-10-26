using Microsoft.Extensions.Options;
using WebBase.Api.Infrastructure;

namespace WebBase.Api.Content;

public static class ContentRegistration
{
    /// <summary>
    /// The CMS, reading from the store Cms:Source names. Adding another kind of
    /// store (Google Cloud Storage, a database...) is a class implementing
    /// IContentStore plus a case below; tests/WebBase.Tests/ContentStores/ has
    /// the tests it should pass.
    /// </summary>
    public static IServiceCollection AddContent(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<CmsOptions>(config.GetSection(CmsOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IContentStore>(sp =>
        {
            var cms = sp.GetRequiredService<IOptions<CmsOptions>>().Value;
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            var source = cms.Source.Trim().ToLowerInvariant();
            // While the CMS is off nothing is read; don't demand settings for it.
            if (!cms.Enabled) source = "files";
            return source switch
            {
                "files" or "" => new FileContentStore(Path.Combine(env.ContentRootPath, cms.ContentPath)),
                "s3" => new S3ContentStore(cms.S3),
                "azure" => new AzureBlobContentStore(cms.Azure),
                _ => throw new InvalidOperationException($"Cms:Source is '{cms.Source}'. It can be files, s3 or azure (docs/CMS.md)."),
            };
        });
        services.AddSingleton<ContentService>();
        return services;
    }
}
