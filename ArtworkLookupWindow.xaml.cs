using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace ZipMp3Player;

public partial class ArtworkLookupWindow : Window
{
    private readonly AlbumArtworkLookupService _service = new();
    private readonly ObservableCollection<CandidateView> _candidates = [];
    private CancellationTokenSource? _cancellation;

    public AlbumArtworkCandidate? SelectedCandidate { get; private set; }

    public ArtworkLookupWindow(string album, string artist)
    {
        InitializeComponent();
        AlbumTextBox.Text = album;
        ArtistTextBox.Text = artist == "アーティスト不明" ? "" : artist;
        CandidateList.ItemsSource = _candidates;
        LocalizationService.Apply(this);
        CandidateList.SelectionChanged += (_, _) => UseButton.IsEnabled = CandidateList.SelectedItem is CandidateView;
        Loaded += async (_, _) => await SearchAsync();
        Closed += (_, _) => _cancellation?.Cancel();
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(AlbumTextBox.Text))
        {
            MessageBox.Show(this, LocalizationService.Select("アルバム名を入力してください。", "Enter an album title."),
                LocalizationService.Select("画像検索", "Artwork Search"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _cancellation?.Cancel();
        _cancellation = new CancellationTokenSource();
        SearchButton.IsEnabled = false;
        UseButton.IsEnabled = false;
        StatusText.Text = LocalizationService.Select("検索しています…", "Searching…");
        EmptyText.Text = LocalizationService.Select("検索しています…", "Searching…");
        EmptyText.Visibility = Visibility.Visible;
        _candidates.Clear();
        try
        {
            var results = await _service.SearchAsync(AlbumTextBox.Text.Trim(), ArtistTextBox.Text.Trim(), _cancellation.Token);
            foreach (var result in results) _candidates.Add(new CandidateView(result, CreateBitmap(result.ImageBytes)));
            EmptyText.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = results.Count == 0 ? LocalizationService.Select(
                "画像付きの候補が見つかりませんでした。検索条件を短くしてお試しください。",
                "No candidates with artwork were found. Try a shorter search query.") : "";
            StatusText.Text = LocalizationService.Select($"候補 {results.Count}件", $"{results.Count} candidates");
            if (_candidates.Count > 0) CandidateList.SelectedIndex = 0;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EmptyText.Text = LocalizationService.Select("検索できませんでした。インターネット接続をご確認ください。",
                "Search failed. Check your internet connection.");
            EmptyText.Visibility = Visibility.Visible;
            StatusText.Text = LocalizationService.Select("検索エラー", "Search Error");
            MessageBox.Show(this, ex.Message, LocalizationService.Select("画像検索", "Artwork Search"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SearchButton.IsEnabled = true;
            LocalizationService.Apply(this);
        }
    }

    private static BitmapImage CreateBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 180;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (CandidateList.SelectedItem is not CandidateView selected) return;
        SelectedCandidate = selected.Candidate;
        DialogResult = true;
    }

    private void CandidateList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CandidateList.SelectedItem is CandidateView) Use_Click(sender, e);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private sealed record CandidateView(AlbumArtworkCandidate Candidate, BitmapImage Preview);
}
