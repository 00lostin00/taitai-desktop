namespace Taitai.Core;

public sealed class SpriteTimeline
{
    readonly int[] ends;
    public int DurationMilliseconds { get; }

    public SpriteTimeline(IEnumerable<int> durations)
    {
        var values = durations.ToArray();
        if (values.Length == 0 || values.Any(value => value <= 0))
            throw new ArgumentException("Frame durations must be positive.", nameof(durations));
        ends = new int[values.Length];
        for (var i = 0; i < values.Length; i++)
            ends[i] = checked(values[i] + (i == 0 ? 0 : ends[i - 1]));
        DurationMilliseconds = ends[^1];
    }

    public int FrameAt(TimeSpan elapsed)
    {
        var position = Math.Max(0, elapsed.TotalMilliseconds) % DurationMilliseconds;
        for (var i = 0; i < ends.Length; i++)
            if (position < ends[i]) return i;
        return 0;
    }
}

public sealed class PointerAttention
{
    bool initialized;
    int lastX, lastY;
    TimeSpan until;

    public bool Update(int x, int y, TimeSpan now, bool eligible)
    {
        if (!initialized)
        {
            initialized = true; lastX = x; lastY = y;
            return false;
        }
        var dx = (long)x - lastX; var dy = (long)y - lastY;
        if (dx * dx + dy * dy >= 16)
        {
            lastX = x; lastY = y;
            until = eligible ? now + TimeSpan.FromMilliseconds(600) : TimeSpan.Zero;
        }
        if (!eligible) until = TimeSpan.Zero;
        return eligible && now < until;
    }
}

public static class FloatingPolicy
{
    public static bool IsTopmost(string mode, PetActivity activity, bool speaking) => mode switch
    {
        "normal" => false,
        "follow" => speaking || activity is PetActivity.Thinking or PetActivity.Tool or PetActivity.Waiting or PetActivity.Complete or PetActivity.Failed,
        _ => true
    };
}
