using User.Application.Commands;
using User.Application.Validation;

namespace User.UnitTest;

public class CreateUserCommandValidatorTests
{
    [Fact]
    public void Invalid_email_fails_validation()
    {
        CreateUserCommandValidator validator = new();
        var result = validator.Validate(new CreateUserCommand("Name", "not-an-email"));
        Assert.False(result.IsValid);
    }
}