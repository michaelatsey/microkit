using System;
using System.Collections.Generic;
using System.Text;

namespace MicroKit.Caching;

public class CacheOptions
{
    public TimeSpan? Duration { get; set; }
    public bool BypassCache { get; set; } = false;
    public bool SlidingExpiration { get; set; } = false;

    public CacheOptions(TimeSpan? duration = null, bool bypassCache = false, bool slidingExpiration = false)
    {
        Duration = duration;
        BypassCache = bypassCache;
        SlidingExpiration = slidingExpiration;
    }
}
