using System.ComponentModel.DataAnnotations;

namespace Auth.Application;

public sealed record RegisterRequest(string Name, string Email, string Password)
{
    public void Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100)
            errors["name"] = ["請輸入 1 至 100 字元的姓名。"];
        RequestValidation.Email(Email, errors);
        RequestValidation.Password(Password, errors);
        RequestValidation.ThrowIfInvalid(errors);
    }
}

public sealed record LoginRequest(string Email, string Password);
public sealed record EmailRequest(string Email);
public sealed record ConfirmEmailRequest(Guid UserId, string Token);
public sealed record ResetPasswordRequest(Guid UserId, string Token, string Password);
public sealed record RefreshRequest(Guid RequestId);
public sealed record SwitchTenantRequest(string TenantId, Guid RequestId);
public sealed record UserProfile(Guid Id, string Email, string Name);
public sealed record TenantProfile(string Id, string Name);
public sealed record SessionProfile(UserProfile User, TenantProfile CurrentTenant,
    IReadOnlyList<TenantProfile> Tenants, bool IsPlatformAdmin, DateTimeOffset AccessExpiresAt,
    DateTimeOffset SessionExpiresAt);

// This value stays inside the server; endpoints only serialize Profile and set HttpOnly cookies.
public sealed record SessionTokens(string AccessToken, string RefreshToken, SessionProfile Profile);

public sealed class AuthProblem(int status, string code, string message,
    IDictionary<string, string[]>? errors = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public IDictionary<string, string[]>? Errors { get; } = errors;
    internal bool CommitState { get; init; }

    public static AuthProblem InvalidSession() => new(401, "session_invalid", "登入已失效，請重新登入。");
}

internal static class RequestValidation
{
    public static void Email(string? email, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !new EmailAddressAttribute().IsValid(email))
            errors["email"] = ["請輸入有效的 Email。"];
    }

    public static void Password(string? password, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 15 || password.Length > 128)
            errors["password"] = ["密碼須包含 15 至 128 個字元。"];
    }

    public static void ThrowIfInvalid(IDictionary<string, string[]> errors)
    {
        if (errors.Count > 0)
            throw new AuthProblem(400, "validation_failed", "請檢查輸入欄位。", errors);
    }

    public static void Operation(Guid requestId)
    {
        if (requestId == Guid.Empty)
            throw new AuthProblem(400, "validation_failed", "缺少有效的請求識別碼。");
    }
}