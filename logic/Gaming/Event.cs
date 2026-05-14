using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Preparation.Utility;

namespace Gaming
{
    public partial class Game
    {
        private int nextEventTriggerMs = GameData.MarketEventIntervalMs;

        private sealed class Event
        {
            public string Name { get; private set; } = string.Empty;
            public string Description { get; private set; } = string.Empty;

            private readonly double[] goodsPriceMultipliers = new double[6];
            private int _startTimeMs;
            private int _endTimeMs;


            public Event()
            {
                InitDefault();
            }

            public async Task<bool> InitializeFromLLMAsync(int startTimeMs, int endTimeMs, CancellationToken cancellationToken = default)
            {
                try
                {
                    var generated = await RequestEventFromLLMAsync(cancellationToken);
                    if (generated == null)
                    {
                        LogicLogging.logger.LogWarning("Event LLM generation failed, fallback to default event.");
                        InitDefault();
                        return false;
                    }

                    Initialize(generated.Name, generated.Description, generated.Multipliers, startTimeMs, endTimeMs);
                    return true;
                }
                catch (Exception ex)
                {
                    LogicLogging.logger.LogError($"Event LLM generation exception: {ex.Message}");
                    InitDefault();
                    return false;
                }
            }

            public bool InitializeFromLLM(int startTimeMs, int endTimeMs)
                => InitializeFromLLMAsync(startTimeMs, endTimeMs).GetAwaiter().GetResult();

            public async Task<string?> AskWithPromptAsync(string prompt, CancellationToken cancellationToken = default)
            {
                try
                {
                    return await RequestTextFromLLMAsync(prompt, cancellationToken);
                }
                catch (Exception ex)
                {
                    LogicLogging.logger.LogError($"AskWithPromptAsync failed: {ex.Message}");
                    return null;
                }
            }

            public string? AskWithPrompt(string prompt)
            {
                using var cts = new CancellationTokenSource(GameData.AskAITimeoutMs);
                return AskWithPromptAsync(prompt, cts.Token).GetAwaiter().GetResult();
            }

            public void InitDefault()
            {
                Name = "normal";
                Description = "No special event.";
                for (int i = 0; i < goodsPriceMultipliers.Length; i++)
                    goodsPriceMultipliers[i] = 1.0;
                _startTimeMs = 0;
                _endTimeMs = int.MaxValue;
            }

            public void Initialize(
                string name,
                string description,
                IReadOnlyList<double>? multipliers,
                int startTimeMs,
                int endTimeMs)
            {
                Name = name ?? string.Empty;
                Description = description ?? string.Empty;

                for (int i = 0; i < goodsPriceMultipliers.Length; i++)
                    goodsPriceMultipliers[i] = 1.0;

                if (multipliers != null)
                {
                    int count = Math.Min(goodsPriceMultipliers.Length, multipliers.Count);
                    for (int i = 0; i < count; i++)
                    {
                        double m = multipliers[i];
                        goodsPriceMultipliers[i] = m <= 0 ? 1.0 : m;
                    }
                }

                _startTimeMs = Math.Max(0, startTimeMs);
                _endTimeMs = endTimeMs <= _startTimeMs ? _startTimeMs + 1 : endTimeMs;

            }

            public bool IsActive(int nowTimeMs)
            {
                return nowTimeMs >= _startTimeMs && nowTimeMs <= _endTimeMs;
            }

            public double GetMultiplier(GoodsType type)
            {
                int idx = (int)type;
                if (idx < 0 || idx >= goodsPriceMultipliers.Length) return 1.0;
                return goodsPriceMultipliers[idx] <= 0 ? 1.0 : goodsPriceMultipliers[idx];
            }

            public int AdjustMarketPrice(GoodsType type, int basePrice, int nowTimeMs)
            {
                if (basePrice <= 0) return 0;
                if (!IsActive(nowTimeMs)) return basePrice;

                double mul = GetMultiplier(type);
                long adjusted = (long)Math.Round(basePrice * mul);
                if (adjusted < 0) adjusted = 0;
                if (adjusted > int.MaxValue) adjusted = int.MaxValue;
                return (int)adjusted;
            }

            public IReadOnlyList<double> SnapshotMultipliers()
            {
                return (double[])goodsPriceMultipliers.Clone();
            }

            private sealed class ChatRequest
            {
                [JsonPropertyName("model")]
                public string Model { get; set; } = string.Empty;

                [JsonPropertyName("messages")]
                public List<ChatMessage> Messages { get; set; } = [];

                [JsonPropertyName("temperature")]
                public double Temperature { get; set; } = 0.9;

                [JsonPropertyName("max_tokens")]
                public int MaxTokens { get; set; } = 1024;
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

            private static readonly HttpClient httpClient = new();

            private static async Task<GeneratedEvent?> RequestEventFromLLMAsync(CancellationToken cancellationToken)
            {
                if (string.IsNullOrWhiteSpace(GameData.API_key) ||
                    string.IsNullOrWhiteSpace(GameData.API_url) ||
                    string.IsNullOrWhiteSpace(GameData.ModelName))
                {
                    LogicLogging.logger.LogError("Event LLM config missing in GameData.");
                    return null;
                }

                if (httpClient.DefaultRequestHeaders.Authorization == null)
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GameData.API_key);

                var req = new ChatRequest
                {
                    Model = GameData.ModelName,
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

                var json = JsonSerializer.Serialize(req);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await httpClient.PostAsync(GameData.API_url, content, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    LogicLogging.logger.LogError($"Event LLM HTTP failed: {(int)resp.StatusCode}");
                    return null;
                }

                var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
                var answer = TryExtractAssistantContent(raw);
                if (string.IsNullOrWhiteSpace(answer))
                    return null;

                var jsonPart = ExtractJsonObject(answer);
                if (string.IsNullOrWhiteSpace(jsonPart))
                    return null;

                var generated = JsonSerializer.Deserialize<GeneratedEvent>(jsonPart);
                if (generated == null)
                    return null;

                NormalizeGeneratedEvent(generated);
                return generated;
            }

            private static async Task<string?> RequestTextFromLLMAsync(string prompt, CancellationToken cancellationToken)
            {
                if (string.IsNullOrWhiteSpace(GameData.API_key) ||
                    string.IsNullOrWhiteSpace(GameData.API_url) ||
                    string.IsNullOrWhiteSpace(GameData.ModelName))
                {
                    LogicLogging.logger.LogError("AskAI config missing in GameData.");
                    return null;
                }

                if (httpClient.DefaultRequestHeaders.Authorization == null)
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GameData.API_key);

                var req = new ChatRequest
                {
                    Model = GameData.ModelName,
                    MaxTokens = 512,
                    Messages =
                    [
                        new ChatMessage
                        {
                            Role = "system",
                            Content = "You are an in-game strategy assistant. Reply with concise plain text only."
                        },
                        new ChatMessage
                        {
                            Role = "user",
                            Content = prompt
                        }
                    ]
                };

                var json = JsonSerializer.Serialize(req);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await httpClient.PostAsync(GameData.API_url, content, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    LogicLogging.logger.LogError($"AskAI HTTP failed: {(int)resp.StatusCode}");
                    return null;
                }

                var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
                var answer = TryExtractAssistantContent(raw);
                return string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
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

                if (generated.Multipliers == null)
                    generated.Multipliers = [];

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

        private readonly Event marketEvent = new();

        internal int GetAdjustedMarketPrice(int basePrice, GoodsType type)
            => marketEvent.AdjustMarketPrice(type, basePrice, NowTime());

        internal bool RefreshEventFromLLM(int startTimeMs, int endTimeMs)
            => marketEvent.InitializeFromLLM(startTimeMs, endTimeMs);

        internal void ResetEventSchedule()
        {
            marketEvent.InitDefault();
            nextEventTriggerMs = 0;  // 游戏开始后立即触发首次事件
        }

        internal void TryTriggerPeriodicEvent(int nowTimeMs)
        {
            if (nowTimeMs < nextEventTriggerMs) return;

            while (nextEventTriggerMs <= nowTimeMs)
                nextEventTriggerMs += GameData.MarketEventIntervalMs;

            int startMs = nowTimeMs;
            int endMs = (int)Math.Min((long)int.MaxValue, (long)startMs + GameData.MarketEventIntervalMs);
            _ = Task.Run(() => RefreshEventFromLLM(startMs, endMs));
        }
    }
}
