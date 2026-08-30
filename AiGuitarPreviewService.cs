using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ZipMp3Player;

public sealed class AiGuitarPreviewService
{
    public const int PreviewDurationSeconds = 60;
    private const string ProcessingRevision = "full-remix-v3";
    private readonly string _dataRoot;
    private readonly string _runtimeRoot;
    private readonly string _modelRoot;
    private readonly string _previewRoot;
    private readonly string _scriptPath;
    private readonly string _readyMarkerPath;

    public bool IsBusy { get; private set; }
    public string PythonPath => Environment.GetEnvironmentVariable("ZIPMP3PLAYER_AI_PYTHON")
        ?? Path.Combine(_runtimeRoot, "Scripts", "python.exe");
    public bool IsRuntimePresent
    {
        get
        {
            var externalPython = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_AI_PYTHON");
            if (!string.IsNullOrWhiteSpace(externalPython)) return File.Exists(externalPython);
            return HasLocalRuntimeStructure && File.Exists(_readyMarkerPath);
        }
    }
    private bool HasLocalRuntimeStructure => File.Exists(Path.Combine(_runtimeRoot, "Scripts", "python.exe"))
        && File.Exists(Path.Combine(_runtimeRoot, "pyvenv.cfg"));
    public string PreviewRoot => _previewRoot;

    public AiGuitarPreviewService(string dataRoot)
    {
        _dataRoot = dataRoot;
        _runtimeRoot = Path.Combine(dataRoot, "ai-runtime");
        _modelRoot = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_AI_MODEL_DIR")
            ?? Path.Combine(dataRoot, "ai-models");
        _previewRoot = Path.Combine(dataRoot, "ai-previews");
        _scriptPath = Path.Combine(dataRoot, "ai_guitar_preview.py");
        _readyMarkerPath = Path.Combine(_runtimeRoot, ".zipmp3player-ready");
    }

    public async Task PrepareAsync(Action<string> status, CancellationToken cancellationToken)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Directory.CreateDirectory(_dataRoot);
            WriteEmbeddedScript();
            var externalPython = Environment.GetEnvironmentVariable("ZIPMP3PLAYER_AI_PYTHON");
            var runtimeCanImport = false;
            if ((!string.IsNullOrWhiteSpace(externalPython) && File.Exists(externalPython)) || HasLocalRuntimeStructure)
            {
                try
                {
                    await RunProcessAsync(PythonPath,
                        ["-c", "import torch, demucs, numpy; print('AI runtime ready')"], null, _ => { }, cancellationToken);
                    runtimeCanImport = true;
                }
                catch { status("AI環境の不足データを修復します…"); }
            }
            if (!runtimeCanImport)
            {
                if (!HasLocalRuntimeStructure && string.IsNullOrWhiteSpace(externalPython))
                {
                    var systemPython = await FindSystemPythonAsync(cancellationToken)
                        ?? throw new InvalidOperationException("Python 3.12が見つかりません。AI追加機能の準備にはPythonが必要です。");
                    status("AI専用環境を作成しています…");
                    await RunProcessAsync(systemPython, ["-m", "venv", _runtimeRoot], null, status, cancellationToken);
                }
                status("CPU用AIライブラリを取得しています…");
                await RunProcessAsync(PythonPath,
                    ["-m", "pip", "install", "torch==2.13.0+cpu", "torchaudio==2.11.0+cpu", "--index-url", "https://download.pytorch.org/whl/cpu"],
                    null, status, cancellationToken);
                status("ギター分離モジュールを取得しています…");
                await RunProcessAsync(PythonPath,
                    ["-m", "pip", "install", "demucs==4.1.0", "numpy==2.5.2"], null, status, cancellationToken);
            }
            status("AI環境を確認しています…");
            await RunProcessAsync(PythonPath,
                ["-c", "import torch, demucs, numpy; print('AI runtime ready')"], null, status, cancellationToken);
            if (string.IsNullOrWhiteSpace(externalPython)) File.WriteAllText(_readyMarkerPath, ProcessingRevision);
        }
        finally { IsBusy = false; }
    }

    public Task<string> CreatePreviewAsync(ZipTrack track, double startSeconds, string style, double mixPercent,
        Action<string, int> progress, CancellationToken cancellationToken)
        => CreateProcessedPreviewAsync(track, startSeconds, style, mixPercent, "remix", progress, cancellationToken);

    public Task<string> CreateKaraokePreviewAsync(ZipTrack track, double startSeconds, double removalPercent,
        Action<string, int> progress, CancellationToken cancellationToken)
        => CreateProcessedPreviewAsync(track, startSeconds, "modern_metal", removalPercent, "karaoke", progress, cancellationToken);

    private async Task<string> CreateProcessedPreviewAsync(ZipTrack track, double startSeconds, string style,
        double mixPercent, string mode, Action<string, int> progress, CancellationToken cancellationToken)
    {
        if (IsBusy) throw new InvalidOperationException("別のAI処理を実行中です。");
        if (!IsRuntimePresent) throw new InvalidOperationException("先に「AI機能を準備」を実行してください。");
        IsBusy = true;
        Directory.CreateDirectory(_previewRoot);
        WriteEmbeddedScript();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{ProcessingRevision}|{mode}|{PreviewDurationSeconds}|{track.SourcePath}|{track.DataOffset}|{track.Size}|{startSeconds:0.0}|{style}|{mixPercent:0}")))[..16];
        var inputPath = Path.Combine(_previewRoot, $"input-{key}.wav");
        var outputPath = Path.Combine(_previewRoot, $"ai-{mode}-{key}.wav");
        try
        {
            progress($"選択曲から{PreviewDurationSeconds}秒を準備しています", 2);
            await Task.Run(() => ExportPreviewInput(track, startSeconds, inputPath), cancellationToken);
            var environment = new Dictionary<string, string>
            {
                ["TORCH_HOME"] = _modelRoot,
                ["HF_HOME"] = Path.Combine(_modelRoot, "hf"),
                ["PYTHONUTF8"] = "1",
                ["HF_HUB_DISABLE_SYMLINKS_WARNING"] = "1"
            };
            await RunProcessAsync(PythonPath,
                [_scriptPath, "--input", inputPath, "--output", outputPath, "--style", style,
                    "--mix", mixPercent.ToString("0", System.Globalization.CultureInfo.InvariantCulture), "--mode", mode],
                environment,
                line => ParseProgress(line, progress), cancellationToken);
            if (!File.Exists(outputPath)) throw new InvalidOperationException("AIプレビューが生成されませんでした。");
            return outputPath;
        }
        finally
        {
            try { if (File.Exists(inputPath)) File.Delete(inputPath); } catch { }
            IsBusy = false;
        }
    }

    public void DeleteAllData()
    {
        if (IsBusy) throw new InvalidOperationException("AI処理中は削除できません。");
        foreach (var path in new[] { _runtimeRoot, _modelRoot, _previewRoot })
        {
            if (!Directory.Exists(path)) continue;
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(Path.GetFullPath(_dataRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;
            Directory.Delete(fullPath, true);
        }
        if (File.Exists(_scriptPath)) File.Delete(_scriptPath);
    }

    private static void ExportPreviewInput(ZipTrack track, double startSeconds, string outputPath)
    {
        using var trackReader = TrackAudioReader.Open(track);
        var reader = trackReader.Reader;
        var safeMaximum = Math.Max(0, reader.TotalTime.TotalSeconds - 0.05);
        reader.CurrentTime = TimeSpan.FromSeconds(Math.Clamp(startSeconds, 0, safeMaximum));
        var samples = reader.ToSampleProvider();
        using var writer = new WaveFileWriter(outputPath,
            WaveFormat.CreateIeeeFloatWaveFormat(samples.WaveFormat.SampleRate, samples.WaveFormat.Channels));
        var buffer = new float[8192];
        var remaining = samples.WaveFormat.SampleRate * samples.WaveFormat.Channels * PreviewDurationSeconds;
        while (remaining > 0)
        {
            var read = samples.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read == 0) break;
            writer.WriteSamples(buffer, 0, read);
            remaining -= read;
        }
    }

    private void WriteEmbeddedScript()
    {
        Directory.CreateDirectory(_dataRoot);
        using var source = typeof(AiGuitarPreviewService).Assembly
            .GetManifestResourceStream("ZipMp3Player.ai_guitar_preview.py")
            ?? throw new InvalidOperationException("AI処理スクリプトを読み込めません。");
        using var target = new FileStream(_scriptPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        source.CopyTo(target);
    }

    private static void ParseProgress(string line, Action<string, int> progress)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var message = root.TryGetProperty("message", out var messageNode) ? messageNode.GetString() : null;
            var percent = root.TryGetProperty("percent", out var percentNode) ? percentNode.GetInt32() : 0;
            if (!string.IsNullOrWhiteSpace(message)) progress(message, percent);
        }
        catch { }
    }

    private static async Task<string?> FindSystemPythonAsync(CancellationToken cancellationToken)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var likely = Directory.Exists(Path.Combine(localAppData, "Programs", "Python"))
            ? Directory.EnumerateFiles(Path.Combine(localAppData, "Programs", "Python"), "python.exe", SearchOption.AllDirectories).FirstOrDefault()
            : null;
        if (likely is not null) return likely;
        var where = new ProcessStartInfo("where.exe", "python") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(where);
        if (process is null) return null;
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(File.Exists);
    }

    private static async Task RunProcessAsync(string executable, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment, Action<string> output, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("AI処理を開始できませんでした。");
        var errors = new StringBuilder();
        var stdout = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line) output(line);
        }, cancellationToken);
        var stderr = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
            {
                errors.AppendLine(line);
                if (line.Contains('%')) output(line);
            }
        }, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdout, stderr);
        if (process.ExitCode != 0)
        {
            var detail = errors.ToString().Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"AI処理が終了コード{process.ExitCode}で停止しました。"
                : detail.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim());
        }
    }
}
