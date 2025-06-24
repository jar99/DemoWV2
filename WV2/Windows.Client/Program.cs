using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Serilog;
using Serilog.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Windows.Client;

static class Program
{
    private static readonly Mutex Mutex = new Mutex(true, "{UNIQUE_MUTEX_GUID}");

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File("log-.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize logger: {ex}");
            return;
        }

        if (Mutex.WaitOne(TimeSpan.Zero, true))
        {
            Log.Information("Application starting initialization...");

            var configuration = Utils.Configuration.BuildConfiguration(args);
            Log.Debug("Configuration built successfully.");

            Log.Logger = Utils.Configuration.BuildLogger(configuration);
            
            Log.Information("Application instance is the first to start.");
            try
            {
                Log.Information("Application starting.");
                ApplicationConfiguration.Initialize();
                Log.Debug("Application configuration initialized.");

                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Log.Debug("High DPI mode set to SystemAware.");
                Application.EnableVisualStyles();
                Log.Debug("Visual styles enabled.");
                Application.SetCompatibleTextRenderingDefault(false);
                Log.Debug("Compatible text rendering default set to false.");

                try
                {
                    using var serviceProvider = Utils.DependencyInjection.BuildServiceProvider(configuration);
                    Log.Debug("Service provider built successfully.");
                    var mainForm = serviceProvider.GetRequiredService<MainForm>();
                    Application.Run(mainForm);
                    Log.Debug("Application run complete.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error running the application.");
                    throw;
                }

                Log.Information("Application exiting.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly.");
            }
            finally
            {
                Mutex.ReleaseMutex();
                Log.Debug("Mutex released.");
                Log.CloseAndFlush();
                Log.Debug("Logger closed and flushed.");
            }
        }
        else
        {
            MessageBox.Show("Another instance of this application is already running.", "Error", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Log.Warning("Another instance is already running.");
        }
    }
}