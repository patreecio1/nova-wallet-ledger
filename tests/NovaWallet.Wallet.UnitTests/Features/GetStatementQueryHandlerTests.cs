using FluentAssertions;
using NovaWallet.BuildingBlocks.Application.Paging;
using NovaWallet.Wallet.Application.Abstractions;
using NovaWallet.Wallet.Application.Features.GetStatement;
using NovaWallet.Wallet.Domain;
using NSubstitute;

namespace NovaWallet.Wallet.UnitTests.Features;

public class GetStatementQueryHandlerTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly IWalletRepository _repository = Substitute.For<IWalletRepository>();
    private readonly GetStatementQueryHandler _handler;

    public GetStatementQueryHandlerTests() => _handler = new GetStatementQueryHandler(_repository);

    [Fact]
    public async Task Owner_can_read_their_own_statement_newest_first()
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        _repository.GetByIdAsync(wallet.Id, Arg.Any<CancellationToken>()).Returns(wallet);

        var entry = LedgerEntry.ForCredit(wallet.Id, 500_00, 500_00, Guid.NewGuid(), "seed", UtcNow);
        _repository
            .GetStatementAsync(wallet.Id, Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<LedgerEntry>([entry], Page: 1, PageSize: 20, TotalCount: 1));

        var query = new GetStatementQuery(wallet.Id, wallet.CustomerId, CallerRole.Customer, Page: null, PageSize: null);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items.Should().ContainSingle(i => i.AmountKobo == 500_00 && i.Type == "Credit");
    }

    [Fact]
    public async Task A_different_customer_cannot_read_someone_elses_statement()
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        _repository.GetByIdAsync(wallet.Id, Arg.Any<CancellationToken>()).Returns(wallet);

        var query = new GetStatementQuery(wallet.Id, Guid.NewGuid(), CallerRole.Customer, Page: null, PageSize: null);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(WalletErrors.Forbidden);
    }

    [Fact]
    public async Task Fails_when_the_wallet_does_not_exist()
    {
        var missingWalletId = Guid.NewGuid();
        _repository.GetByIdAsync(missingWalletId, Arg.Any<CancellationToken>()).Returns((WalletAccount?)null);

        var query = new GetStatementQuery(missingWalletId, Guid.NewGuid(), CallerRole.Customer, Page: null, PageSize: null);
        var result = await _handler.Handle(query, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(WalletErrors.NotFound(missingWalletId).Code);
    }

    [Fact]
    public async Task Passes_caller_supplied_paging_through_to_the_repository_clamped()
    {
        var wallet = WalletAccount.Create(Guid.NewGuid(), UtcNow);
        _repository.GetByIdAsync(wallet.Id, Arg.Any<CancellationToken>()).Returns(wallet);
        _repository
            .GetStatementAsync(wallet.Id, Arg.Any<PageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<LedgerEntry>([], Page: 3, PageSize: 100, TotalCount: 0));

        // Requests a page size far above PageRequest.MaxPageSize -- the handler must clamp it
        // via PageRequest.From rather than pass the raw caller-supplied value straight through.
        var query = new GetStatementQuery(wallet.Id, wallet.CustomerId, CallerRole.Customer, Page: 3, PageSize: 10_000);
        await _handler.Handle(query, CancellationToken.None);

        await _repository.Received(1).GetStatementAsync(
            wallet.Id,
            Arg.Is<PageRequest>(p => p.Page == 3 && p.PageSize == PageRequest.MaxPageSize),
            Arg.Any<CancellationToken>());
    }
}
