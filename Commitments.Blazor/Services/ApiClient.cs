using System.Net.Http.Json;

namespace CommitmentsBlazor.Services;

public class ApiClient
{
    private readonly HttpClient _http;
    public ApiClient(HttpClient http) { _http = http; }

    public record CommitmentSummary(Guid Id, string Goal, string Currency, long StakeAmountMinor, DateTime DeadlineUtc, string Status, double ProgressPercent, string RiskBadge);
    public record CheckIn(Guid Id, DateTime OccurredAtUtc, string? Note, string? PhotoUrl);

    public async Task<IReadOnlyList<CommitmentSummary>> GetCommitmentsAsync(Guid userId, CancellationToken ct = default)
    {
        var url = $"commitments?userId={userId}";
        var doc = await _http.GetFromJsonAsync<ResponseWrapper>(url, ct) ?? new ResponseWrapper();
        return doc.items ?? new List<CommitmentSummary>();
    }

    public Task<CommitmentSummary?> GetCommitmentAsync(Guid id, CancellationToken ct = default)
        => _http.GetFromJsonAsync<CommitmentSummary>($"commitments/{id}", ct);

    public Task<List<CheckIn>?> GetCheckInsAsync(Guid commitmentId, CancellationToken ct = default)
        => _http.GetFromJsonAsync<List<CheckIn>>($"commitments/{commitmentId}/checkins", ct);

    public Task<HttpResponseMessage> CancelAsync(Guid id) => _http.PostAsync($"commitments/{id}/actions/cancel", null);
    public Task<HttpResponseMessage> CompleteAsync(Guid id) => _http.PostAsync($"commitments/{id}/actions/complete", null);
    public Task<HttpResponseMessage> FailAsync(Guid id) => _http.PostAsync($"commitments/{id}/actions/fail", null);
    public Task<HttpResponseMessage> DeleteAsync(Guid id) => _http.PostAsync($"commitments/{id}/actions/delete", null);

    public Task<HttpResponseMessage> CreateCheckInAsync(Guid id, string? note, string? photoUrl = null)
        => _http.PostAsJsonAsync($"commitments/{id}/checkins", new { note, photoUrl });

    private class ResponseWrapper { public List<CommitmentSummary>? items { get; set; } }
}
