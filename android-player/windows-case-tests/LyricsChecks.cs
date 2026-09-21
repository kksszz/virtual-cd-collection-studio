using System.IO;
using System.Reflection;
using System.Text.Json;
using ZipMp3Player;
internal static class LyricsChecks {
 public static void Run(){
  var root=Path.Combine(Path.GetTempPath(),"lyrics-sync-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  Environment.SetEnvironmentVariable("ZIPMP3PLAYER_DATA_DIR",Path.Combine(root,"app"));
  var music=Path.Combine(root,"music");Directory.CreateDirectory(music);var audio=Path.Combine(music,"song.mp3");File.WriteAllBytes(audio,[1,2,3]);
  var track=new ZipTrack{SourcePath=audio,FileName="song.mp3",Title="歌詞テスト",TrackNumber=1};
  var album=new ZipAlbum{Path=music,Tracks=[track]};
  var export=typeof(MainWindow).GetMethod("ExportMobileLyrics",BindingFlags.Static|BindingFlags.NonPublic)!;
  string Read(){var path=(string)export.Invoke(null,[album,root])!;using var json=JsonDocument.Parse(File.ReadAllBytes(path));return json.RootElement.GetProperty("tracks")[0].GetProperty("text").GetString()!;}
  File.WriteAllText(Path.ChangeExtension(audio,".lrc"),"[00:01.00]日本語の歌詞");
  if(Read()!="[00:01.00]日本語の歌詞")throw new Exception("External LRC");
  var saved=(string)typeof(MainWindow).GetMethod("GetSavedLyricsPath",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[album,track])!;
  Directory.CreateDirectory(Path.GetDirectoryName(saved)!);File.WriteAllText(saved,"保存した歌詞");
  if(Read()!="保存した歌詞")throw new Exception("Saved lyrics priority");
  var glb=Path.Combine(root,"case.glb");File.WriteAllBytes(glb,[1,2,3]);
  var target=Path.Combine(root,"target");var sync=MobileSync.Open(new MobileSync.FolderTarget(target)).GetAwaiter().GetResult();
  sync.Send(music,"test","test",glb,lyrics:Path.Combine(root,"lyrics.json")).GetAwaiter().GetResult();
  string Hash(){using var json=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(target,"vcd-sync.json")));var record=json.RootElement.GetProperty("albums").EnumerateObject().Single().Value;var path=record.GetProperty("lyrics").GetString()!;if(!record.GetProperty("files").EnumerateArray().Any(f=>f.GetProperty("path").GetString()==path))throw new Exception("Lyrics not in inventory");return record.GetProperty("lyricsSha256").GetString()!;}
  var previous=Hash();File.WriteAllText(saved,"");if(Read()!="")throw new Exception("Empty override lost");
  sync.Send(music,"test","test",glb,lyrics:Path.Combine(root,"lyrics.json")).GetAwaiter().GetResult();if(Hash()==previous)throw new Exception("Lyrics-only update lost");
  Console.WriteLine("PASS lyrics: external LRC, saved priority, empty override, manifest inventory, lyrics-only update. "+root);
 }
}
