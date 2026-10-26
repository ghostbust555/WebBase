using Microsoft.Extensions.Options;
using WebBase.Api.Infrastructure;

namespace WebBase.Api.Content;

public static class ContentRegistration
{
    /// <summary>
    /// The CMS, reading from the store Cms:Source names: "files" (a folder,
    /// built in), or a file storage module from Modules.cs, configured by the
    /// section of its name (Cms:S3, Cms:Azure, Cms:Gcs). docs/CMS.md.
    /// </summary>
    public static IServiceCollection AddContent(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<CmsOptions>(config.GetSection(CmsOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFileStore>(sp =>
        {
            var cms = sp.GetRequiredService<IOptions<CmsOptions>>().Value;
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            var source = cms.Source.Trim().ToLowerInvariant();
            // While the CMS is off nothing is read; don't demand settings for it.
            if (!cms.Enabled || source is "" or "files")
                return new LocalFileStore(Path.Combine(env.ContentRootPath, cms.ContentPath));
            var module = Modules.FileStorageNamed(source, "Cms:Source");
            return module.Create(config.GetSection($"{CmsOptions.SectionName}:{module.Name}"));
        });
        services.AddSingleton<ContentService>();
        return services;
    }
}
