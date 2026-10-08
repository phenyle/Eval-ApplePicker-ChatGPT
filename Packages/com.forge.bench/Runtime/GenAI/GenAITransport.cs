using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Forge.Bench.GenAI
{
    /// <summary>
    /// HTTP transport for `stub` and `live` modes (plan §2.2) to the gateway's native /query endpoint (or the replay
    /// stub, which serves the same schema): HttpClient with continuations off the main thread;
    /// completed requests go to a ConcurrentQueue drained once per frame by GenAIService. The gateway token is read
    /// from OPENAI_API_KEY only in live mode and is never logged.
    /// </summary>
    public sealed class GenAITransport
    {
        readonly HttpClient _http;
        readonly string _url;
        readonly byte[] _body;
        readonly long _seed;
        readonly bool _live;
        public readonly ConcurrentQueue<GenAIRequest> Completed = new();

        public GenAITransport(BenchConfig cfg)
        {
            _live = cfg.genai == "live";
            _url = (_live ? cfg.live_url : cfg.stub_url).TrimEnd('/') + "/query";
            _body = Encoding.UTF8.GetBytes(GenAIPrompts.Body(cfg.game, cfg.model, cfg.max_output_tokens, cfg.temperature));
            _seed = cfg.seed;
            var handler = new HttpClientHandler { UseProxy = false };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            if (_live)
            {
                var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (string.IsNullOrEmpty(key)) RunController.Fail("live mode but OPENAI_API_KEY not set");
                else _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + key);
            }
        }

        public int BodyBytes => _body.Length;

        /// <summary>Called on the main thread; the synchronous prefix (request construction) is main-thread cost.</summary>
        public void Send(GenAIRequest r) => _ = SendAsync(r);

        async Task SendAsync(GenAIRequest r)
        {
            int maxRetries = _live ? 2 : 0;
            for (int attempt = 0; ; attempt++)
            {
                long t0 = Stopwatch.GetTimestamp();
                try
                {
                    using var msg = new HttpRequestMessage(HttpMethod.Post, _url);
                    msg.Content = new ByteArrayContent(_body);
                    msg.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json");
                    msg.Headers.TryAddWithoutValidation("X-Bench-Seed", _seed.ToString());
                    msg.Headers.TryAddWithoutValidation("X-Bench-Req-Id", r.Id.ToString());
                    using var resp = await _http.SendAsync(msg).ConfigureAwait(false);
                    var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    r.DoneQpc = Stopwatch.GetTimestamp();
                    r.HttpCode = (int)resp.StatusCode;
                    r.BytesIn = bytes.Length;
                    if (r.HttpCode == 200)
                    {
                        try
                        {
                            var body = JsonUtility.FromJson<RespBody>(Encoding.UTF8.GetString(bytes));
                            r.Text = Extract(body);
                            r.TokensIn = body?.metadata?.usage_metric?.input_token_count ?? -1;
                            r.TokensOut = body?.metadata?.usage_metric?.output_token_count ?? -1;
                            r.Status = r.Text.Length > 0 ? "ok" : "parse_error";
                        }
                        catch (Exception) { r.Status = "parse_error"; }
                    }
                    else r.Status = "http_error";
                }
                catch (TaskCanceledException) { r.DoneQpc = Stopwatch.GetTimestamp(); r.Status = "timeout"; }
                catch (Exception) { r.DoneQpc = Stopwatch.GetTimestamp(); r.Status = "http_error"; }
                r.LatencyMs = (float)((r.DoneQpc - t0) * 1000.0 / Stopwatch.Frequency);
                bool retryable = r.Status == "timeout" || r.HttpCode == 429 || r.HttpCode >= 500;
                if (!retryable || attempt >= maxRetries) break;
                r.Retries++;
                double jitter = 0.5 + BenchRng.U01d(_seed, BenchRng.StreamBackoff, r.Id * 4L + attempt);
                await Task.Delay(TimeSpan.FromSeconds(0.5 * Math.Pow(2, attempt) * jitter)).ConfigureAwait(false);
            }
            Completed.Enqueue(r);
        }

        public static string Extract(RespBody b) => (b?.response ?? "").Trim();
    }
}
