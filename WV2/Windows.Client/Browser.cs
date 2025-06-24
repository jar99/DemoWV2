using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Windows.Client;

public partial class Browser : WebView2, IDisposable
{
    public class BrowserOptions
    {
        public Uri InitialURL { get; set; }
        public string UserAgent { get; set; }
        public CoreWebView2PreferredColorScheme ColorScheme { get; set; }
    }

    private readonly ILogger _logger = null!;

    private readonly BrowserOptions _options = null!;

    internal Browser()
    {
        InitializeComponent();
    }

    public Browser(ILogger<Browser> logger, IOptionsMonitor<BrowserOptions> browser)
    {
        _logger = logger;
        _options = browser.CurrentValue;
        browser.OnChange(OnBrowserOptionsChanged);

        _logger.LogInformation("Browser component initializing...");
        InitializeComponent();
        _logger.LogInformation("Browser component initialized.");
        CoreWebView2InitializationCompleted += WebView2Control_CoreWebView2InitializationCompleted;
        _logger.LogInformation("CoreWebView2InitializationCompleted event handler attached.");
    }

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _logger.LogInformation("Navigation to {Uri} completed successfully.", CoreWebView2.Source);
        }
        else
        {
            _logger.LogError("Navigation to {Uri} failed with error code {ErrorCode}.", CoreWebView2.Source,
                e.HttpStatusCode);
        }
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _logger.LogInformation("Navigation starting to {Uri}.", e.Uri);
    }

    private void Core_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        _logger.LogDebug("Web Resource Requested: {Uri}, Method: {Method}", e.Request.Uri, e.Request.Method);
        if (IsImage(e.Request.Uri))
        {
            ReplaceImageWithLocal(e);
        }
    }

    private void ReplaceImageWithLocal(CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var filePath = Path.Combine(Application.StartupPath, "assets", "images", "kittyheart-DC845.png");
            if (TryCreateWebResourceResponseFromFile(filePath, out var response))
            {
                e.Response = response;
                _logger.LogInformation("Replaced image {Uri} with local image.", e.Request.Uri);
            }
            else
            {
                _logger.LogWarning("Could not create web resource response for {Uri}.", e.Request.Uri);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replacing image {Uri} with local image.", e.Request.Uri);
        }
    }

    private bool TryCreateWebResourceResponseFromFile(string filePath, out CoreWebView2WebResourceResponse? response)
    {
        response = null;
        if (File.Exists(filePath))
        {
            try
            {
                var stream = File.OpenRead(filePath);
                response = CoreWebView2.Environment.CreateWebResourceResponse(stream, 200, "OK",
                    "Content-Type: image/png");
                _logger.LogInformation("Created web resource response from file {FilePath}.", filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating web resource response from file {FilePath}.", filePath);
                return false;
            }
        }

        _logger.LogWarning("File not found: {FilePath}", filePath);
        return false;
    }

    private static bool IsImage(string uriString)
    {
        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri))
            return false; // If not a valid URI, consider it not an image

        var path = uri.LocalPath.ToLower(); // Get the path portion and lowercase it
        return path.EndsWith(".png") || path.EndsWith(".jpg") ||
               path.EndsWith(".jpeg") || path.EndsWith(".gif") ||
               path.EndsWith(".bmp");
    }

    void Core_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        _logger.LogDebug("Web Resource Response Received: {Uri}, Status: {StatusCode}",
            e.Request.Uri, e.Response.StatusCode);

        try
        {
            // Check if the response is an image based on content type
            if (!e.Response.Headers.GetHeader("content-type").StartsWith("image/")) return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WebResourceResponseReceived event for {Uri}.", e.Request.Uri);
        }
    }

    // private async void WebView2_FetchRequestPaused(object sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    // {
    //     try
    //     {
    //         var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
    //         var id = doc.RootElement.GetProperty("requestId");
    //         string type = doc.RootElement.GetProperty("resourceType").ToString();
    //         string method = doc.RootElement.GetProperty("request").GetProperty("method").ToString();
    //         string payload = "{\"requestId\":\""+id+"\"}";
    //         string? code = doc.RootElement.GetProperty("responseStatusCode").ToString();
    //
    //         if (type == "Document" && method == "GET" && !string.IsNullOrWhiteSpace(code) && code=="200")
    //         {
    //
    //             try
    //             {
    //                 string bodyResponse = await CoreWebView2.CallDevToolsProtocolMethodAsync("Fetch.getResponseBody", payload);
    //
    //                 if (!string.IsNullOrWhiteSpace(bodyResponse))
    //                 {
    //                     var docBody = JsonDocument.Parse(bodyResponse);
    //                     if (docBody.RootElement.GetProperty("base64Encoded").ToString() == "True")
    //                     {
    //                         bodyResponse = Encoding.UTF8.GetString(Convert.FromBase64String(docBody.RootElement.GetProperty("body").ToString()));
    //                     }
    //                     else
    //                     {
    //                         bodyResponse = docBody.RootElement.GetProperty("body").ToString();
    //                     }
    //
    //                     System.Diagnostics.Trace.WriteLine(bodyResponse);
    //                     //... Fetch.fulfillRequest CODE....
    //                 }
    //                 else
    //                 {
    //                     await CoreWebView2.CallDevToolsProtocolMethodAsync("Fetch.continueRequest", payload);
    //                 }
    //             } 
    //             catch 
    //             {
    //                 await CoreWebView2.CallDevToolsProtocolMethodAsync("Fetch.continueRequest", payload);
    //             }
    //             
    //
    //         }
    //         else
    //         {
    //
    //             await CoreWebView2.CallDevToolsProtocolMethodAsync("Fetch.continueRequest", payload);
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         await CoreWebView2.CallDevToolsProtocolMethodAsync("Fetch.continueRequest" , payload);
    //         System.Console.WriteLine(ex.Message);
    //     }
    // }
    

    private void OnBrowserOptionsChanged(BrowserOptions options, string? key)
    {
        if (_options.InitialURL != options.InitialURL)
        {
            _logger.LogInformation("Initial URL changed from {OldUrl} to {NewUrl}", _options.InitialURL,
                options.InitialURL);
            _options.InitialURL = options.InitialURL;
            LogInvoke(() => { Navigate(_options.InitialURL); });
        }

        if (_options.ColorScheme != options.ColorScheme)
        {
            _logger.LogInformation("Color scheme changed from {OldScheme} to {NewScheme}", _options.ColorScheme,
                options.ColorScheme);
            _options.ColorScheme = options.ColorScheme;
            LogInvoke(() => { CoreWebView2.Profile.PreferredColorScheme = _options.ColorScheme; });
        }

        if (_options.UserAgent != options.UserAgent)
        {
            _logger.LogInformation("User agent changed from {OldAgent} to {NewAgent}", _options.UserAgent,
                options.UserAgent);
            _options.UserAgent = options.UserAgent;
            LogInvoke(() => { CoreWebView2.Settings.UserAgent = _options.UserAgent; });
        }
    }

    private void Navigate(Uri url)
    {
        Source = url;
        _logger.LogInformation("Navigating to new URL: {Url}", url);
    }

    private async void WebView2Control_CoreWebView2InitializationCompleted(object? sender,
        CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            _logger?.LogError(e.InitializationException, "WebView2 creation failed.");
            MessageBox.Show($"WebView2 creation failed with exception = {e.InitializationException}");
            return;
        }

        CoreWebView2.WebResourceRequested += Core_WebResourceRequested;
        CoreWebView2.WebResourceResponseReceived += Core_WebResourceResponseReceived;
        CoreWebView2.NavigationStarting += Core_NavigationStarting;
        CoreWebView2.NavigationCompleted += Core_NavigationCompleted;

        // CoreWebView2.GetDevToolsProtocolEventReceiver("Fetch.requestPaused").DevToolsProtocolEventReceived +=
        //     WebView2_FetchRequestPaused;
        // await CoreWebView2.CallDevToolsProtocolMethodAsync("Fetch.enable", "{}");

        // Setup host resource mapping for local files

        var assetPath = Path.Combine(Application.StartupPath, "assets");
        CoreWebView2.SetVirtualHostNameToFolderMapping("appassets.hostvoid.net", assetPath,
            CoreWebView2HostResourceAccessKind.DenyCors);
        _logger?.LogInformation("Virtual host mapping set to appassets.hostvoid.net -> assets");


        Source = _options.InitialURL;
        CoreWebView2.Profile.PreferredColorScheme = _options.ColorScheme;
        CoreWebView2.Settings.UserAgent = _options.UserAgent;
        _logger?.LogInformation("Navigating to start page: {StartPageUri}", Source);

        CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image,
            CoreWebView2WebResourceRequestSourceKinds.Document);
        _logger?.LogInformation("Web resource requested filter added for images.");
    }

    private void LogInvoke(Action function)
    {
        // Invoke the navigation on the UI thread
        if (IsHandleCreated)
        {
            Invoke(function);
        }
        else
        {
            _logger.LogWarning("Handle not created, unable to navigate to new Initial URL.");
        }
    }
}