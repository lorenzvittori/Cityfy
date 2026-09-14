using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;

namespace ServiceDefault.Logging
{
    public static class LoggingExtensions
    {
        // Register logging providers and configure NLog via configuration
        public static void AddAppLogging(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                // Add NLog as logging provider; configuration (levels/filters) can still be driven by appsettings
                builder.AddNLog(configuration);
            });

            try
            {
                var cfgFile = configuration["NLogConfigFile"] ?? "nlog.config";
                // Use the newer Setup API instead of the deprecated LoadConfiguration
                LogManager.Setup().LoadConfigurationFromFile(cfgFile);
            }
            catch
            {
                // ignore
            }
        }
    }
}
