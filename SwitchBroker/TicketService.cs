using Microsoft.Extensions.Options;

namespace SwitchBroker
{

    public interface ITicketService
    {
        Task<IssueTicketResponse> IssueAsync(SwitchRequest request, CancellationToken ct = default);

        /// <summary>
        /// Validates and atomically consumes a ticket.
        /// Returns null if the ticket is missing, expired, already used, or the
        /// expectedTargetApp does not match what was stored.
        /// </summary>
        Task<ConsumeTicketResponse?> ConsumeAsync(
            string ticketId,
            string expectedTargetApp,
            CancellationToken ct = default);
    }

    public class TicketService : ITicketService
    {
        private readonly ITicketStore _store;
        private readonly RouteRegistry _registry;
        private readonly BrokerOptions _options;
        private readonly ILogger<TicketService> _log;

        public TicketService(
            ITicketStore store,
            RouteRegistry registry,
            IOptions<BrokerOptions> options,
            ILogger<TicketService> log)
        {
            _store = store;
            _registry = registry;
            _options = options.Value;
            _log = log;
        }

        // ── Issue ─────────────────────────────────────────────────────────────────

        public async Task<IssueTicketResponse> IssueAsync(SwitchRequest req, CancellationToken ct = default)
        {
            ValidateTargetApp(req.TargetApp);

            var ticket = new SwitchTicket
            {
                TicketId = Guid.NewGuid().ToString("N"),
                UserId = req.UserId,
                Email = req.Email,
                TenantId = req.TenantId,
                TargetApp = req.TargetApp.ToLowerInvariant(),
                RouteKey = req.RouteKey,
                RouteParams = req.RouteParams ?? new(),
                IssuedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_options.TicketLifetimeSeconds)
            };

            await _store.StoreAsync(ticket, ct);

            string redirectPath;
            if (string.Equals(ticket.TargetApp, "legacy", StringComparison.Ordinal))
            {
                // Legacy app expects the ASPX-style path
                redirectPath = $"/Switch/SwitchConsume.aspx?ticket={ticket.TicketId}";
            }
            else
            {
                redirectPath = $"/switch/consume?ticket={ticket.TicketId}";
            }

            _log.LogInformation(
                "[SwitchBroker] ticket_issued ticketId={TicketId} user={UserId} tenant={TenantId} target={TargetApp} route={RouteKey}",
                ticket.TicketId, ticket.UserId, ticket.TenantId, ticket.TargetApp, ticket.RouteKey);

            return new IssueTicketResponse(ticket.TicketId, ticket.ExpiresAt, redirectPath);
        }

        // ── Consume ───────────────────────────────────────────────────────────────

        public async Task<ConsumeTicketResponse?> ConsumeAsync(
            string ticketId,
            string expectedTargetApp,
            CancellationToken ct = default)
        {
            var ticket = await _store.ConsumeAsync(ticketId, ct);

            if (ticket is null)
            {
                _log.LogWarning(
                    "[SwitchBroker] ticket_rejected reason=not_found_expired_or_used ticketId={Id}", ticketId);
                return null;
            }

            // Verify the consuming app is the intended target
            if (!string.Equals(ticket.TargetApp, expectedTargetApp.ToLowerInvariant(), StringComparison.Ordinal))
            {
                _log.LogWarning(
                    "[SwitchBroker] ticket_rejected reason=target_mismatch ticketId={Id} expected={Expected} got={Got}",
                    ticketId, ticket.TargetApp, expectedTargetApp);
                return null;
            }

            // Resolve the route in the target runtime
            _registry.TryResolve(
                ticket.RouteKey,
                ticket.TargetApp,
                ticket.RouteParams,
                out var nativeRoute,
                out var fallbackUsed);

            _log.LogInformation(
                "[SwitchBroker] ticket_consumed ticketId={TicketId} user={UserId} tenant={TenantId} target={TargetApp} route={RouteKey} fallback={Fallback}",
                ticket.TicketId, ticket.UserId, ticket.TenantId, ticket.TargetApp, ticket.RouteKey, fallbackUsed);

            return new ConsumeTicketResponse(
                ticket.UserId,
                ticket.Email,
                ticket.TenantId,
                ticket.RouteKey,
                ticket.RouteParams,
                nativeRoute,
                fallbackUsed);
        }

        // ── Validation ────────────────────────────────────────────────────────────

        private void ValidateTargetApp(string targetApp)
        {
            if (!_options.AllowedTargetHosts.ContainsKey(targetApp.ToLowerInvariant()))
                throw new ArgumentException($"Unknown target app '{targetApp}'.");
        }
    }
}