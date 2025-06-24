using Windows.Client.Utils;
using Microsoft.Extensions.Logging;

namespace Windows.Client;

public partial class MainForm : Form
{
    private readonly IServiceProvider _provider;
    private readonly ILogger<MainForm> _logger;

    public MainForm(ILogger<MainForm> logger, IServiceProvider provider)
    {
        _logger = logger;
        _provider = provider;
        
        InitializeComponent();

        _logger.LogInformation("MainForm initialized.");
    }

    private async Task InitializeWebView2Async()
    {
        try
        {
            await webView2Control.EnsureCoreWebView2Async();
            _logger.LogInformation("WebView2 initialized successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize WebView2.");
        }
    }

    private async void Form_Load(object sender, EventArgs e)
    {
        try
        {
            await InitializeWebView2Async();
            _logger.LogInformation("MainForm loaded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load MainForm.");
            MessageBox.Show($"Failed to load the application. Error: {ex.Message}", "Error", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Application.Exit();
        }
    }

    private void Form_Resize(object sender, EventArgs e)
    {
        _logger.LogTrace("Form resized to Width: {Width}, Height: {Height}", this.Width, this.Height);
    }
}