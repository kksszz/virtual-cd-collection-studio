using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ZipMp3Player;

public partial class MainWindow
{
    private readonly AlbumTypeNavigation _albumTypeNavigation = new();

    private void AlbumList_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!AlbumList.IsKeyboardFocusWithin || !AlbumList.IsVisible
            || Keyboard.FocusedElement is TextBoxBase or PasswordBox
            || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0
            || string.IsNullOrEmpty(e.Text) || !e.Text.All(char.IsLetterOrDigit)) return;

        // Enumerate the filtered, ordered view, not the backing library.
        var items = AlbumList.Items.OfType<AlbumListItem>().ToList();
        var labels = items.Select(item => _albumSortMode == AlbumSortMode.Album ? item.Title : item.Artist).ToArray();
        var index = _albumTypeNavigation.Find(labels, e.Text,
            AlbumList.SelectedItem is AlbumListItem selected ? items.IndexOf(selected) : -1, Environment.TickCount64);
        e.Handled = true;
        if (index < 0) return;
        AlbumList.SelectedItem = items[index];
        AlbumList.ScrollIntoView(items[index]);
        // Focus the new row too, so the next arrow key continues from the jumped-to item.
        AlbumList.UpdateLayout();
        if (AlbumList.ItemContainerGenerator.ContainerFromItem(items[index]) is ListBoxItem row) row.Focus();
    }
}
