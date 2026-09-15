namespace NovaWallet.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Marker for an entity that is the transactional consistency boundary and sole
/// mutation entry point for everything beneath it (e.g. a Wallet owns its ledger entries).
/// </summary>
public abstract class AggregateRoot : Entity
{
    protected AggregateRoot(Guid id) : base(id) { }

    protected AggregateRoot() { }
}
