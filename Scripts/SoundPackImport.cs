using System.IO.Compression;
using System.Text.Json;
using Godot;

namespace MySfxMod;

/// <summary>Read only a sound pack's bindings and audio, never install executable mod content.</summary>
internal static class SoundPackImport
{
    public static SoundPackData Read(string zipPath, string assetDir, List<string> createdFiles)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > 10000) throw new InvalidDataException("压缩包文件过多。");
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p is "." or ".."))
                throw new InvalidDataException("压缩包包含不安全的文件路径。");
            total = checked(total + entry.Length);
            if (total > 256L * 1024 * 1024) throw new InvalidDataException("解压后音效包不能超过 256 MB。");
            if (!entries.TryAdd(name, entry)) throw new InvalidDataException("压缩包包含重名文件。");
        }
        var configs = entries.Where(e => e.Key == "soundpack.json" || e.Key.EndsWith("/soundpack.json", StringComparison.Ordinal)).ToArray();
        if (configs.Length != 1) throw new InvalidDataException("请选择音效工坊导出的 ZIP，里面需要且只能有一份 soundpack.json。");
        var config = configs[0];
        if (config.Value.Length > 1024 * 1024) throw new InvalidDataException("音效配置文件过大。");
        using var configStream = config.Value.Open();
        var data = JsonSerializer.Deserialize<SoundPackData>(configStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (data?.Format is not (1 or 2) || data.Sounds == null || data.Sounds.Count > 5000)
            throw new InvalidDataException("不支持的音效包格式。");
        var root = config.Key[..^"soundpack.json".Length];
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Validate every binding before writing any resource.
        foreach (var b in data.Sounds)
        {
            if (b == null || !SoundStore.IsSafeBinding(b) || !keys.Add(b.Kind + ":" + b.Key))
                throw new InvalidDataException("音效包包含无效或重复的绑定。");
            var maxBytes = b.Kind == "bgm" ? 100L * 1024 * 1024 : 20L * 1024 * 1024;
            foreach (var clip in b.AudioClips)
                if (!entries.TryGetValue(root + clip.File, out var entry) || entry.Length <= 0 || entry.Length > maxBytes)
                    throw new InvalidDataException(b.Kind == "bgm" ? "BGM 音频缺失或超过 100 MB：" + clip.File : "音效缺失或超过 20 MB：" + clip.File);
        }
        Directory.CreateDirectory(assetDir);
        foreach (var b in data.Sounds)
        {
            if (b.Clips.Count == 0) b.Clips = b.AudioClips.ToList();
            foreach (var clip in b.Clips)
            {
                if (!files.TryGetValue(clip.File, out var replacement))
                {
                    var name = Guid.NewGuid().ToString("N") + Path.GetExtension(clip.File).ToLowerInvariant();
                    var target = Path.Combine(assetDir, name);
                    createdFiles.Add(target);
                    using (var input = entries[root + clip.File].Open())
                    using (var output = File.Create(target)) input.CopyTo(output);
                    AudioStream? decoded = name.EndsWith(".mp3", StringComparison.Ordinal)
                        ? AudioStreamMP3.LoadFromFile(target) : AudioStreamWav.LoadFromFile(target);
                    if (decoded == null || decoded.GetLength() <= 0) throw new InvalidDataException("音频无法读取：" + clip.File);
                    decoded.Dispose();
                    replacement = "assets/" + name;
                    files.Add(clip.File, replacement);
                }
                clip.File = replacement;
            }
            b.File = b.Clips[0].File;
        }
        data.Format = 2;
        return data;
    }
}
