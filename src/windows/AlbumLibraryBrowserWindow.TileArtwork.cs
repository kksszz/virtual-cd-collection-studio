using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ZipMp3Player;

// Keep recently decoded browser tiles across modal browser windows. The main
// album model invalidates an entry whenever its artwork summary changes.
internal static class BrowserTileArtworkCache
{
    private const int Capacity = 64;
    private static readonly object Gate = new();
    private static readonly LinkedList<(string Key, BitmapSource Image)> Recent = new();
    private static readonly Dictionary<string, LinkedListNode<(string Key, BitmapSource Image)>> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    internal static bool TryGet(string key, out BitmapSource? image)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out var node)) { image = null; return false; }
            Recent.Remove(node);
            Recent.AddLast(node);
            image = node.Value.Image;
            return true;
        }
    }

    internal static void Store(string key, BitmapSource image)
    {
        if (!image.IsFrozen) return;
        lock (Gate)
        {
            if (Entries.TryGetValue(key, out var old)) Recent.Remove(old);
            var node = Recent.AddLast((key, image));
            Entries[key] = node;
            while (Recent.Count > Capacity)
            {
                var first = Recent.First!;
                Recent.RemoveFirst();
                Entries.Remove(first.Value.Key);
            }
        }
    }

    internal static void Invalidate(string key)
    {
        lock (Gate)
        {
            if (!Entries.Remove(key, out var node)) return;
            Recent.Remove(node);
        }
    }
}

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
            else if (BrowserTileArtworkCache.TryGet(item.Key, out var cached) && cached is not null)
            {
                RetainTileArtwork(item.TileArtwork, cached);
            }
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
            RetainTileArtwork(next.TileArtwork, image);
            BrowserTileArtworkCache.Store(next.Key, image);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { _tileArtworkUnavailable.Add(next.TileArtwork); }
        finally { _tileArtworkBusy = false; }
    }

    private void RetainTileArtwork(BrowserTileArtwork state, BitmapSource image)
    {
        while (_tileArtworkCache.Count >= 64)
        {
            _tileArtworkCache.First!.Value.SetImage(null);
            _tileArtworkCache.RemoveFirst();
        }
        state.SetImage(image);
        _tileArtworkCache.AddLast(state);
    }
}
