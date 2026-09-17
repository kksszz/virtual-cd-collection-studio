using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZipMp3Player;

internal sealed record CueTrackMetadata(string Title, string Artist);
internal sealed record CueMetadata(string Fingerprint, string ReleaseId, string Album, string Year,
    int DiscNumber, int DiscCount, List<CueTrackMetadata> Tracks, string Description);

internal static class CueMetadataStore
{
    private static string Location(string cue)
    {
        var data = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZipMp3Player");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(cue).ToUpperInvariant())));
        return Path.Combine(data, "cue-metadata", key + ".json");
    }
    internal static CueMetadata? Load(CueAlbumReader.Disc disc)
    {
        try
        {
            var path = Location(disc.CuePath);
            if (!File.Exists(path)) return null;
            var value = JsonSerializer.Deserialize<CueMetadata>(File.ReadAllText(path));
            return value?.Fingerprint == disc.Fingerprint && value.Tracks.Count == disc.Tracks.Count ? value : null;
        }
        catch { return null; }
    }
    internal static void Save(CueAlbumReader.Disc disc, CueMetadata metadata)
    {
        if (disc.Fingerprint != metadata.Fingerprint || metadata.Tracks.Count != disc.Tracks.Count)
            throw new InvalidDataException("取り込み情報がCUEと一致しません。");
        var path = Location(disc.CuePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path)) File.Replace(temp, path, path + ".previous");
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
