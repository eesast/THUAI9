using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Preparation.Utility;

namespace LlmApiTest;

internal static class Program
{
    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = [];

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.9;

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 2048;
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class GeneratedEvent
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("multipliers")]
        public List<double>? Multipliers { get; set; }
    }

    public static async Task Main()
    {
        if (string.IsNullOrWhiteSpace(GameData.LLM_api_token) ||
            string.IsNullOrWhiteSpace(GameData.LLM_api_url) ||
            string.IsNullOrWhiteSpace(GameData.LLM_model))
        {
            Console.WriteLine("GameData API config is missing.");
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        GeneratedEvent? generated;
        try
        {
            generated = await RequestEventFromLLMAsync(cts.Token);
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("Request timeout/canceled.");
            return;
        }

        if (generated == null)
        {
            Console.WriteLine("LLM request failed or invalid response.");
            return;
        }

        Console.WriteLine("LLM OK");
        Console.WriteLine($"name: {generated.Name}");
        Console.WriteLine($"description: {generated.Description}");
        Console.WriteLine("multipliers: [" + string.Join(", ", generated.Multipliers!) + "]");
    }

    private static async Task<GeneratedEvent?> RequestEventFromLLMAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GameData.LLM_api_token);

        var req = new ChatRequest
        {
            Model = GameData.LLM_model,
            Messages =
            [
                new ChatMessage
                {
                    Role = "system",
                    Content = BuildStrictSystemPrompt()
                },
                new ChatMessage
                {
                    Role = "user",
                    Content = "Generate one random game market event now."
                }
            ]
        };

        var payload = JsonSerializer.Serialize(req);
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var resp = await http.PostAsync(GameData.LLM_api_url, content, cancellationToken);

        Console.WriteLine($"HTTP {(int)resp.StatusCode} {resp.StatusCode}");
        if (!resp.IsSuccessStatusCode)
        {
            var failText = await resp.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine(failText);
            return null;
        }

        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
        Console.WriteLine("RAW RESPONSE:");
        Console.WriteLine(raw);

        var answer = TryExtractAssistantContent(raw);
        if (string.IsNullOrWhiteSpace(answer))
        {
            Console.WriteLine("Cannot extract assistant content from response.");
            return null;
        }

        var jsonPart = ExtractJsonObject(answer);
        if (string.IsNullOrWhiteSpace(jsonPart))
            return null;

        var generated = JsonSerializer.Deserialize<GeneratedEvent>(jsonPart);
        if (generated == null)
            return null;

        NormalizeGeneratedEvent(generated);
        return generated;
    }

    private static string? TryExtractAssistantContent(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            return null;

        var first = choices[0];
        if (!first.TryGetProperty("message", out var message))
            return null;
        if (!message.TryGetProperty("content", out var content))
            return null;

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString();

        if (content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    sb.Append(item.GetString());
                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        sb.Append(text.GetString());
                }
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        return null;
    }

    private static string BuildStrictSystemPrompt()
    {
        return """
You are generating one random in-game event for a market-pricing system.

Output MUST be valid JSON only (no markdown, no explanation).
Use EXACT schema:
{
  "name": "string",
  "description": "string",
  "multipliers": [m0,m1,m2,m3,m4,m5]
}

Rules:
1) multipliers must contain exactly 6 numbers.
2) Index mapping is fixed:
   0=NULL_GOODS_TYPE, 1=SEMICONDUCTOR, 2=MEDICINE, 3=TOYS, 4=CLOTHES, 5=FOOD.
3) m0 must always be 1.0.
4) Each m1..m5 must be in [0.5, 1.5].
5) name should be short natural language event (e.g., "storm", "festival", "chip shortage").
6) description can be short or long natural language.
7) Return only one event JSON object.
""";
    }

    private static string? ExtractJsonObject(string text)
    {
        int l = text.IndexOf('{');
        int r = text.LastIndexOf('}');
        if (l < 0 || r < l) return null;
        return text.Substring(l, r - l + 1);
    }

    private static void NormalizeGeneratedEvent(GeneratedEvent generated)
    {
        generated.Name = string.IsNullOrWhiteSpace(generated.Name) ? "random-event" : generated.Name.Trim();
        generated.Description = generated.Description ?? string.Empty;

        generated.Multipliers ??= [];

        while (generated.Multipliers.Count < 6)
            generated.Multipliers.Add(1.0);

        if (generated.Multipliers.Count > 6)
            generated.Multipliers = generated.Multipliers.GetRange(0, 6);

        generated.Multipliers[0] = 1.0;
        for (int i = 1; i <= 5; i++)
        {
            double m = generated.Multipliers[i];
            if (double.IsNaN(m) || double.IsInfinity(m)) m = 1.0;
            if (m < 0.5) m = 0.5;
            if (m > 1.5) m = 1.5;
            generated.Multipliers[i] = m;
        }
    }
}
