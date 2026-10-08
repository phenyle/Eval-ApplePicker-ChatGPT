namespace Forge.Bench.GenAI
{
    /// <summary>One scheduled GenAI request (SCHEMA.md requests_&lt;role&gt;.csv).</summary>
    public sealed class GenAIRequest
    {
        public int Id;
        public int Target;
        public long SchedQpc, SentQpc, DoneQpc, AppliedQpc, VisibleQpc;
        public int VisibleFrame = -1;
        public string Status = "";
        public int HttpCode = -1;
        public int Retries;
        public float LatencyMs = float.NaN;
        public int TokensIn = -1, TokensOut = -1, BytesOut = -1, BytesIn = -1;
        public string Text = "";
    }

    /// <summary>Prompts and request body for the institutional gateway's native /query endpoint; must match
    /// analysis/harness/genai_common.py (request_body, PROMPTS, SYSTEM_PROMPT).</summary>
    public static class GenAIPrompts
    {
        public const string AP = "1 line greeting";
        public const string CG = "You're a zombie trying to eat the car. What do you say? Limit to 5 words or less.";

        public static string For(string game) => game == "CG" ? CG : AP;

        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        public const string SystemPrompt = " ";   // suppresses the gateway project's default system prompt

        /// <summary>Same key order and separators as Python json.dumps(request_body(...)).
        /// `model` is "provider/name", e.g. "openai/gpt4o_mini".</summary>
        public static string Body(string game, string model, int maxTokens, float temperature)
        {
            model ??= "";
            int slash = model.IndexOf('/');
            string provider = slash > 0 ? model.Substring(0, slash) : "";
            string name = slash > 0 ? model.Substring(slash + 1) : model;
            string t = temperature.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture);
            return "{\"query\": \"" + Esc(For(game)) + "\", \"endpoint\": \"query\", \"request_source\": \"override_params\", " +
                   "\"model_provider\": \"" + Esc(provider) + "\", \"model_name\": \"" + Esc(name) + "\", " +
                   "\"model_params\": {\"temperature\": " + t + ", \"max_tokens\": " + maxTokens +
                   ", \"system_prompt\": \"" + Esc(SystemPrompt) + "\"}, \"enable_search\": false, \"enable_history\": false}";
        }
    }

    // Gateway response: {"response": "<text>", "metadata": {"usage_metric": {"input_token_count", "output_token_count", ...}}}
    [System.Serializable] public class RespUsage { public int input_token_count = -1; public int output_token_count = -1; }
    [System.Serializable] public class RespMeta { public RespUsage usage_metric; }
    [System.Serializable] public class RespBody { public string response; public RespMeta metadata; }

    [System.Serializable] public class PregenEntry { public int http_code; public float latency_ms; public string text; public int tokens_in; public int tokens_out; }
    [System.Serializable] public class PregenFile { public string[] sources; public PregenEntry[] entries; }
}
