namespace MillBurn.Pipeline;

/// <summary>
/// Where an open project came from, in the words the Project info panel uses.
///
/// **Asked for from the workshop:** *"We should display the gerber folder somewhere so the user
/// knows where the project is from."* `OriginFolder` was already there and already used — for the
/// folder a file dialog opens at, and for *"The source folder '…' is not there any more"* — and was
/// never shown while a project was open. The window title carries `DisplayName`, which is the
/// folder's last segment alone.
///
/// That is the whole problem in one line. This machine's recent list holds two projects called
/// `Millburn_Test_Board`, one in `WorkingFolder` and one in `WorkingFolder/V0.1.6`. The list can
/// tell them apart because it shows the path; once one is open, nothing can — and the operator is a
/// click from exporting programs built from a board they did not mean to open.
///
/// **The panel shows the path whole.** A first attempt elided its middle to keep the panel narrow,
/// and the product owner's answer to it was *"what I would like to know is where the source folder
/// is"* — which a path with its middle taken out does not say. It wraps instead, and the only thing
/// left to do to it is the one below.
/// </summary>
public static class ProjectOrigin
{
    /// <summary>
    /// A path as this machine writes it: absolute, and with the separators the platform uses.
    ///
    /// **Both halves were found by looking at the panel.** A board opened as `tests/boards/…` showed
    /// exactly that — forward slashes among a window of backslashes, and a path relative to a working
    /// directory the operator never sees, which answers *which folder* with "it depends where you
    /// were standing". The point of showing it at all is to tell two folders of the same name apart,
    /// and a relative path cannot.
    ///
    /// Returns what it was given if the path cannot be resolved: a name that is merely odd beats an
    /// exception thrown while a panel is drawing.
    /// </summary>
    public static string Full(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
