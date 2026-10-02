using Order.Application.Commands;
using Order.Application.Validation;

namespace Order.UnitTest;

public class PlaceOrderCommandValidatorTests
{
    [Fact]
    public void Empty_items_fail_validation()
    {
        PlaceOrderCommandValidator validator = new();
        var result = validator.Validate(new PlaceOrderCommand(Guid.NewGuid(), Array.Empty<PlaceOrderItem>()));
        Assert.False(result.IsValid);
    }
}