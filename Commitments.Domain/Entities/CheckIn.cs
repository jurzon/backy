namespace Commitments.Domain.Entities;

public class CheckIn
{
    public Guid Id { get; set; }
    public Guid CommitmentId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Note { get; set; }
    public string? PhotoUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
