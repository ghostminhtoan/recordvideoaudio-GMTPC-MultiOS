using System;

namespace RecordVideoAudio.GMTPC.Models;

public enum AudioDeviceFlow
{
    Render,  // Speaker / Headphone
    Capture  // Microphone / Line In
}

public class AudioDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public AudioDeviceFlow Flow { get; set; }

    public override string ToString() => Name;

    public override bool Equals(object? obj)
    {
        return obj is AudioDeviceInfo other &&
               string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase) &&
               Flow == other.Flow;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id?.ToLowerInvariant(), Flow);
    }
}
