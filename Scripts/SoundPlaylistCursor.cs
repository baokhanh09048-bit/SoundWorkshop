using System.Text.Json;

namespace MySfxMod;

/// <summary>A private cursor per binding or preview; configuration reads never advance it.</summary>
public sealed class SoundPlaylistCursor
{
    private string _stamp = "";
    private int _next;

    public int Select(IReadOnlyList<SoundClip> clips, string mode, string source = "", double? roll = null)
    {
        if (clips.Count == 0) return -1;
        var stamp = source + mode + JsonSerializer.Serialize(clips);
        if (_stamp != stamp) { _stamp = stamp; _next = 0; }
        if (mode == "sequential")
        {
            var index = _next;
            _next = (_next + 1) % clips.Count;
            return index;
        }
        var total = clips.Sum(c => c.Weight);
        if (total <= 0) return -1;
        var target = Math.Clamp(roll ?? Random.Shared.NextDouble(), 0, Math.BitDecrement(1d)) * total;
        for (var i = 0; i < clips.Count; i++)
        {
            target -= clips[i].Weight;
            if (target < 0) return i;
        }
        return clips.Count - 1;
    }
}
