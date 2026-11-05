using System.ComponentModel.DataAnnotations;

namespace GeoConquest.Models.ViewModels;

public sealed class RegisterViewModel
{
    [Required]
    [RegularExpression("^[A-Za-z0-9_]{3,20}$", ErrorMessage = "3–20 letters, numbers or underscores.")]
    [Display(Name = "Username")]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(30, MinimumLength = 2)]
    [Display(Name = "Commander name")]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "At least 8 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Include at least one letter and one number.")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords don't match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>POPIA consent. Must be ticked; the service checks it again.</summary>
    [MustBeTrue(ErrorMessage = "Please accept the privacy notice to create an account.")]
    [Display(Name = "I consent to the processing of my personal information")]
    public bool PopiaConsent { get; set; }
}

public sealed class LoginViewModel
{
    [Required]
    [Display(Name = "Username or email")]
    public string Login { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Enter your email or username.")]
    [StringLength(254)]
    [Display(Name = "Email or username")]
    public string Login { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required]
    [RegularExpression("^[a-f0-9]{32}$")]
    public string Uid { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [RegularExpression("^[A-Za-z0-9_-]+$")]
    public string Token { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "At least 8 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Include at least one letter and one number.")]
    [Display(Name = "New password")]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords don't match.")]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ConsentViewModel
{
    [MustBeTrue(ErrorMessage = "Tick the box to accept, or delete your account below.")]
    [Display(Name = "I consent to the processing of my personal information")]
    public bool Accept { get; set; }

    public string? ReturnUrl { get; set; }
}

/// <summary>Everything we hold about the signed-in user (POPIA right of access).</summary>
public sealed class MyDataViewModel
{
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? LastLoginAtUtc { get; init; }
    public DateTime? ConsentGivenAtUtc { get; init; }
    public string? ConsentVersion { get; init; }
    public DateTime? PasswordChangedAtUtc { get; init; }
    public int Games { get; init; }

    public DeleteMyAccountViewModel Delete { get; set; } = new();
}

public sealed class DeleteMyAccountViewModel
{
    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    [StringLength(100)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Type DELETE to confirm.")]
    [RegularExpression("^DELETE$", ErrorMessage = "Type DELETE in capitals to confirm.")]
    [Display(Name = "Type DELETE to confirm")]
    public string Confirm { get; set; } = string.Empty;
}

/// <summary>
/// A checkbox that must be ticked (consent). Checked on the server. [Range] is not used because jQuery's
/// client-side range check treats "true" as a number and wrongly blocks ticked boxes.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class MustBeTrueAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is true;
}

/// <summary>Account security page: change password and change email, each confirmed with the current password.</summary>
public sealed class SecurityViewModel
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime? PasswordChangedAtUtc { get; set; }

    public ChangePasswordViewModel Password { get; set; } = new();
    public ChangeEmailViewModel EmailChange { get; set; } = new();
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Enter your current password.")]
    [DataType(DataType.Password)]
    [StringLength(100)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a new password.")]
    [DataType(DataType.Password)]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "At least 8 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Include at least one letter and one number.")]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Type the new password again.")]
    [DataType(DataType.Password)]
    [Compare(nameof(NewPassword), ErrorMessage = "The new passwords don't match.")]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ChangeEmailViewModel
{
    [Required(ErrorMessage = "Enter the new email address.")]
    [EmailAddress]
    [StringLength(254)]
    [Display(Name = "New email")]
    public string NewEmail { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your current password.")]
    [DataType(DataType.Password)]
    [StringLength(100)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;
}
