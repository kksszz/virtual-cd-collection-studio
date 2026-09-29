using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace ZipMp3Player;

// Download the public list, but never guess from a partial model or another vendor.
internal static class DriveOffsetLookup
{
    internal static readonly Uri Source=new("https://www.accuraterip.com/driveoffsets.htm");
    internal sealed record Entry(string Name,int? Samples,int Submissions,int Agreement);
    internal sealed record Result(string State,string Message,Entry? Match=null);
    private static readonly Regex Rows=new(@"<tr\b[^>]*>(.*?)</tr\s*>",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromSeconds(1));
    private static readonly Regex Cells=new(@"<td\b[^>]*>(.*?)</td\s*>",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromSeconds(1));
    private static readonly Regex Tags=new(@"<[^>]*>",RegexOptions.Singleline,TimeSpan.FromSeconds(1));
    private static readonly Regex Space=new(@"\s+",RegexOptions.None,TimeSpan.FromSeconds(1));
    private static string Normalize(string text)=>Space.Replace(text.Trim()," ").ToUpperInvariant();
    private static string Vendor(string text)=>Normalize(text) switch {
        "HL-DT-ST"=>"LG ELECTRONICS","JLMS"=>"LITE-ON","MATSHITA"=>"PANASONIC",var name=>name
    };
    internal static List<Entry> Parse(string html)
    {
        var entries=new List<Entry>();
        foreach(Match row in Rows.Matches(html)){
            var cells=Cells.Matches(row.Groups[1].Value);
            if(cells.Count!=4)continue;
            string Text(int n)=>Space.Replace(WebUtility.HtmlDecode(Tags.Replace(cells[n].Groups[1].Value,""))," ").Trim();
            string name=Text(0),value=Text(1);
            if(!int.TryParse(Text(2),NumberStyles.None,CultureInfo.InvariantCulture,out int count)||count<1||!int.TryParse(Text(3).TrimEnd('%'),NumberStyles.None,CultureInfo.InvariantCulture,out int agreement)||agreement<0||agreement>100)continue;
            int? samples=null;
            if(int.TryParse(value,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int offset)&&Math.Abs((long)offset)<=CdOffsetReader.MaximumOffset)samples=offset;
            else if(!value.Equals("[Purged]",StringComparison.OrdinalIgnoreCase))continue;
            entries.Add(new(name,samples,count,agreement));
        }
        if(entries.Count==0)throw new InvalidDataException("補正値一覧の形式を確認できませんでした。");
        return entries;
    }
    internal static Result Find(IReadOnlyList<Entry> entries,string identity)
    {
        var parts=identity.Split('|');
        if(parts.Length!=4||string.IsNullOrWhiteSpace(parts[0])||string.IsNullOrWhiteSpace(parts[1]))return new("unknown","ドライブのメーカー・型番を識別できません。手入力してください。");
        string vendor=Vendor(parts[0]),model=Normalize(parts[1]);
        var matching=entries.Where(e=>{
            int split=e.Name.IndexOf(" - ",StringComparison.Ordinal);
            return split>0&&Vendor(e.Name[..split])==vendor&&Normalize(e.Name[(split+3)..])==model;
        }).ToArray();
        if(matching.Length==0)return new("not-found","公式一覧にメーカー・型番の完全一致がありません。値は変更せず、手入力できます。");
        if(matching.Any(e=>e.Samples is null))return new("purged","この型番は補正値が一定でないため公式一覧から除外されています。自動設定しません。");
        if(matching.Select(e=>e.Samples).Distinct().Count()!=1||matching.Any(e=>e.Agreement!=100))return new("ambiguous","登録値の不一致または一致率100%未満です。自動設定せず、実CDでの確認が必要です。");
        var chosen=matching.OrderByDescending(e=>e.Submissions).First();
        return new("found",$"公式一覧：{chosen.Name} ／ {chosen.Samples:+0;-0;0}サンプル（報告{chosen.Submissions}件・一致率100%）。取り込み後にAccurateRip結果を確認してください。",chosen);
    }
    internal static async Task<List<Entry>> Fetch(CancellationToken token,HttpClient? supplied=null)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var owned=supplied is null?new HttpClient():null;var client=supplied??owned!;
        using var request=new HttpRequestMessage(HttpMethod.Get,Source);request.Headers.UserAgent.ParseAdd("VirtualCDCollectionStudio/0.89.0");
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();const int maximum=4*1024*1024;
        if(response.Content.Headers.ContentLength>maximum)throw new InvalidDataException("補正値一覧のサイズが大きすぎます。");
        using var input=await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);using var output=new MemoryStream();var buffer=new byte[8192];int read;
        while((read=await input.ReadAsync(buffer,deadline.Token).ConfigureAwait(false))>0){if(output.Length+read>maximum)throw new InvalidDataException("補正値一覧のサイズが大きすぎます。");output.Write(buffer,0,read);}
        token.ThrowIfCancellationRequested();
        // The official list declares windows-1252; model names and numeric fields are ASCII.
        return Parse(Encoding.Latin1.GetString(output.ToArray()));
    }
}
