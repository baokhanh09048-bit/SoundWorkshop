using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace MySfxMod;

public sealed class SoundClip
{
    public string File { get; set; } = "";
    public string Name { get; set; } = "";
    public double Weight { get; set; } = 1;
}

public sealed class SoundBinding
{
    public string Kind { get; set; } = "";
    public string Key { get; set; } = "";
    public string File { get; set; } = "";
    public float Volume { get; set; } = 1f;
    public string Mode { get; set; } = "sequential";
    public List<SoundClip> Clips { get; set; } = [];
    [JsonIgnore] public IReadOnlyList<SoundClip> AudioClips => Clips.Count > 0 ? Clips :
        string.IsNullOrEmpty(File) ? [] : [new SoundClip { File = File, Name = Path.GetFileName(File) }];
    [JsonIgnore] public string Summary => $"{AudioClips.Count} 个音频 · {(Mode == "random" ? "随机播放" : "顺序播放")}";
}

public sealed class SoundPackData
{
    public int Format { get; set; } = 2;
    public string Name { get; set; } = "My sounds";
    public List<SoundBinding> Sounds { get; set; } = [];
}

public static class SoundStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly string Home = ProjectSettings.GlobalizePath("user://MySfxMod");
    private static readonly string LocalDataPath = Path.Combine(Home, "sounds.json");
    private static readonly string AssetDir = Path.Combine(Home, "assets");
    public static string ExportDirectory => Path.Combine(Home, "exports");
    public static string BackupDirectory => Path.Combine(Home, "backups");
    public static event Action? Changed;
    private static readonly Dictionary<string, (SoundBinding Binding, string Root)> Effective = new(StringComparer.Ordinal);
    private static SoundPackData _local = new();
    private static string _packSignature = "";
    private static readonly Dictionary<string, SoundPlaylistCursor> Cursors = new(StringComparer.Ordinal);

    public static IReadOnlyList<SoundBinding> LocalBindings => _local.Sounds;
    public static IReadOnlyList<SoundBinding> ActiveBindings
    {
        get { Refresh(); return Effective.Values.Select(entry => entry.Binding).ToArray(); }
    }

    public static void Initialize()
    {
        _local = new();
        _packSignature = "";
        Cursors.Clear();
        Directory.CreateDirectory(AssetDir);
        if (File.Exists(LocalDataPath))
        {
            try { _local = JsonSerializer.Deserialize<SoundPackData>(File.ReadAllText(LocalDataPath), JsonOptions) ?? new(); }
            catch (Exception e) { GD.PrintErr($"[MySfxMod] Cannot read sound settings: {e}"); }
        }
        Refresh();
    }

    public static void Refresh()
    {
        var packs = SoundSettings.EnableSoundPacks
            ? ModManager.GetLoadedMods().Where(m => m.manifest?.id?.StartsWith("MySfxPack_", StringComparison.Ordinal) == true)
                .OrderBy(m => m.manifest!.id, StringComparer.Ordinal).ToArray()
            : [];
        var signature = string.Join("|", packs.Select(m => m.path)) + ":" + SoundSettings.EnableSoundPacks;
        if (signature == _packSignature && Effective.Count > 0) return;
        _packSignature = signature;
        Effective.Clear();
        foreach (var mod in packs)
        {
            var packPath = Path.Combine(mod.path, "soundpack.json");
            if (!File.Exists(packPath)) continue;
            try
            {
                var pack = JsonSerializer.Deserialize<SoundPackData>(File.ReadAllText(packPath), JsonOptions);
                if (pack?.Format is not (1 or 2)) continue;
                foreach (var binding in pack.Sounds)
                    Put(binding, mod.path);
            }
            catch (Exception e) { GD.PrintErr($"[MySfxMod] Cannot load {packPath}: {e}"); }
        }
        foreach (var binding in _local.Sounds) Put(binding, Home);
    }

    private static void Put(SoundBinding binding, string root)
    {
        if (!IsSafeBinding(binding)) return;
        if (!binding.AudioClips.Any(clip => File.Exists(ClipPath(root, clip)))) return;
        Effective[binding.Kind + ":" + binding.Key] = (binding, root);
    }

    internal static bool IsSafeBinding(SoundBinding b) =>
        (b.Kind is "event" or "debug" or "card" or "relic" or "monster_hit" or "monster_death" or "monster_action" ||
            b.Kind == "bgm" && BgmRuntime.IsKnownKey(b.Key)) && !string.IsNullOrWhiteSpace(b.Key) &&
        float.IsFinite(b.Volume) && b.Volume is > 0f and <= 5f && b.Mode is "sequential" or "random" &&
        b.Clips != null && b.AudioClips.Count is > 0 and <= 512 &&
        b.AudioClips.All(clip => clip != null && IsSafeAsset(clip.File) && clip.Name != null && clip.Name.Length <= 256 &&
            double.IsFinite(clip.Weight) && clip.Weight is >= 0 and <= 1000000) &&
        b.AudioClips.Sum(clip => clip.Weight) > 0;

    private static bool IsSafeAsset(string file) => file != null && file.StartsWith("assets/", StringComparison.Ordinal) &&
        !file.Contains("..", StringComparison.Ordinal) && !file.Contains('\\') && !file.Contains(':') &&
        Path.GetExtension(file).ToLowerInvariant() is ".wav" or ".mp3";
    private static string ClipPath(string root, SoundClip clip) => Path.Combine(root, clip.File.Replace('/', Path.DirectorySeparatorChar));

    public static bool TryGet(string kind, string key, out SoundBinding binding, out string path)
    {
        Refresh();
        if (Effective.TryGetValue(kind + ":" + key, out var found))
        {
            var available = found.Binding.AudioClips.FirstOrDefault(clip => File.Exists(ClipPath(found.Root, clip)));
            if (available != null)
            {
                binding = found.Binding;
                path = ClipPath(found.Root, available);
                return true;
            }
        }
        binding = null!;
        path = "";
        return false;
    }

    // UI lookups never consume the gameplay sequence.
    public static bool TrySelect(string kind, string key, out SoundBinding binding, out string path)
    {
        if (!TryGet(kind, key, out binding, out path)) return false;
        var found = Effective[kind + ":" + key];
        var available = binding.AudioClips.Where(clip => File.Exists(ClipPath(found.Root, clip))).ToArray();
        var id = kind + ":" + key;
        if (!Cursors.TryGetValue(id, out var cursor)) Cursors[id] = cursor = new();
        var index = cursor.Select(available, binding.Mode, found.Root);
        if (index < 0) return false;
        path = ClipPath(found.Root, available[index]);
        return true;
    }

    public static IReadOnlyList<(SoundClip Clip, string Path)> GetPlaylist(string kind, string key)
    {
        if (!TryGet(kind, key, out var binding, out _)) return [];
        var root = Effective[kind + ":" + key].Root;
        return binding.AudioClips.Select(clip => (clip, ClipPath(root, clip))).ToArray();
    }

    public static void Set(string kind, string key, string source, float volume)
    {
        AddFiles(kind, key, [source], volume, replace: true);
    }

    private static string CopyAudio(string kind, string source, List<string> created)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("Audio file not found", source);
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (extension is not ".wav" and not ".mp3") throw new InvalidDataException("Only WAV and MP3 are supported in this version.");
        var maxBytes = kind == "bgm" ? 100L * 1024 * 1024 : 20L * 1024 * 1024;
        if (new FileInfo(source).Length > maxBytes)
            throw new InvalidDataException(kind == "bgm" ? "BGM 文件不能超过 100 MB。" : "音效文件不能超过 20 MB。");
        using (var stream = File.OpenRead(source))
        {
            Span<byte> header = stackalloc byte[4];
            if (stream.Read(header) < 4 ||
                (extension == ".wav" && !header.SequenceEqual("RIFF"u8)) ||
                (extension == ".mp3" && !header[..3].SequenceEqual("ID3"u8) && !(header[0] == 0xff && (header[1] & 0xe0) == 0xe0)))
                throw new InvalidDataException("The file contents do not match its WAV/MP3 extension.");
        }
        var name = Guid.NewGuid().ToString("N") + extension;
        var target = Path.Combine(AssetDir, name);
        created.Add(target);
        File.Copy(source, target);
        using var decoded = extension == ".mp3" ? (AudioStream?)AudioStreamMP3.LoadFromFile(target) : AudioStreamWav.LoadFromFile(target);
        if (decoded == null || decoded.GetLength() <= 0) throw new InvalidDataException("音频无法读取：" + Path.GetFileName(source));
        return "assets/" + name;
    }

    private static SoundBinding Clone(SoundBinding binding) => new()
    {
        Kind = binding.Kind, Key = binding.Key, Volume = binding.Volume, Mode = binding.Mode,
        Clips = binding.AudioClips.Select(c => new SoundClip { File = c.File, Name = c.Name, Weight = c.Weight }).ToList()
    };

    private static SoundBinding EditableCopy(string kind, string key, List<string> created)
    {
        var local = _local.Sounds.FirstOrDefault(b => b.Kind == kind && b.Key == key);
        if (local != null) return Clone(local);
        if (!TryGet(kind, key, out var shared, out _)) return new() { Kind = kind, Key = key };
        var copy = Clone(shared);
        var paths = GetPlaylist(kind, key);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < copy.Clips.Count; i++)
        {
            var source = paths[i].Path;
            if (!files.TryGetValue(source, out var file)) files[source] = file = CopyAudio(kind, source, created);
            copy.Clips[i].File = file;
        }
        return copy;
    }

    public static void AddFiles(string kind, string key, IReadOnlyList<string> sources, float volume = 1, bool replace = false)
    {
        if (sources.Count == 0) return;
        var created = new List<string>();
        try
        {
            var binding = replace ? new SoundBinding { Kind = kind, Key = key, Volume = Math.Clamp(volume, 0.05f, 5f) }
                : EditableCopy(kind, key, created);
            if (binding.Clips.Count == 0) binding.Volume = Math.Clamp(volume, 0.05f, 5f);
            if (binding.Clips.Count + sources.Count > 512) throw new InvalidDataException("一个列表最多可添加 512 个音频。");
            foreach (var source in sources)
                binding.Clips.Add(new SoundClip { File = CopyAudio(kind, source, created), Name = Path.GetFileName(source) });
            Commit(binding);
        }
        catch { foreach (var file in created) if (File.Exists(file)) File.Delete(file); throw; }
    }

    private static void Edit(string kind, string key, Action<SoundBinding> edit)
    {
        var created = new List<string>();
        try
        {
            var copy = EditableCopy(kind, key, created);
            if (copy.Clips.Count == 0) return;
            edit(copy);
            Commit(copy);
        }
        catch { foreach (var file in created) if (File.Exists(file)) File.Delete(file); throw; }
    }

    private static void Commit(SoundBinding binding)
    {
        binding.File = binding.Clips.FirstOrDefault()?.File ?? "";
        if (binding.Clips.Count > 0 && !IsSafeBinding(binding)) throw new InvalidDataException("列表设置无效；至少保留一个正权重音频。");
        var previous = _local.Sounds;
        _local.Sounds = previous.Where(b => b.Kind != binding.Kind || b.Key != binding.Key).ToList();
        if (binding.Clips.Count > 0) _local.Sounds.Add(binding);
        try { Save(); }
        catch { _local.Sounds = previous; throw; }
    }

    public static void SetMode(string kind, string key, string mode) => Edit(kind, key, b => b.Mode = mode);
    public static void SetWeight(string kind, string key, int index, double weight) => Edit(kind, key, b => b.Clips[index].Weight = weight);
    public static void MoveClip(string kind, string key, int index, int destination) => Edit(kind, key, b =>
    {
        if (destination < 0 || destination >= b.Clips.Count) return;
        var clip = b.Clips[index]; b.Clips.RemoveAt(index); b.Clips.Insert(destination, clip);
    });
    public static void RemoveClip(string kind, string key, int index) => Edit(kind, key, b => b.Clips.RemoveAt(index));

    public static string PlaylistStamp(SoundBinding binding) => JsonSerializer.Serialize(new { binding.Mode, Clips = binding.AudioClips });

    public static void NotifyPlaylistChanged() => Changed?.Invoke();

    public static void Remove(string kind, string key)
    {
        _local.Sounds.RemoveAll(b => b.Kind == kind && b.Key == key);
        Save();
    }

    public static int ClearPersonalLibrary()
    {
        var removed = _local.Sounds.Count;
        _local = new SoundPackData { Name = "My sounds" };
        Cursors.Clear();
        Directory.CreateDirectory(AssetDir);
        foreach (var file in Directory.EnumerateFiles(AssetDir, "*", SearchOption.TopDirectoryOnly))
        {
            try { File.Delete(file); }
            catch (Exception e) { GD.PrintErr($"[MySfxMod] Cannot delete unused audio {file}: {e}"); }
        }
        Save();
        return removed;
    }

    public static void SetVolume(string kind, string key, float volume)
    {
        Edit(kind, key, b => b.Volume = Math.Clamp(volume, 0.05f, 5f));
    }

    internal static void MigrateLegacyBgmBindings(string menuKey, IReadOnlyList<string> runKeys)
    {
        var legacyMenu = _local.Sounds.FirstOrDefault(b => b.Kind == "bgm" && b.Key == "menu");
        var changed = false;
        if (legacyMenu != null && !_local.Sounds.Any(b => b.Kind == "bgm" && b.Key == menuKey))
        {
            var migrated = Clone(legacyMenu); migrated.Key = menuKey; migrated.File = migrated.Clips[0].File;
            _local.Sounds.Add(migrated);
            changed = true;
        }
        if (legacyMenu != null)
        {
            _local.Sounds.RemoveAll(b => b.Kind == "bgm" && b.Key == "menu");
            changed = true;
        }

        // 0.7.1 expanded the old single run binding into every official run event.
        // Collapse only that exact signature; independently edited events must survive.
        var runBindings = _local.Sounds.Where(b => b.Kind == "bgm" && runKeys.Contains(b.Key)).ToArray();
        if (!_local.Sounds.Any(b => b.Kind == "bgm" && b.Key == BgmRuntime.LegacyRunKey) &&
            runBindings.Length == runKeys.Count &&
            runKeys.All(key => runBindings.Count(b => b.Key == key) == 1) &&
            runBindings.All(b => b.Clips.Count == 0 && b.File == runBindings[0].File && b.Volume == runBindings[0].Volume) &&
            IsSafeBinding(runBindings[0]) && File.Exists(Path.Combine(Home, runBindings[0].File.Replace('/', Path.DirectorySeparatorChar))))
        {
            try
            {
                Directory.CreateDirectory(BackupDirectory);
                File.Copy(LocalDataPath, Path.Combine(BackupDirectory, $"BGM迁移前_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.json"));
                _local.Sounds.RemoveAll(b => b.Kind == "bgm" && runKeys.Contains(b.Key));
                _local.Sounds.Add(new SoundBinding { Kind = "bgm", Key = BgmRuntime.LegacyRunKey,
                    File = runBindings[0].File, Volume = runBindings[0].Volume });
                changed = true;
            }
            catch (Exception e) { GD.PrintErr($"[MySfxMod] Cannot back up old BGM bindings: {e}"); }
        }

        if (changed) Save();
    }

    private static void Save()
    {
        _local.Format = 2;
        Directory.CreateDirectory(Home);
        WriteData(_local);
        _packSignature = "";
        Refresh();
    }

    private static void WriteData(SoundPackData data)
    {
        var pending = LocalDataPath + ".tmp";
        try
        {
            File.WriteAllText(pending, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(pending, LocalDataPath, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    public static (string Name, int Count, string Backup) Import(string zipPath)
    {
        var createdFiles = new List<string>();
        SoundPackData data;
        string backup;
        try
        {
            data = SoundPackImport.Read(zipPath, AssetDir, createdFiles);
            Directory.CreateDirectory(BackupDirectory);
            backup = Path.Combine(BackupDirectory, $"导入前_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.zip");
            WriteArchive(backup, "MySfxPack_Backup", _local.Name, "本地备份", _local.Sounds.ToArray());
            WriteData(data);
        }
        catch
        {
            // Only remove freshly generated files, never any previous library resources.
            foreach (var file in createdFiles) if (File.Exists(file)) File.Delete(file);
            throw;
        }
        _local = data;
        Cursors.Clear();
        _packSignature = "";
        Refresh();
        Changed?.Invoke();
        return (data.Name, data.Sounds.Count, backup);
    }

    public static string Export(string name, string author)
    {
        var slug = new string(name.Where(char.IsLetterOrDigit).Take(28).ToArray());
        if (slug.Length == 0) slug = "Custom";
        var id = "MySfxPack_" + slug;
        var exportRoot = ExportDirectory;
        Directory.CreateDirectory(exportRoot);
        var output = Path.Combine(exportRoot, id + ".zip");
        var bindings = _local.Sounds.Where(IsSafeBinding).ToArray();
        if (bindings.Length == 0) throw new InvalidOperationException("No sound has been configured.");
        WriteArchive(output, id, name, author, bindings);
        return output;
    }

    private static void WriteArchive(string output, string id, string name, string author, SoundBinding[] bindings)
    {
        var assets = bindings.SelectMany(b => b.AudioClips).Select(c => c.File).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var file in assets)
            if (!File.Exists(Path.Combine(Home, file.Replace('/', Path.DirectorySeparatorChar))))
                throw new FileNotFoundException("Missing sound: " + file);
        if (File.Exists(output)) File.Delete(output);
        using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
        var manifest = new
        {
            id, name, author, description = "Custom audio pack for MySfxMod", version = "1.0.0",
            has_pck = false, has_dll = false,
            dependencies = new[] { new { id = "src", min_version = bindings.Any(b => b.Kind == "bgm" && BgmRuntime.IsBossKey(b.Key)) ? "0.8.2" : "0.8.0" } },
            affects_gameplay = false
        };
        AddText(zip, id + "/" + id + ".json", JsonSerializer.Serialize(manifest, JsonOptions));
        AddText(zip, id + "/soundpack.json", JsonSerializer.Serialize(new SoundPackData { Name = name, Sounds = bindings.ToList() }, JsonOptions));
        foreach (var file in assets)
            zip.CreateEntryFromFile(Path.Combine(Home, file.Replace('/', Path.DirectorySeparatorChar)), id + "/" + file);
    }

    private static void AddText(ZipArchive archive, string name, string contents)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(contents);
    }
}
