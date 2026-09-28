using System.IO;
using System.Windows;
namespace ZipMp3Player;
public partial class MainWindow
{
    private async Task UndoArtworkEditAsync(AlbumImageSource source,Window owner)
    {
        if(_album is null||_artworkRotationSaving)return;
        if(!await CommitArtworkRotationAsync())return;
        using var operation=_dataOperations.Begin();if(operation is null)return;
        var album=_album;var path=source.FilePath??album.Path;
        var backupFolder=_settings.TagBackupEnabled?_settings.TagBackupFolder:null;
        _artworkRotationSaving=true;
        try{
            var backup=await Task.Run(()=>ArtworkRotationWriter.FindUndoBackup(path,source.ZipEntry?.FileName,backupFolder));
            if(backup is null){MessageBox.Show(owner,"この画像を戻せるバックアップが見つかりません。保存前の画像は、バックアップの保持期間内だけ復元できます。","元に戻す",MessageBoxButton.OK,MessageBoxImage.Information);return;}
            if(MessageBox.Show(owner,$"「{source.DisplayName}」を直前の異なる画像バックアップへ戻します。\n復元前の画像もバックアップします。ZIP内の他のファイルは変更しません。\n\nバックアップ日時: {File.GetLastWriteTime(backup):yyyy/MM/dd HH:mm:ss}\n戻しますか？","画像を元に戻す",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
            var selected=(TrackGrid.SelectedItem as ZipTrack)?.FileName;
            if(source.ZipEntry is not null&&_playingAlbum?.Path==album.Path)StopPlayback(resetPosition:false);
            await Task.Run(()=>ArtworkRotationWriter.Restore(path,source.ZipEntry?.FileName,backup,backupFolder));
            _pendingArtworkRotation=null;_currentArtworkRotations.Remove(source.RoleKey);SaveArtworkRotations(album.Path,_currentArtworkRotations);
            ArtworkThumbnailCache.ClearAlbum(DataDirectory,album.Path);
            if(source.ZipEntry is not null)ReplaceLibraryAlbum(album,ZipAlbumReader.Open(album.Path),selected);
            else{_albums.FirstOrDefault(item=>item.Album.Path==album.Path)?.RefreshImageCount();LoadAlbumImages(album);}
            var index=_albumImages.FindIndex(image=>image.RoleKey==source.RoleKey);if(index>=0){_albumImageIndex=index;_selectedAlbumImageIndex=index;}
            ShowCurrentAlbumImage();QueueCoverFlowRefresh();SaveLibraryCache();StatusText.Text="画像を元に戻しました: "+source.DisplayName;
        }catch(Exception ex){MessageBox.Show(owner,ex.Message,"画像を元に戻せませんでした",MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally{_artworkRotationSaving=false;}
    }
}
