using System.Transactions;
using Mediator;
using Product.Application.Abstractions;
using Product.Application.Commands;

namespace ModularMonolith.IntegrationTest;

public class TransactionBehaviorTests
{
    [Theory]
    [InlineData("User", false)]
    [InlineData("User", true)]
    [InlineData("Product", false)]
    [InlineData("Product", true)]
    [InlineData("Order", false)]
    [InlineData("Order", true)]
    [InlineData("DataSource", false)]
    [InlineData("DataSource", true)]
    public async Task Pipeline_forwards_message_and_token_and_completes_only_on_success(string module, bool fail)
    {
        IPipelineBehavior<CreateProductCommand, ProductDto> behavior = module switch
        {
            "User" => new User.Application.Pipeline.TransactionBehavior<CreateProductCommand, ProductDto>(),
            "Product" => new Product.Application.Pipeline.TransactionBehavior<CreateProductCommand, ProductDto>(),
            "Order" => new Order.Application.Pipeline.TransactionBehavior<CreateProductCommand, ProductDto>(),
            _ => new DataSource.Application.Pipeline.TransactionBehavior<CreateProductCommand, ProductDto>()
        };
        CreateProductCommand request = new("Gadget", 10m);
        ProductDto response = new(Guid.NewGuid(), request.Name, request.Price);
        using CancellationTokenSource cancellation = new();
        TransactionStatus? completionStatus = null;

        async ValueTask<ProductDto> Next(CreateProductCommand message, CancellationToken token)
        {
            Assert.Same(request, message);
            Assert.Equal(cancellation.Token, token);
            var transaction = Assert.IsType<Transaction>(Transaction.Current);
            transaction.TransactionCompleted += (_, args) =>
                completionStatus = args.Transaction!.TransactionInformation.Status;
            await Task.Yield();
            Assert.Same(transaction, Transaction.Current);
            if (fail) throw new InvalidOperationException("handler failed");
            return response;
        }

        if (fail)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                behavior.Handle(request, Next, cancellation.Token).AsTask());
        }
        else
        {
            Assert.Same(response, await behavior.Handle(request, Next, cancellation.Token));
        }

        Assert.Equal(fail ? TransactionStatus.Aborted : TransactionStatus.Committed, completionStatus);
        Assert.Null(Transaction.Current);
    }
}