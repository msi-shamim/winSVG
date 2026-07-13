using System.Windows;

namespace SvgViewer;

/// <summary>
/// Application entry point. Reads the SVG file path passed on the command
/// line (Windows supplies it when the user double-clicks an associated file)
/// and opens the main viewer window with it.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Creates the main window, forwarding the first command-line argument as
    /// the initially opened SVG file when one was provided.
    /// </summary>
    /// <param name="sender">The application instance raising the event.</param>
    /// <param name="startupEventArgs">Startup data containing the command-line arguments.</param>
    private void OnApplicationStartup(object sender, StartupEventArgs startupEventArgs)
    {
        string? initialSvgFilePath = startupEventArgs.Args.Length > 0 ? startupEventArgs.Args[0] : null;

        var mainViewerWindow = new MainWindow(initialSvgFilePath);
        mainViewerWindow.Show();
    }
}
