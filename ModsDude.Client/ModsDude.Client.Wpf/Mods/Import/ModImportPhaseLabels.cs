using ModsDude.Client.Core.Import;

namespace ModsDude.Client.Wpf.Mods.Import;

/// <summary>The words for an import phase, shared by the row chip and the progress strip.</summary>
public static class ModImportPhaseLabels
{
    public static string Describe(ModImportPhase phase) => phase switch
    {
        ModImportPhase.Queued => "Queued",
        ModImportPhase.Checking => "Checking",
        ModImportPhase.Downloading => "Downloading",
        ModImportPhase.Linking => "Preparing",
        ModImportPhase.Uploading => "Uploading",
        ModImportPhase.Registering => "Adding to repo",
        ModImportPhase.Storing => "Storing",
        ModImportPhase.PublishingImagery => "Publishing images",
        ModImportPhase.Completed => "Done",
        ModImportPhase.Failed => "Failed",
        ModImportPhase.Skipped => "Skipped",
        _ => string.Empty
    };
}
