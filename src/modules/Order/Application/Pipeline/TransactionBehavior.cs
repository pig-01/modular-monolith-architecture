using Mediator;
using System.Transactions;

namespace Order.Application.Pipeline;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(TRequest request, MessageHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken)
    {
        using var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions
        {
            IsolationLevel = IsolationLevel.ReadCommitted
        }, TransactionScopeAsyncFlowOption.Enabled);

        var response = await next(request, cancellationToken);
        scope.Complete();
        return response;
    }
}
