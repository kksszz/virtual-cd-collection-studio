using System.IO;
using System.Windows;

namespace ZipMp3Player;

public partial class MainWindow
{
    private bool _scanningAlbumArtwork;
    private async void ScanAlbumArtwork_Click(object sender,RoutedEventArgs e)
    {
        if(_scanningAlbumArtwork||_artworkRotationSaving||_dataOperations.CloseRequested)return;
        var item=GetSelectedAlbumItem();
        if(item is null){MessageBox.Show(this,"先にアルバムを選択してください。","画像をスキャン",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(!await CommitArtworkRotationAsync())return;
        _scanningAlbumArtwork=true;
        try{
            var album=item.Album;string? backup=null;
            var dialog=new AlbumScanWindow(item.Title,async(pages,progress)=>{
                using var operation=_dataOperations.Begin();if(operation is null)throw new IOException("終了処理中のため保存できません。");
                if(!Directory.Exists(album.Path)&&_playingAlbum?.Path==album.Path)StopPlayback(resetPosition:false);
                backup=await Task.Run(()=>ScannedArtworkStorage.Save(album.Path,pages,progress));
            },Path.Combine(DataDirectory,"scanner.json")){Owner=this};
            if(dialog.ShowDialog()!=true)return;
            var selectedTrack=(TrackGrid.SelectedItem as ZipTrack)?.FileName;
            ArtworkThumbnailCache.ClearAlbum(DataDirectory,album.Path);
            var refreshed=await Task.Run(()=>Directory.Exists(album.Path)?ZipAlbumReader.RefreshFolderArtwork(album):ZipAlbumReader.Open(album.Path));
            ReplaceLibraryAlbum(album,refreshed,selectedTrack);SaveLibraryCache();QueueCoverFlowRefresh();
            StatusText.Text=$"スキャン画像を{dialog.SavedCount}枚、アルバム内へ保存しました。"+(backup is null?"":" 元ZIPのバックアップ："+backup);
        }catch(Exception ex){MessageBox.Show(this,ex.Message,"画像をスキャン",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{_scanningAlbumArtwork=false;}
    }
}
