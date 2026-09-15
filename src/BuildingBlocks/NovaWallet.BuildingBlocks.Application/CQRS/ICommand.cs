using MediatR;
using NovaWallet.BuildingBlocks.Domain.Results;

namespace NovaWallet.BuildingBlocks.Application.CQRS;

public interface ICommand : IRequest<Result>;

public interface ICommand<TResponse> : IRequest<Result<TResponse>>;
