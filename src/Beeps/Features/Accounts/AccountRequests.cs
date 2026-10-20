namespace Beeps.Features.Accounts;

public sealed record RegisterRequest(string? Email, string? Username, string? Password, string? InviteCode, string? CaptchaToken);

/// <param name="Login">Email address or username.</param>
/// <param name="CaptchaToken">Turnstile's, which only an account past its wrong passwords asks for.</param>
public sealed record LoginRequest(string? Login, string? Password, string? CaptchaToken = null);

/// <summary>
/// Signed in as <see cref="Account"/>; or, with none, on to the code an account past its wrong passwords sends to its address
/// (if the password was right: the answer does not say).
/// </summary>
/// <param name="CodeStamp">
/// With no account: what the code step must bring back for its guesses to count (AccountService.LoginWithCodeAsync),
/// kept where only this browser has it. It names the code the right password asked for, and nothing for a wrong one.
/// </param>
public sealed record LoginOutcome(AccountState? Account, string? CodeStamp = null)
{
    public static LoginOutcome CodeSent(string codeStamp) => new(null, codeStamp);
}

public sealed record PasswordResetRequest(string? Email, string? Code, string? NewPassword);
