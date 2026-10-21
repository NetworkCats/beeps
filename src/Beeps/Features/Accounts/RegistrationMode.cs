namespace Beeps.Features.Accounts;

/// <summary>Who may sign up. Stored in <c>site_settings</c>, so every instance agrees.</summary>
public enum RegistrationMode
{
    Open,
    InviteOnly,
    Closed,
}
