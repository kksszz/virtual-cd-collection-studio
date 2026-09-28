using ZipMp3Player;

var navigation = new AlbumTypeNavigation();
string[] artists = ["Abba", "Madonna", "Megadeth", "Metallica", "Muse", "ZZ Top"];
void Check(int actual, int expected, string name)
{
    if (actual != expected) throw new Exception($"{name}: {actual} != {expected}");
    Console.WriteLine("PASS " + name);
}
Check(navigation.Find(artists, "m", 0, 100), 1, "initial letter");
Check(navigation.Find(artists, "e", 1, 200), 2, "continuous ME");
Check(navigation.Find(artists, "t", 2, 300), 3, "continuous MET");
Check(navigation.Find(artists, "z", 3, 2000), 5, "timeout resets prefix");
navigation.Reset();
Check(navigation.Find(artists, "M", 0, 3000), 1, "case insensitive");
Check(navigation.Find(artists, "M", 1, 3100), 2, "repeated letter cycles");
Check(navigation.Find(artists, "M", 4, 3200), 1, "cycle wraps");
Check(navigation.Find(artists, "Z", 1, 3300), 5, "failed continuation falls back");
Check(navigation.Find(artists, "Q", 5, 3400), -1, "missing letter leaves selection");
navigation.Reset();
Check(navigation.Find(["Muse", "Metallica"], "m", 1, 4000), 0, "uses visible order");
navigation.Reset();
Check(navigation.Find(["Black Album", "Master of Puppets"], "m", 0, 5000), 1, "album title labels");
navigation.Reset();
Check(navigation.Find(artists, "ＭＥＴ", 0, 6000), 3, "full width and multi-character input");
Check(navigation.Find([], "A", -1, 8000), -1, "empty list");
Check(navigation.Find(artists, " ", 0, 9000), -1, "space not a navigation command");
