namespace SwitchBroker
{

    // ──────────────────────────────────────────────────────────────────────────────
    // Inbound request: sent by the source app when the user clicks "Switch"
    // POST /api/switch/tickets
    // ──────────────────────────────────────────────────────────────────────────────

    public record SwitchRequest(
        /// <summary>"legacy" or "modern"</summary>
        string TargetApp,

        // Identity fields flat at root (per spec)
        string UserId,
        string Email,
        string TenantId,

        /// <summary>Canonical route key, e.g. "sales.orders.detail"</summary>
        string RouteKey,

        /// <summary>Route parameters, e.g. { "id": "SO-10234" }</summary>
        Dictionary<string, string>? RouteParams
    );

    // ──────────────────────────────────────────────────────────────────────────────
    // Inbound request: sent by the target app to consume a ticket
    // POST /api/switch/tickets/{ticketId}/consume
    // ──────────────────────────────────────────────────────────────────────────────

    public record ConsumeRequest(
        /// <summary>Must match the TargetApp stored on the ticket.</summary>
        string ExpectedTargetApp
    );

    // ──────────────────────────────────────────────────────────────────────────────
    // Stored ticket
    // ──────────────────────────────────────────────────────────────────────────────

    public class SwitchTicket
    {
        public string TicketId { get; init; } = Guid.NewGuid().ToString("N");
        public string UserId { get; init; } = default!;
        public string Email { get; init; } = default!;
        public string TenantId { get; init; } = default!;
        public string TargetApp { get; init; } = default!;
        public string RouteKey { get; init; } = default!;
        public Dictionary<string, string> RouteParams { get; init; } = new();
        public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.UtcNow;
        public DateTimeOffset ExpiresAt { get; init; }
        public DateTimeOffset? UsedAt { get; set; }

        public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
        public bool IsConsumed => UsedAt.HasValue;
        public bool IsValid => !IsExpired && !IsConsumed;
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Response models — field names match the spec exactly
    // ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Returned to the source app after issuing a ticket.</summary>
    public record IssueTicketResponse(
        string TicketId,
        DateTimeOffset ExpiresAtUtc,
        /// <summary>Relative redirect path — source app prepends the target host.</summary>
        string RedirectPath
    );

    /// <summary>Returned to the target app after ticket consumption.</summary>
    public record ConsumeTicketResponse(
        string UserId,
        string Email,
        string TenantId,
        string RouteKey,
        Dictionary<string, string> RouteParams,
        /// <summary>Resolved URL in the target app's native routing format.</summary>
        string? NativeRoute,
        /// <summary>True when the exact route was unavailable and a fallback was used.</summary>
        bool FallbackUsed
    );

    public record ErrorResponse(string Error, string? Detail = null);

}
