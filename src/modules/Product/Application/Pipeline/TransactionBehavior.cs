using System.Transactions;
using Mediator;

namespace Product.Application.Pipeline;

public class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(TRequest request, MessageHandlerDelegate<TRequest, TResponse> next, CancellationToken cancellationToken)
    {
        using TransactionScope scope = new(TransactionScopeOption.Required, new TransactionOptions
        {
            IsolationLevel = IsolationLevel.ReadCommitted
        }, TransactionScopeAsyncFlowOption.Enabled);

        var response = await next(request, cancellationToken);
        scope.Complete();
        return response;
    }
}