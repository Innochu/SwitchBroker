namespace SwitchBroker
{
    public interface ITicketStore
    {
        Task StoreAsync(SwitchTicket ticket, CancellationToken ct = default);
        Task<SwitchTicket?> ConsumeAsync(string ticketId, CancellationToken ct = default);
    }
}
