using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using ZipMp3Player;

internal static class BackupUiChecks
{
    internal static void Run()
    {
        _ = new Application();
        var root = Path.Combine(Path.GetTempPath(), "vccs-backup-ui-" + Guid.NewGuid().ToString("N"));
        var data = Path.Combine(root, "data");
        var app = Path.Combine(root, "app");
        var libraryA = Path.Combine(root, "music-a");
        var libraryB = Path.Combine(root, "music-b");
        try
        {
            using var window = new BackupTestWindow(new CollectionBackupWindow(data, app, [libraryA, libraryB]));
            var backup = window.Value;
            Require(backup.DataCheck.IsChecked == true && backup.ApplicationCheck.IsChecked != true,
                "management data only is selected by default");
            var folders = (ObservableCollection<BackupFolderChoice>)backup.FolderChoices.ItemsSource;
            Require(folders.Count == 2 && folders.All(folder => !folder.Include), "music folders are initially excluded");

            Invoke(backup, "SelectEverything_Click");
            Require(backup.DataCheck.IsChecked == true && backup.ApplicationCheck.IsChecked == true
                && folders.All(folder => folder.Include), "all-target selection");

            Invoke(backup, "ClearAll_Click");
            Require(backup.DataCheck.IsChecked == false && backup.ApplicationCheck.IsChecked == false
                && folders.All(folder => !folder.Include), "all-target clear");

            Invoke(backup, "SelectAll_Click");
            Require(backup.DataCheck.IsChecked == false && backup.ApplicationCheck.IsChecked == false
                && folders.All(folder => folder.Include), "music-only selection");
            Console.WriteLine("PASS backup UI: default, select all, clear all, music-only selection");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void Invoke(CollectionBackupWindow window, string name) =>
        typeof(CollectionBackupWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [null, new RoutedEventArgs()]);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class BackupTestWindow(CollectionBackupWindow window) : IDisposable
    {
        internal CollectionBackupWindow Value { get; } = window;
        public void Dispose() => Value.Close();
    }
}
