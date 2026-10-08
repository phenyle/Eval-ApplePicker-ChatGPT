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

    /// <summary>Prompts and request body; must match analysis/harness/genai_common.py.</summary>
    public static class GenAIPrompts
    {
        public const string AP = "1 line greeting";
        public const string CG = "You're a zombie trying to eat the car. What do you say? Limit to 5 words or less.";

        public static string For(string game) => game == "CG" ? CG : AP;

        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>Same key order and separators as Python json.dumps(request_body(...)).</summary>
        public static string Body(string game, string model, int maxTokens, float temperature) =>
            "{\"model\": \"" + Esc(model ?? "") + "\", \"input\": \"" + Esc(For(game)) +
            "\", \"max_output_tokens\": " + maxTokens + ", \"temperature\": " +
            temperature.ToString("0.0###", System.Globalization.CultureInfo.InvariantCulture) + ", \"store\": false}";
    }

    [System.Serializable] public class RespContent { public string type; public string text; }
    [System.Serializable] public class RespOutput { public string type; public RespContent[] content; }
    [System.Serializable] public class RespUsage { public int input_tokens = -1; public int output_tokens = -1; }
    [System.Serializable] public class RespBody { public RespOutput[] output; public RespUsage usage; }

    [System.Serializable] public class PregenEntry { public int http_code; public float latency_ms; public string text; public int tokens_in; public int tokens_out; }
    [System.Serializable] public class PregenFile { public string[] sources; public PregenEntry[] entries; }
}
