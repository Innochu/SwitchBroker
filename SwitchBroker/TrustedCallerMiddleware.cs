using Microsoft.Extensions.Options;

namespace SwitchBroker
{

    /// <summary>
    /// Authenticates callers on the two broker endpoints:
    ///
    /// POST /api/switch/tickets (issue)
    ///   → requires X-Broker-Secret matching config IssueSecret.
    ///   → called server-to-server by the source app.
    ///
    /// POST /api/switch/tickets/{id}/consume
    ///   → requires X-App-Name + X-App-Key matching the AppKeys dictionary in config.
    ///   → called server-to-server by the target app.
    ///   → each app has its own key so a rogue app cannot consume tickets meant for another.
/// </summary>
public class TrustedCallerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly BrokerOptions _options;

    public TrustedCallerMiddleware(RequestDelegate next, IOptions<BrokerOptions> options)
        {
            _next = next;
            _options = options.Value;
        }

        public async Task InvokeAsync(HttpContext ctx)
        {
            var path = ctx.Request.Path.Value ?? string.Empty;

            // ── Issue endpoint ────────────────────────────────────────────────────
            if (ctx.Request.Method == "POST" &&
                path.Equals("/api/switch/tickets", StringComparison.OrdinalIgnoreCase))
            {
                if (!ctx.Request.Headers.TryGetValue("X-Broker-Secret", out var secret) ||
                    secret != _options.IssueSecret)
                {
                    await Reject(ctx, "Missing or invalid X-Broker-Secret.");
                    return;
                }
            }

            // ── Consume endpoint ──────────────────────────────────────────────────
            else if (ctx.Request.Method == "POST" &&
                     path.Contains("/consume", StringComparison.OrdinalIgnoreCase))
            {
                if (!ctx.Request.Headers.TryGetValue("X-App-Name", out var appName) ||
                    !ctx.Request.Headers.TryGetValue("X-App-Key", out var appKey))
                {
                    await Reject(ctx, "X-App-Name and X-App-Key headers are required.");
                    return;
                }

                var name = appName.ToString().ToLowerInvariant();

                if (!_options.AppKeys.TryGetValue(name, out var expectedKey) || appKey != expectedKey)
                {
                    await Reject(ctx, "Unknown app or invalid X-App-Key.");
                    return;
                }

                // Expose the verified app name to the endpoint handler
                ctx.Items["VerifiedAppName"] = name;
            }

            await _next(ctx);
        }

        private static Task Reject(HttpContext ctx, string detail)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return ctx.Response.WriteAsJsonAsync(new ErrorResponse("Unauthorized", detail));
        }
    }
}