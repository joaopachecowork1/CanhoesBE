using System.ComponentModel.DataAnnotations;

namespace Canhoes.Api.Models;

public static class EventContextDefaults
{
    public const string DefaultEventId = "canhoes-do-ano";
    public const string DefaultEventName = "Canhoes do Ano";
}

public static class EventPhaseTypes
{
    public const string Draw = "DRAW";
    public const string Proposals = "PROPOSALS";
    public const string Voting = "VOTING";
    public const string Results = "RESULTS";
}

public static class EventRoles
{
    public const string Admin = "admin";
    public const string User = "user";
}

public sealed class EventEntity
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = EventContextDefaults.DefaultEventId;

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = EventContextDefaults.DefaultEventName;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class EventMemberEntity
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    [MaxLength(64)]
    public string EventId { get; set; } = EventContextDefaults.DefaultEventId;

    [Required]
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(16)]
    public string Role { get; set; } = EventRoles.User;

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class EventPhaseEntity
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    [MaxLength(64)]
    public string EventId { get; set; } = EventContextDefaults.DefaultEventId;

    [Required]
    [MaxLength(32)]
    public string Type { get; set; } = EventPhaseTypes.Proposals;

    public DateTime StartDateUtc { get; set; }

    public DateTime EndDateUtc { get; set; }

    public bool IsActive { get; set; }
}
