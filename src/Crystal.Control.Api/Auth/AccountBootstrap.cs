using System.Text.RegularExpressions;
using Crystal.Control.Api.Audit;

namespace Crystal.Control.Api.Auth;

public sealed partial class AccountBootstrap
{
    private readonly AuthStore _authStore;
    private readonly PasswordHasher _passwordHasher;
    private readonly AuditStore _auditStore;
    private readonly ControlApiOptions _options;
    private readonly ILogger<AccountBootstrap> _logger;

    public AccountBootstrap(
        AuthStore authStore,
        PasswordHasher passwordHasher,
        AuditStore auditStore,
        ControlApiOptions options,
        ILogger<AccountBootstrap> logger)
    {
        _authStore = authStore;
        _passwordHasher = passwordHasher;
        _auditStore = auditStore;
        _options = options;
        _logger = logger;
    }

    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await _authStore.AnyAccountsAsync(cancellationToken)) return;

            if (string.IsNullOrWhiteSpace(_options.BootstrapAdminPassword))
            {
                _logger.LogWarning(
                    "No control-plane accounts exist. Set CRYSTAL_CONTROL_BOOTSTRAP_PASSWORD to create the initial administrator.");
                return;
            }

            var username = _options.BootstrapAdminUsername.Trim();
            if (!UsernamePattern().IsMatch(username) ||
                _options.BootstrapAdminPassword.Length is < 12 or > 128)
            {
                _logger.LogWarning(
                    "Control-plane bootstrap account was not created because the username or password is invalid.");
                return;
            }

            var account = await _authStore.CreateAccountAsync(
                username,
                username.ToLowerInvariant(),
                email: null,
                _options.BootstrapAdminDisplayName,
                _passwordHasher.Hash(_options.BootstrapAdminPassword),
                ["platform_admin"],
                cancellationToken);

            await _auditStore.WriteAsync(
                new AuditEventWrite(
                    null,
                    "bootstrap",
                    null,
                    "account.bootstrap",
                    "control_account",
                    account.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null,
                    null,
                    null,
                    "success",
                    "initial_platform_administrator",
                    null,
                    new { account.Username, roles = account.Roles }),
                cancellationToken);

            _logger.LogInformation(
                "Created bootstrap control-plane administrator {Username}.",
                account.Username);
        }
        catch (DuplicateAccountException)
        {
            _logger.LogInformation("Control-plane bootstrap account already exists.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Control-plane bootstrap account creation was skipped.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
