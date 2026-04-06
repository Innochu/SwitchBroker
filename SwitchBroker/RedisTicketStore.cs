using StackExchange.Redis;
using System.Text.Json;

namespace SwitchBroker
{

    public class RedisTicketStore : ITicketStore
    {
        private readonly IDatabase _db;
        private const string ConsumeScript = @"
local val = redis.call('GET', KEYS[1])
if val then redis.call('DEL', KEYS[1]) end
return val";

        public RedisTicketStore(IConnectionMultiplexer redis)
        => _db = redis.GetDatabase();

        public async Task StoreAsync(SwitchTicket ticket, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(ticket);
            var ttl = ticket.ExpiresAt - DateTimeOffset.UtcNow;
            await _db.StringSetAsync(ticket.TicketId, json, ttl);
        }

        public async Task<SwitchTicket?> ConsumeAsync(string ticketId, CancellationToken ct = default)
        {

            var result = (string?)await _db.ScriptEvaluateAsync(
    ConsumeScript, new RedisKey[] { ticketId });

            if (result is null) return null;

            var ticket = JsonSerializer.Deserialize<SwitchTicket>(result);
            if (ticket is null || !ticket.IsValid) return null;

            ticket.UsedAt = DateTimeOffset.UtcNow;
            return ticket;
        }
    }
}