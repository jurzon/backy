using System.Net.Http.Json;
using System.Diagnostics.CodeAnalysis;

namespace CommitmentsBlazor.Services;

#pragma warning disable SA1101 // this. prefix not required
#pragma warning disable CA1054 // Keep string for simplicity in sample
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

    private static Uri U(string path) => new(path, UriKind.Relative);

    public Task<HttpResponseMessage> CancelAsync(Guid id) => _http.PostAsync(U($"commitments/{id}/actions/cancel"), null);
    public Task<HttpResponseMessage> CompleteAsync(Guid id) => _http.PostAsync(U($"commitments/{id}/actions/complete"), null);
    public Task<HttpResponseMessage> FailAsync(Guid id) => _http.PostAsync(U($"commitments/{id}/actions/fail"), null);
    public Task<HttpResponseMessage> DeleteAsync(Guid id) => _http.PostAsync(U($"commitments/{id}/actions/delete"), null);

    public Task<HttpResponseMessage> CreateCheckInAsync(Guid id, string? note, string? photoUrl = null)
        => _http.PostAsJsonAsync(U($"commitments/{id}/checkins"), new { note, photoUrl });

    public Task<HttpResponseMessage> CreateCommitmentAsync(object payload, CancellationToken ct = default)
        => _http.PostAsJsonAsync("commitments", payload, ct);

    private class ResponseWrapper { public List<CommitmentSummary>? items { get; set; } }
}
#pragma warning restore CA1054
#pragma warning restore SA1101
