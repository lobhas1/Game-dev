using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace Arena;

/// <summary>The LLM behind kit generation. The only I/O boundary; everything else is offline.</summary>
public interface IOracle
{
    Task<string> CompleteAsync(string prompt);
}

/// <summary>Deterministic canned responses for offline tests — returned in order, one per call.</summary>
public sealed class StubOracle : IOracle
{
    private readonly Queue<string> _responses;
    public StubOracle(params string[] responses) => _responses = new Queue<string>(responses);
    public StubOracle(IEnumerable<string> responses) => _responses = new Queue<string>(responses);
    public int CallCount { get; private set; }

    public Task<string> CompleteAsync(string prompt)
    {
        CallCount++;
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : "[]");
    }
}

/// <summary>Live Anthropic Messages API. The API key is read ONLY from ANTHROPIC_API_KEY and never
/// appears in code, arguments, logs, or commits. Constructed only for the human's live run and the
/// skipped live test; never used offline.</summary>
public sealed class LiveAnthropicOracle : IOracle
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _apiKey;

    public LiveAnthropicOracle(string model, HttpClient? http = null)
    {
        _model = model;
        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                  ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not set in the environment.");
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task<string> CompleteAsync(string prompt)
    {
        var body = new JsonObject
        {
            ["model"] = _model,
            ["max_tokens"] = 3000,
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = prompt } }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req);
        string text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic API returned {(int)resp.StatusCode}: {Clip(text, 300)}");

        var doc = JsonNode.Parse(text);
        var content = doc?["content"] as JsonArray
                      ?? throw new InvalidOperationException("Anthropic response had no content array.");
        var sb = new StringBuilder();
        foreach (var block in content)
            if (block?["type"]?.GetValue<string>() == "text")
                sb.Append(block?["text"]?.GetValue<string>());
        return sb.ToString();
    }

    private static string Clip(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";
}
