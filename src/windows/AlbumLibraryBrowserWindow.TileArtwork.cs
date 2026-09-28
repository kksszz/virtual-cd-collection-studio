using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ZipMp3Player;

public partial class AlbumLibraryBrowserWindow
{
    private readonly CancellationTokenSource _tileArtworkCancellation = new();
    private readonly LinkedList<BrowserTileArtwork> _tileArtworkCache = new();
    private readonly HashSet<BrowserTileArtwork> _tileArtworkUnavailable = [];
    private DispatcherTimer? _tileArtworkTimer;
    private bool _tileArtworkBusy;
    private DateTime _lastTileScrollUtc;

    private void StartTileArtworkLoading()
    {
        _tileArtworkTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _tileArtworkTimer.Tick += async (_, _) => await LoadVisibleTileArtworkAsync();
        _tileArtworkTimer.Start();
    }

    private void StopTileArtworkLoading()
    {
        _tileArtworkTimer?.Stop();
        _tileArtworkCancellation.Cancel();
        foreach (var state in _tileArtworkCache) state.SetImage(null);
        _tileArtworkCache.Clear();
        _tileArtworkUnavailable.Clear();
    }

    private async Task LoadVisibleTileArtworkAsync()
    {
        if (_tileArtworkBusy || !TileList.IsVisible || _tileArtworkCancellation.IsCancellationRequested
            || _tileScrollTimer.IsEnabled || DateTime.UtcNow - _lastTileScrollUtc < TimeSpan.FromMilliseconds(200)) return;
        var viewport = new Rect(0, 0, TileList.ActualWidth, TileList.ActualHeight);
        var visible = new List<AlbumLibraryBrowserItem>();
        foreach (var item in TileList.Items.OfType<AlbumLibraryBrowserItem>())
        {
            if (TileList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container || !container.IsVisible) continue;
            var bounds = container.TransformToAncestor(TileList).TransformBounds(new Rect(container.RenderSize));
            if (viewport.IntersectsWith(bounds)) visible.Add(item);
        }
        // Touch resident visible images before choosing the least recently used victim.
        foreach (var item in visible)
        {
            var node = _tileArtworkCache.Find(item.TileArtwork);
            if (node is not null) { _tileArtworkCache.Remove(node); _tileArtworkCache.AddLast(node); }
        }
        var next = visible.FirstOrDefault(item => item.LoadTileCover is not null
            && !_tileArtworkCache.Contains(item.TileArtwork) && !_tileArtworkUnavailable.Contains(item.TileArtwork));
        if (next is null) return;
        _tileArtworkBusy = true;
        var token = _tileArtworkCancellation.Token;
        try
        {
            var image = await next.LoadTileCover!(token);
            token.ThrowIfCancellationRequested();
            if (image is null) { _tileArtworkUnavailable.Add(next.TileArtwork); return; }
            // Decoding is off-thread; never rebuild collections or the 3D scene for a tile.
            if (_tileScrollTimer.IsEnabled || DateTime.UtcNow - _lastTileScrollUtc < TimeSpan.FromMilliseconds(200)) return;
            while (_tileArtworkCache.Count >= 64)
            {
                _tileArtworkCache.First!.Value.SetImage(null);
                _tileArtworkCache.RemoveFirst();
            }
            next.TileArtwork.SetImage(image);
            _tileArtworkCache.AddLast(next.TileArtwork);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _tileArtworkUnavailable.Add(next.TileArtwork); }
        finally { _tileArtworkBusy = false; }
    }
}
