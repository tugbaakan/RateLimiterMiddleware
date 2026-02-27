using StackExchange.Redis;
using System.Net.Http.Headers;
using System.Text;
using System.Numerics;

namespace RateLimiterMiddleware.Models
{   

public class SlidingWindowRateLimiter
{
 
    private readonly IDatabase _db;
    private readonly IConfiguration _config;
    private readonly RequestDelegate _next;

    public SlidingWindowRateLimiter(RequestDelegate next, IConnectionMultiplexer muxer, IConfiguration config)
    {
        _db = muxer.GetDatabase();
        _config = config;
        _next = next;
    }

    private const string SlidingRateLimiterScript = @"
        local current_time = { tonumber(ARGV[1]), tonumber(ARGV[2]) }
        local num_windows = tonumber(ARGV[3])
        for i=2, num_windows*2, 2 do
            local window = ARGV[i+2]
            local max_requests = ARGV[i+3]
            local curr_key = KEYS[i/2]
            local trim_time = current_time[1] - tonumber(window)
            redis.call('ZREMRANGEBYSCORE', curr_key, 0, trim_time)
            local request_count = redis.call('ZCARD', curr_key)
            if request_count >= tonumber(max_requests) then
                return 1
            end
        end
        for i=2, num_windows*2, 2 do
            local curr_key = KEYS[i/2]
            local window = ARGV[i+2]
            redis.call('ZADD', curr_key, current_time[1], current_time[1] .. current_time[2])
            redis.call('EXPIRE', curr_key, window)
        end
        return 0
        ";


    private static string GetApiKey(HttpContext context)
    {
        var encoded = string.Empty;
        var auth = context.Request.Headers["Authorization"];
        if (!string.IsNullOrEmpty(auth)) 
            encoded = AuthenticationHeaderValue.Parse(auth).Parameter;
        if (string.IsNullOrEmpty(encoded)) 
            return encoded;
        return Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split(':')[0];
    }

    public IEnumerable<RateLimitRule> GetApplicableRules(HttpContext context)
    {
        var limits = _config.GetSection("RedisRateLimits").Get<RateLimitRule[]>();
        var applicableRules = limits
            .Where(x => x.MatchPath(context.Request.Path))
            .OrderBy(x => x.MaxRequests)
            .GroupBy(x => new{x.PathKey, x.WindowSeconds})
            .Select(x=>x.First());
        return applicableRules;
    }
    
    private async Task<bool> IsLimited(IEnumerable<RateLimitRule> rules, string apiKey)
    {
        var keys = rules.Select(x => new RedisKey($"{x.PathKey}:{{{apiKey}}}:{x.WindowSeconds}")).ToArray();
        var now = DateTimeOffset.UtcNow;
        var unixSeconds = now.ToUnixTimeSeconds();
        var microseconds = (long)((now.UtcDateTime.Ticks % 10_000_000) / 10);
        var args = new List<RedisValue>
        {
            unixSeconds,
            microseconds,
            rules.Count()
        };
        foreach (var rule in rules)
        {
            args.Add(rule.WindowSeconds);
            args.Add(rule.MaxRequests);
        }
        return (int) await _db.ScriptEvaluateAsync(SlidingRateLimiterScript, keys, args.ToArray()) == 1;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var apiKey = GetApiKey(httpContext);
        if (string.IsNullOrEmpty(apiKey))
        {
            httpContext.Response.StatusCode = 401;
            return;
        }
        var applicableRules = GetApplicableRules(httpContext);
        var limited = await IsLimited(applicableRules, apiKey);
        if (limited)
        {
            httpContext.Response.StatusCode = 429;
            return;
        }
        await _next(httpContext);
    }

}
}