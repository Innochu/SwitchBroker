using System.Text.Json;

namespace SwitchBroker
{
    public record RouteEntry(
string? Legacy,
string? Modern,
string Status,
string? FallbackRouteKey
);

    public class RouteRegistry
    {
        private Dictionary<string, RouteEntry> _routes = new();

        public static RouteRegistry LoadFromFile(string path)
        {
            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, RouteEntry>>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return new RouteRegistry { _routes = dict ?? new() };
        }
        public void TryResolve(
string routeKey, string targetApp,
Dictionary<string, string> routeParams,
out string? nativeRoute, out bool fallbackUsed)
        {
            fallbackUsed = false;
            nativeRoute = null;

            if (!_routes.TryGetValue(routeKey, out var entry))
            {
                fallbackUsed = true;
                ResolveDefault(targetApp, out nativeRoute);
                return;
            }

            var template = targetApp == "modern" ? entry.Modern : entry.Legacy;

            if (template is null)
            {
                fallbackUsed = true;
                var fbKey = entry.FallbackRouteKey ?? "dashboard.home";
                TryResolve(fbKey, targetApp, new(), out nativeRoute, out _);
                return;
            }

            nativeRoute = routeParams.Aggregate(template,
            (s, kv) => s.Replace($"{{{kv.Key}}}", kv.Value));
        }

        private void ResolveDefault(string targetApp, out string? route)
        {
            _routes.TryGetValue("dashboard.home", out var fb);
            route = targetApp == "modern" ? fb?.Modern : fb?.Legacy;
        }
    }
}