using System.ComponentModel.DataAnnotations;

namespace GeoConquest.Models.ViewModels;

// Small, validated input models for each move. The engine re-checks every rule on the server.

public sealed class NewGameInput
{
    [Required(ErrorMessage = "Pick the country you want to command.")]
    [StringLength(3, MinimumLength = 2)]
    public string CountryCode { get; set; } = string.Empty;

    [Range(1, 9, ErrorMessage = "Choose between 1 and 9 NPC opponents.")]
    public int NpcCount { get; set; } = 9;
}

public sealed class ReinforceInput
{
    [Required(ErrorMessage = "Choose a country to reinforce.")]
    [StringLength(3, MinimumLength = 2)]
    public string CountryCode { get; set; } = string.Empty;

    [Range(1, 1000, ErrorMessage = "Place at least 1 soldier.")]
    public int Amount { get; set; } = 1;
}

public sealed class TransferInput
{
    [Required(ErrorMessage = "Choose where to move soldiers from.")]
    [StringLength(3, MinimumLength = 2)]
    public string FromCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose where to move soldiers to.")]
    [StringLength(3, MinimumLength = 2)]
    public string ToCode { get; set; } = string.Empty;

    [Range(1, 1000, ErrorMessage = "Move at least 1 soldier.")]
    public int Amount { get; set; } = 1;
}

public sealed class AttackInput
{
    [Required(ErrorMessage = "Choose the country to attack from.")]
    [StringLength(3, MinimumLength = 2)]
    public string FromCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a target.")]
    [StringLength(3, MinimumLength = 2)]
    public string ToCode { get; set; } = string.Empty;
}
