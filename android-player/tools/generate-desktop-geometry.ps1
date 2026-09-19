param([Parameter(Mandatory=$true)][string]$Desktop)
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $PSScriptRoot '..\app\src\main\java\jp\virtualcd\player\case3d\CaseGeometry.java')
$body=$source.Substring($source.IndexOf('    private void buildCase'))
$body=$body.Replace('CasePackage','Options').Replace('boolean','bool').Replace('String','string').Replace('FloatBuffer','float[]')
$body=$body.Replace('.length','.Length').Replace('meshes.size()','meshes.Count').Replace('meshes.add(','meshes.Add(')
$body=[regex]::Replace($body,'meshes\.get\(([^)]+)\)','meshes[$1]')
$body=$body.Replace('data.tray.equals("White")','data.tray == "White"').Replace('data.tray.equals("Gray")','data.tray == "Gray"').Replace('data.tray.equals("Clear")','data.tray == "Clear"').Replace('data.images.containsKey("disc")','data.hasDiscImage')
$body=[regex]::Replace($body,'for\((float|int) (\w+):([^\r\n]*?)\)','foreach($1 $2 in $3)')
$body=[regex]::Replace($body,'\bMath\.(abs|cos|sin|min|max|sqrt)\b',{param($m) 'Math.'+$m.Groups[1].Value.Substring(0,1).ToUpper()+$m.Groups[1].Value.Substring(1)})
$body=[regex]::Replace($body,'\bout\b','cursor')
$body=$body.Replace('float[] src=m.vertices.duplicate();src.position(0);src.get(values,at,m.count*8);','Array.Copy(m.vertices,0,values,at,m.count*8);')
$body=$body.Replace('meshes.subList(start,meshes.Count).clear();','meshes.RemoveRange(start,meshes.Count-start);')
$header=@'
// Generated from Android CaseGeometry.java by tools/generate-desktop-geometry.ps1.
// Keep the independently authored mobile model identical across exporters. No third-party STL.
namespace ZipMp3Player;
internal sealed class MobileGlbGeometry {
    internal sealed record Options(string tray,bool hasObi,float obiFrontWidth,float obiBackWidth,bool hasDiscImage);
    const int BASE=0,LID=1,DISC=2,OBI=3,FILM_TOP=4,FILM_BOTTOM=5,TAPE=6;
    static readonly float[] WHITE={1,1,1,1},PAPER={.96f,.95f,.91f,1},GLASS={.72f,.85f,.90f,.22f},EDGE={.65f,.79f,.83f,.55f},SILVER={.77f,.81f,.85f,1};
    internal sealed class Mesh {
        internal readonly float[] vertices,color;
        internal readonly int count,part,finish;
        internal readonly string texture;
        internal Mesh(float[] values,string role,float[] tint,int section,int surface){vertices=values;color=tint;count=values.Length/8;part=section;finish=surface;texture=role;}
    }
    readonly List<Mesh> meshes=new();
    int part,finish;
    internal static List<Mesh> Build(Options options){var g=new MobileGlbGeometry();g.buildCase(options);return g.Compact();}
    List<Mesh> Compact(){
        var result=new List<Mesh>();var groups=new Dictionary<string,List<Mesh>>();
        foreach(var mesh in meshes){if(mesh.color[3]<1||mesh.part>=OBI){result.Add(mesh);continue;}
            string key=mesh.part+":"+mesh.finish+":"+mesh.texture+":"+string.Join(",",mesh.color.Select(v=>v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if(!groups.TryGetValue(key,out var group))groups[key]=group=new();group.Add(mesh);}
        foreach(var group in groups.Values){float[] data=new float[group.Sum(m=>m.vertices.Length)];int offset=0;
            foreach(var mesh in group){Array.Copy(mesh.vertices,0,data,offset,mesh.vertices.Length);offset+=mesh.vertices.Length;}
            var first=group[0];result.Add(new Mesh(data,first.texture,first.color,first.part,first.finish));}
        return result;
    }
'@
[IO.File]::WriteAllText((Join-Path $Desktop 'MobileGlbGeometry.cs'),$header+[Environment]::NewLine+$body,[Text.UTF8Encoding]::new($false))
