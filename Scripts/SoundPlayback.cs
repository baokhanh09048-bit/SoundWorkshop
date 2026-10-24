using Godot;

namespace MySfxMod;

public static class SoundPlayback
{
    public const float DefaultGain = 2.75f;
    public const float BgmDefaultGain = 2f;

    public static float EffectiveGain(float volume, bool isBgm = false) =>
        Math.Clamp(volume, 0.05f, 5f) * SoundSettings.MasterVolume * (isBgm ? BgmRuntime.GameVolumeScale * BgmDefaultGain : DefaultGain);

    public static AudioStreamPlayer? Start(Node parent, string path, float volume, float pitchScale = 1f, bool loop = false, bool isBgm = false)
    {
        try
        {
            AudioStream? stream = path.StartsWith("res://", StringComparison.Ordinal)
                ? GD.Load<AudioStream>(path)
                : Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                    ? AudioStreamMP3.LoadFromFile(path)
                    : AudioStreamWav.LoadFromFile(path);
            if (stream == null || stream.GetLength() <= 0 || !float.IsFinite(volume)) return null;
            if (stream is AudioStreamWav wav)
            {
                wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                if (loop)
                {
                    wav.LoopBegin = 0;
                    wav.LoopEnd = Math.Max(1, (int)Math.Round(wav.GetLength() * wav.MixRate));
                }
            }
            if (stream is AudioStreamMP3 mp3) mp3.Loop = loop;
            var player = new AudioStreamPlayer
            {
                Stream = stream,
                VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, EffectiveGain(volume, isBgm))),
                PitchScale = float.IsFinite(pitchScale) && pitchScale > 0f ? pitchScale : 1f,
                ProcessMode = Node.ProcessModeEnum.Always,
                // Music and one-shot effects use separate buses when the game exposes them.
                Bus = SelectBus(isBgm)
            };
            parent.AddChild(player);
            player.Finished += player.QueueFree;
            player.Play();
            return player;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[MySfxMod] Cannot play {path}: {e}");
            return null;
        }
    }

    private static string SelectBus(bool isBgm)
    {
        var preferred = isBgm ? "BGM" : "SFX";
        if (AudioServer.GetBusIndex(preferred) >= 0) return preferred;
        if (isBgm && AudioServer.GetBusIndex("Music") >= 0) return "Music";
        return "Master";
    }
}

/// <summary>One preview per editor, with an explicit stop and actual completion state.</summary>
public partial class SoundPreviewButton : Button
{
    private AudioStreamPlayer? _player;
    private string? _path;
    private float _volume = 1;
    private bool _loop;
    private bool _isBgm;
    private IReadOnlyList<(SoundClip Clip, string Path)> _playlist = [];
    private string _mode = "sequential";
    private bool _continuous;
    private SoundPlaylistCursor _cursor = new();
    public event Action<string>? StatusChanged;

    public SoundPreviewButton()
    {
        Text = "试听";
        Disabled = true;
        Pressed += Toggle;
    }

    public void Configure(string? path, float volume, bool loop = false, bool isBgm = false)
    {
        Stop();
        _path = path;
        _volume = volume;
        _loop = loop;
        _isBgm = isBgm;
        _playlist = [];
        _continuous = false;
        Disabled = path == null;
        TooltipText = path == null ? "请先为当前项目选择 WAV/MP3" : isBgm ? "循环试听当前绑定的背景音乐" : "播放当前绑定的自定义音频";
    }

    public void ConfigurePlaylist(string kind, string key, float volume, bool continuous = false, bool isBgm = false)
    {
        SoundStore.TryGet(kind, key, out var binding, out _);
        Configure(null, volume, isBgm: isBgm);
        _playlist = SoundStore.GetPlaylist(kind, key).Where(p => File.Exists(p.Path)).ToArray();
        _mode = binding?.Mode ?? "sequential";
        _continuous = continuous;
        _loop = isBgm && _playlist.Count == 1;
        _cursor = new();
        Disabled = _playlist.Count == 0;
        TooltipText = "按列表模式试听；试听进度独立于游戏中的播放顺序";
    }

    public void SetVolume(float volume)
    {
        _volume = volume;
        if (GodotObject.IsInstanceValid(_player))
            _player!.VolumeDb = Mathf.LinearToDb(Math.Max(0.0001f, SoundPlayback.EffectiveGain(volume, _isBgm)));
    }

    private void Toggle()
    {
        if (GodotObject.IsInstanceValid(_player)) { Stop(); StatusChanged?.Invoke("已停止试听。"); return; }
        StartNext();
    }

    private void StartNext()
    {
        var path = _path;
        if (_playlist.Count > 0)
        {
            var index = _cursor.Select(_playlist.Select(p => p.Clip).ToArray(), _mode);
            if (index < 0) { Stop(); return; }
            path = _playlist[index].Path;
        }
        if (path == null) return;
        _player = SoundPlayback.Start(this, path, _volume, loop: _loop, isBgm: _isBgm);
        if (_player == null) { Text = "试听"; StatusChanged?.Invoke("试听失败：无法读取音频，请重新选择文件。"); return; }
        Text = "停止试听";
        StatusChanged?.Invoke($"正在试听（{_player.Stream.GetLength():0.0} 秒）");
        var current = _player;
        _player.Finished += () =>
        {
            if (_player != current) return;
            _player = null;
            if (_continuous && _playlist.Count > 0) StartNext();
            else { Text = "试听"; StatusChanged?.Invoke("试听结束。"); }
        };
    }

    public void Stop()
    {
        if (GodotObject.IsInstanceValid(_player)) { _player!.Stop(); _player.QueueFree(); }
        _player = null;
        Text = "试听";
    }
}
