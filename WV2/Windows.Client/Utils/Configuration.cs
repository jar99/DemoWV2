using System.IO;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Windows.Client.Utils;

public static class Configuration
{
    public static IConfigurationRoot BuildConfiguration(string[] args)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddIniFile("client.ini", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);

        return builder.Build();
    }

    public static ILogger BuildLogger(IConfiguration configuration)
    {
        var logFilePath = configuration["LogFilePath"] ?? "logs/log-.txt";
        var logLevelString = configuration["LogLevel"] ?? "Debug";

        if (!Enum.TryParse(logLevelString, out Serilog.Events.LogEventLevel logLevel))
        {
            logLevel = Serilog.Events.LogEventLevel.Debug;
            Log.Warning($"Invalid LogLevel '{logLevelString}' in configuration. Using default 'Debug'.");
        }
        else
        {
            Log.Debug($"Log level set to: {logLevel}");
        }

        Log.Information($"Using log file path: {logFilePath}");
        var logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Console()
            .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        return logger;
    }
}