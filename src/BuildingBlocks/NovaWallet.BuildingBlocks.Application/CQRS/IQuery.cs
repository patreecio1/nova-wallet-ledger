using MediatR;
using NovaWallet.BuildingBlocks.Domain.Results;

namespace NovaWallet.BuildingBlocks.Application.CQRS;

public interface IQuery<TResponse> : IRequest<Result<TResponse>>;

public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;
