using FluentValidation;

namespace DataSource.Application.Commands;

public class RegisterDataSourceCommandValidator : AbstractValidator<RegisterDataSourceCommand>
{
    public RegisterDataSourceCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ConnectionString).NotEmpty();
    }
}