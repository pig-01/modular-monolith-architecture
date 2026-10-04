using Auth.Application;

namespace Auth.Tests;

public class AuthValidationTests
{
    [Fact]
    public void Registration_requires_a_long_password_before_contacting_storage()
    {
        var exception = Assert.Throws<AuthProblem>(() =>
            new RegisterRequest("測試使用者", "user@example.com", "short").Validate());

        Assert.Equal("validation_failed", exception.Code);
        Assert.Contains("password", exception.Errors!.Keys);
    }
}