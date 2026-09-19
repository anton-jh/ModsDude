using ModsDude.Client.Core.Savegames;
using ModsDude.Client.Wpf.ViewModel.ViewModels;

namespace ModsDude.Client.Wpf.ViewModel.Services;

/// <summary>
/// Puts a savegame operation's stages and bytes on the strip entry that is already running it.
/// </summary>
/// <remarks>
/// <para>
/// <b>On the task's own bar rather than in subtasks.</b> A check-in is one thing happening at a time -
/// pack, then upload, then record - so the entry's bar is exactly what is moving, and a row under it
/// would say the same number twice. The bar restarts with each stage, which is honest: packing 80 MB
/// and uploading 30 MB of the result are two different distances.
/// </para>
/// <para>
/// Runs on whichever thread counted the bytes. The strip coalesces on its own timer, so nothing here
/// throttles.
/// </para>
/// </remarks>
public sealed class SavegameStripProgress(IBackgroundTask task) : IProgress<SavegameProgress>
{
    public void Report(SavegameProgress value)
    {
        task.Report(
            Describe(value.Stage),
            value.Completed,
            value.Total,
            value.Total > 0 || value.Completed > 0 ? ByteSize.Describe(value.Completed, value.Total) : null);
    }

    private static string Describe(SavegameStage stage) => stage switch
    {
        SavegameStage.Packing => "Packing the slot",
        SavegameStage.Uploading => "Uploading",
        SavegameStage.Recording => "Recording the snapshot",
        SavegameStage.Downloading => "Downloading",
        SavegameStage.Verifying => "Checking what arrived",
        SavegameStage.Unpacking => "Writing it into the slot",
        _ => stage.ToString()
    };
}
