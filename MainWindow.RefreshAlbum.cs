using System.IO;
using System.Windows;

namespace ZipMp3Player;
public partial class MainWindow
{
    private bool _manualAlbumRefreshInProgress;
    private async void RefreshAlbum_Click(object sender,RoutedEventArgs e)
    {
        var original=GetSelectedAlbumItem();
        if(original is null||_manualAlbumRefreshInProgress||_dataOperations.CloseRequested)return;
        if(!await CommitArtworkRotationAsync())return;
        _scanCancellation?.Cancel();
        _incrementalRefreshCancellation?.Cancel();
        _manualAlbumRefreshInProgress=true;
        var path=original.Album.Path;
        StatusText.Text="アルバムを最新の状態に更新しています…";
        try
        {
            var refreshed=await Task.Run(()=>{
                var album=Directory.Exists(path)?ZipAlbumReader.OpenFolder(path):ZipAlbumReader.Open(path);
                if(album.Tracks.Count==0)throw new IOException("曲を読み込めませんでした。元の表示を保持します。");
                ArtworkThumbnailCache.ClearAlbum(DataDirectory,path);
                return new AlbumListItem(album);
            });
            if(_dataOperations.CloseRequested||!IsLoaded)return;
            var wasSelected=string.Equals(_album?.Path,path,StringComparison.OrdinalIgnoreCase);
            var selectedTrack=(TrackGrid.SelectedItem as ZipTrack)?.FileName;
            // Do not change the playback source or restart the current track.
            foreach(var old in _albums.Where(a=>string.Equals(a.Album.Path,path,StringComparison.OrdinalIgnoreCase)).ToArray()){
                RemoveAlbumFromArtistTree(old);_albums.Remove(old);_albumPaths.Remove(path);
            }
            InsertAlbumSorted(refreshed);
            if(wasSelected){
                AlbumList.SelectedItem=refreshed;
                var index=refreshed.Album.Tracks.ToList().FindIndex(t=>t.FileName==selectedTrack);
                TrackGrid.SelectedIndex=index<0?0:index;
            }
            QueueCoverFlowRefresh();
            SaveLibraryCache();
            StatusText.Text=$"最新の状態に更新しました: {refreshed.Title}";
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"アルバム更新",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{_manualAlbumRefreshInProgress=false;}
    }
}
