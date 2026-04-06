using System.Collections.Concurrent;

namespace SwitchBroker
{
    public class InMemoryTicketStore : ITicketStore
    {
        private readonly ConcurrentDictionary<string, SwitchTicket> _store = new();

        public Task StoreAsync(SwitchTicket ticket, CancellationToken ct = default)
        {
            _store[ticket.TicketId] = ticket;
            return Task.CompletedTask;
        }

        public Task<SwitchTicket?> ConsumeAsync(string ticketId, CancellationToken ct = default)
        {
            // TryRemove is the atomic operation — only one caller wins
            if (!_store.TryRemove(ticketId, out var ticket))
                return Task.FromResult<SwitchTicket?>(null);

            if (!ticket.IsValid) // expired or already consumed
                return Task.FromResult<SwitchTicket?>(null);

            ticket.UsedAt = DateTimeOffset.UtcNow;
            return Task.FromResult<SwitchTicket?>(ticket);
        }
    }
}