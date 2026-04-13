namespace SwitchBroker
{

    public class BrokerOptions
    {
        public const string SectionName = "Broker";

        /// <summary>Lifetime of a switch ticket in seconds. Default: 45.</summary>
        public int TicketLifetimeSeconds { get; set; } = 45;

        /// <summary>Shared secret expected in the X-Broker-Secret header on POST /api/switch/tickets (issue).</summary>
        public string IssueSecret { get; set; } = string.Empty;

        /// <summary>
        /// Per-app shared secrets expected in the X-App-Key header on POST /api/switch/tickets/{id}/consume.
        /// Key = app name (matches X-App-Name header, e.g. "legacy", "modern").
        /// Value = secret shared only with that app.
        /// </summary>
        public Dictionary<string, string> AppKeys { get; set; } = new();

        /// <summary>Known hostnames for each runtime. Used for open-redirect protection.</summary>
        public Dictionary<string, string> AllowedTargetHosts { get; set; } = new()
        {
            ["legacy"] = "https://app.liquidaccounts.com/LiquidDelta",
            ["modern"] = "https://beta.liquidaccounts.com"
        };

        /// <summary>Redis connection string. When empty the broker falls back to in-memory storage.</summary>
        public string? RedisConnectionString { get; set; }
    }

}
