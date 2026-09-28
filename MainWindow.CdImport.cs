using System.IO;
using System.Windows;
namespace ZipMp3Player;
public partial class MainWindow
{
    private async void CdImport_Click(object sender,RoutedEventArgs e)
    {
        using var operation=_dataOperations.Begin();if(operation is null)return;
        var dialog=new CdImportWindow(_folders.FirstOrDefault()??Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),()=>StopPlayback(resetPosition:false)){Owner=this};
        dialog.ShowDialog();
        if(dialog.ImportedAlbum is not string album)return;
        string root=Path.GetDirectoryName(album)!;
        if(!_folders.Contains(root,StringComparer.OrdinalIgnoreCase))_folders.Add(root);
        _disabledFolders.Remove(root);SaveSettings();ConfigureLibraryWatchers();
        operation.Dispose(); // Only writes/import publication block shutdown, not library discovery.
        if (!_dataOperations.CloseRequested) await ScanFoldersAsync();
    }
}
