namespace Beeps.Features.Accounts;

public sealed record RegisterRequest(string? Email, string? Username, string? Password, string? InviteCode, string? CaptchaToken);

public sealed record PasswordResetRequest(string? Email, string? Code, string? NewPassword);
