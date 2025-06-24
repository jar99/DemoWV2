using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Windows.Client.Utils;

public static class DependencyInjection
{
    public static ServiceProvider BuildServiceProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddTransient<MainForm>();
        services.AddTransient<Browser>();
        services.AddOptions<Browser.BrowserOptions>().Bind(configuration.GetSection("Browser"));

        services.AddLogging(loggingBuilder =>
            loggingBuilder.AddSerilog(dispose: true));
        return services.BuildServiceProvider();
    }

    public static ILogger<T> GetLogger<T>(this IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        return loggerFactory.CreateLogger<T>();
    }
}