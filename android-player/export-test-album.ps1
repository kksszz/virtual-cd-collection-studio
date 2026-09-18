param([Parameter(Mandatory=$true)][string]$SourceFolder,[Parameter(Mandatory=$true)][string]$DestinationFile)
$ErrorActionPreference='Stop'
$source=(Get-Item -LiteralPath $SourceFolder).FullName
if(!(Test-Path -LiteralPath $source -PathType Container)){throw 'Source is not a directory'}
if(Test-Path -LiteralPath $DestinationFile){throw 'Destination already exists; refusing to overwrite'}
$files=@(Get-ChildItem -LiteralPath $source -Recurse -Force)
if(@($files | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count){throw 'Reparse points are not supported'}
$files=@($files | Where-Object { !$_.PSIsContainer })
Add-Type -AssemblyName System.IO.Compression
$output=[IO.File]::Open($DestinationFile,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try {
    $zip=[IO.Compression.ZipArchive]::new($output,[IO.Compression.ZipArchiveMode]::Create,$true,[Text.Encoding]::UTF8)
    try {
        foreach($file in $files){
            $name=[IO.Path]::GetRelativePath($source,$file.FullName).Replace('\','/')
            $entry=$zip.CreateEntry($name,[IO.Compression.CompressionLevel]::NoCompression)
            $input=[IO.File]::OpenRead($file.FullName)
            try {$target=$entry.Open();try{$input.CopyTo($target)}finally{$target.Dispose()}}finally{$input.Dispose()}
        }
    }finally{$zip.Dispose()}
}finally{$output.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($DestinationFile)
try {
    if($zip.Entries.Count -ne $files.Count){throw 'Entry count mismatch'}
    foreach($file in $files){
        $name=[IO.Path]::GetRelativePath($source,$file.FullName).Replace('\','/')
        $entry=$zip.GetEntry($name)
        if(!$entry -or $entry.Length -ne $file.Length){throw "Size mismatch: $name"}
        $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$actual=[Convert]::ToHexString($sha.ComputeHash($stream))}finally{$sha.Dispose();$stream.Dispose()}
        if($actual -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash){throw "Content mismatch: $name"}
    }
}finally{$zip.Dispose()}
"Verified $($files.Count) unchanged files in $DestinationFile"
