using MediatR;
using Microsoft.Extensions.Logging;

namespace NovaWallet.BuildingBlocks.Application.Behaviours;

public sealed class LoggingBehaviour<TRequest, TResponse>(ILogger<LoggingBehaviour<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;

        logger.LogInformation("Handling {RequestName}", requestName);
        var response = await next();
        logger.LogInformation("Handled {RequestName}", requestName);

        return response;
    }
}
