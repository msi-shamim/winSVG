using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace SvgViewer;

/// <summary>
/// Main viewer window. Hosts a WebView2 control that renders the SVG with the
/// Chromium engine and provides pan/zoom UI, while this class owns the file
/// system side: which file is open, the sibling-file list used for previous /
/// next navigation, and the open-file dialog.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Virtual host that maps to the app's bundled HTML assets.</summary>
    private const string AppAssetsVirtualHost = "app.svgpreview";

    /// <summary>Virtual host that maps to the folder containing the open SVG.</summary>
    private const string SvgFolderVirtualHost = "files.svgpreview";

    /// <summary>All SVG files in the currently mapped folder, sorted by name.</summary>
    private List<string> _svgFilePathsInFolder = new();

    /// <summary>Index of the currently displayed file within <see cref="_svgFilePathsInFolder"/>.</summary>
    private int _currentFileIndex = -1;

    /// <summary>Folder currently mapped to the file virtual host, if any.</summary>
    private string? _currentlyMappedFolderPath;

    /// <summary>True once WebView2 finished loading viewer.html and can accept script calls.</summary>
    private bool _isViewerPageReady;

    /// <summary>File requested before the viewer page finished loading, shown once ready.</summary>
    private string? _pendingSvgFilePath;

    /// <summary>
    /// Initializes the window and starts asynchronous WebView2 setup.
    /// </summary>
    /// <param name="initialSvgFilePath">SVG file to open on startup, or null to start empty.</param>
    public MainWindow(string? initialSvgFilePath)
    {
        InitializeComponent();
        _pendingSvgFilePath = initialSvgFilePath;
        Loaded += async (_, _) => await InitializeWebViewAsync();
    }

    /// <summary>
    /// Creates the WebView2 environment (with an isolated user-data folder),
    /// wires up the JavaScript message bridge, and navigates to the bundled
    /// viewer page.
    /// </summary>
    private async Task InitializeWebViewAsync()
    {
        string webViewUserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SvgPreview", "WebView2Data");

        CoreWebView2Environment webViewEnvironment = await CoreWebView2Environment.CreateAsync(null, webViewUserDataFolder);
        await SvgWebView.EnsureCoreWebView2Async(webViewEnvironment);

        CoreWebView2 webViewCore = SvgWebView.CoreWebView2;
        webViewCore.Settings.AreDefaultContextMenusEnabled = false;
        webViewCore.Settings.IsZoomControlEnabled = false;

        string appAssetsFolder = Path.Combine(AppContext.BaseDirectory, "Assets");
        webViewCore.SetVirtualHostNameToFolderMapping(
            AppAssetsVirtualHost, appAssetsFolder, CoreWebView2HostResourceAccessKind.Allow);

        webViewCore.WebMessageReceived += OnWebMessageReceived;
        webViewCore.NavigationCompleted += OnViewerPageNavigationCompleted;

        webViewCore.Navigate($"https://{AppAssetsVirtualHost}/viewer.html");
    }

    /// <summary>
    /// Marks the viewer page as ready and shows any file that was requested
    /// before initialization finished; otherwise opens the file picker.
    /// </summary>
    /// <param name="sender">The WebView2 core raising the event.</param>
    /// <param name="navigationArgs">Navigation result details.</param>
    private async void OnViewerPageNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs navigationArgs)
    {
        if (!navigationArgs.IsSuccess)
        {
            return;
        }

        _isViewerPageReady = true;

        if (_pendingSvgFilePath is not null)
        {
            string requestedFilePath = _pendingSvgFilePath;
            _pendingSvgFilePath = null;
            await OpenSvgFileAsync(requestedFilePath);
        }
        else
        {
            await PromptForFileAsync();
        }
    }

    /// <summary>
    /// Handles commands posted from the viewer page's JavaScript
    /// (navigation between sibling files, opening the file dialog, closing).
    /// </summary>
    /// <param name="sender">The WebView2 core raising the event.</param>
    /// <param name="messageArgs">The posted JSON message.</param>
    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs messageArgs)
    {
        string commandName;
        try
        {
            using JsonDocument messageDocument = JsonDocument.Parse(messageArgs.WebMessageAsJson);
            commandName = messageDocument.RootElement.GetProperty("command").GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            return;
        }

        switch (commandName)
        {
            case "navigatePrevious":
                await NavigateSiblingFileAsync(-1);
                break;
            case "navigateNext":
                await NavigateSiblingFileAsync(+1);
                break;
            case "openFileDialog":
                await PromptForFileAsync();
                break;
            case "imageLoadFailed":
                await ShowCurrentFileAsDataUriAsync();
                break;
            case "closeWindow":
                Close();
                break;
        }
    }

    /// <summary>
    /// Shows the standard open-file dialog filtered to SVG files and opens the
    /// selected file in the viewer.
    /// </summary>
    private async Task PromptForFileAsync()
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Open SVG file",
            Filter = "SVG images (*.svg)|*.svg|All files (*.*)|*.*",
        };

        if (openFileDialog.ShowDialog(this) == true)
        {
            await OpenSvgFileAsync(openFileDialog.FileName);
        }
    }

    /// <summary>
    /// Opens the given SVG file: maps its folder into the WebView2 virtual
    /// host, refreshes the sibling-file list for arrow-key navigation, and
    /// tells the viewer page to display it.
    /// </summary>
    /// <param name="svgFilePath">Absolute path of the SVG file to display.</param>
    private async Task OpenSvgFileAsync(string svgFilePath)
    {
        string fullFilePath = Path.GetFullPath(svgFilePath);

        if (!File.Exists(fullFilePath))
        {
            MessageBox.Show(this, $"File not found:\n{fullFilePath}", "SVG Preview",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string containingFolderPath = Path.GetDirectoryName(fullFilePath)!;

        if (!string.Equals(containingFolderPath, _currentlyMappedFolderPath, StringComparison.OrdinalIgnoreCase))
        {
            SvgWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                SvgFolderVirtualHost, containingFolderPath, CoreWebView2HostResourceAccessKind.Allow);
            _currentlyMappedFolderPath = containingFolderPath;
        }

        _svgFilePathsInFolder = Directory
            .EnumerateFiles(containingFolderPath, "*.svg", SearchOption.TopDirectoryOnly)
            .OrderBy(filePath => filePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _currentFileIndex = _svgFilePathsInFolder.FindIndex(
            filePath => string.Equals(filePath, fullFilePath, StringComparison.OrdinalIgnoreCase));

        await ShowCurrentFileInViewerAsync();
    }

    /// <summary>
    /// Moves to the previous or next SVG file in the current folder, wrapping
    /// around at both ends of the list.
    /// </summary>
    /// <param name="indexStep">-1 for the previous file, +1 for the next file.</param>
    private async Task NavigateSiblingFileAsync(int indexStep)
    {
        if (_svgFilePathsInFolder.Count == 0 || _currentFileIndex < 0)
        {
            return;
        }

        int siblingCount = _svgFilePathsInFolder.Count;
        _currentFileIndex = ((_currentFileIndex + indexStep) % siblingCount + siblingCount) % siblingCount;
        await ShowCurrentFileInViewerAsync();
    }

    /// <summary>
    /// Pushes the currently selected file into the viewer page and updates the
    /// window title to match.
    /// </summary>
    private async Task ShowCurrentFileInViewerAsync()
    {
        if (!_isViewerPageReady || _currentFileIndex < 0 || _currentFileIndex >= _svgFilePathsInFolder.Count)
        {
            return;
        }

        string currentFilePath = _svgFilePathsInFolder[_currentFileIndex];
        string currentFileName = Path.GetFileName(currentFilePath);
        string svgFileUrl = $"https://{SvgFolderVirtualHost}/{Uri.EscapeDataString(currentFileName)}";

        Title = $"{currentFileName} — SVG Preview";

        string showFileScript =
            $"showFile({JsonSerializer.Serialize(svgFileUrl)}, {JsonSerializer.Serialize(currentFileName)}, " +
            $"{_currentFileIndex + 1}, {_svgFilePathsInFolder.Count});";

        await SvgWebView.CoreWebView2.ExecuteScriptAsync(showFileScript);
    }

    /// <summary>
    /// Fallback display path: reads the current SVG file directly and hands it
    /// to the viewer page as a base64 data: URI. Used when the virtual-host
    /// URL load fails (e.g. unusual file names or resource-loading quirks),
    /// since a data: URI cannot fail to fetch.
    /// </summary>
    private async Task ShowCurrentFileAsDataUriAsync()
    {
        if (!_isViewerPageReady || _currentFileIndex < 0 || _currentFileIndex >= _svgFilePathsInFolder.Count)
        {
            return;
        }

        string currentFilePath = _svgFilePathsInFolder[_currentFileIndex];
        string currentFileName = Path.GetFileName(currentFilePath);

        byte[] svgFileBytes;
        try
        {
            svgFileBytes = await File.ReadAllBytesAsync(currentFilePath);
        }
        catch (IOException)
        {
            return;
        }

        string svgDataUri = "data:image/svg+xml;base64," + Convert.ToBase64String(svgFileBytes);

        string fallbackScript =
            $"showFile({JsonSerializer.Serialize(svgDataUri)}, {JsonSerializer.Serialize(currentFileName)}, " +
            $"{_currentFileIndex + 1}, {_svgFilePathsInFolder.Count}, true);";

        await SvgWebView.CoreWebView2.ExecuteScriptAsync(fallbackScript);
    }
}
