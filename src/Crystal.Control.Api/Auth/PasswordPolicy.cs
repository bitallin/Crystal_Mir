namespace Crystal.Control.Api.Auth;

public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    public static void Validate(
        string? password,
        string field,
        IDictionary<string, string[]> errors)
    {
        if (password is null || password.Length < MinLength || password.Length > MaxLength)
        {
            errors[field] = [$"password must be between {MinLength} and {MaxLength} characters."];
        }
    }
}
