using System;

namespace Templates.Definitions
{
    [Flags]
    public enum TargetAlliance
    {
        None = 0,
        Self = 1 << 0,
        Ally = 1 << 1,
        Neutral = 1 << 2,
        Enemy = 1 << 3,
    }
}
