using Product.Application.Commands;
using Product.Application.Validation;

namespace Product.UnitTest;

public class CreateProductCommandValidatorTests
{
    [Fact]
    public void Price_must_be_positive()
    {
        CreateProductCommandValidator validator = new();
        var result = validator.Validate(new CreateProductCommand("Item", 0));
        Assert.False(result.IsValid);
    }
}