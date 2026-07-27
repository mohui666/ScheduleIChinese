using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace ScheduleIChinese
{
    /// <summary>
    /// Background machine translation through the free Google Translate endpoint.
    /// Results are cached into Translations/Auto.zh_CN.txt and applied to live
    /// components on the main thread by MainThreadRunner.
    /// </summary>
    public static class AutoTranslator
    {
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();
        private static readonly HashSet<string> _pending = new HashSet<string>(StringComparer.Ordinal);
        private static readonly ConcurrentQueue<KeyValuePair<string, string>> _results = new ConcurrentQueue<KeyValuePair<string, string>>();
        private static readonly Dictionary<string, int> _failures = new Dictionary<string, int>(StringComparer.Ordinal);

        private static HttpClient _http;
        private static string _autoFile;
        private static Thread _thread;

        private static readonly Regex RxTag = new Regex("<[^>]+>|\\{[0-9]+(:[^}]*)?\\}", RegexOptions.Compiled);

        public static void Start()
        {
            _autoFile = Path.Combine(Plugin.DataDir, "Translations", "Auto.zh_CN.txt");
            _http = new HttpClient();
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            _http.Timeout = TimeSpan.FromSeconds(15);

            _thread = new Thread(Worker) { IsBackground = true, Name = "ScheduleIChinese.Translator" };
            _thread.Start();
        }

        public static void Enqueue(string src)
        {
            if (string.IsNullOrEmpty(src)) return;
            lock (_pending)
            {
                if (_failures.TryGetValue(src, out var f) && f >= 3) return;
                if (_pending.Add(src)) _queue.Enqueue(src);
            }
        }

        /// <summary>Drain finished translations on the main thread.</summary>
        public static void ApplyPendingOnMainThread()
        {
            int n = 0;
            while (n++ < 64 && _results.TryDequeue(out var kv))
            {
                TranslationStore.AddRuntime(kv.Key, kv.Value);
                TranslationStore.ApplyToLive(kv.Key, kv.Value);
            }
        }

        private static void Worker()
        {
            var batch = new List<string>();
            while (true)
            {
                try
                {
                    batch.Clear();
                    int chars = 0;
                    while (batch.Count < 25 && chars < 1500 && _queue.TryDequeue(out var s))
                    {
                        // never batch multi-line strings; they collide with the \n join
                        if (s.Contains("\n")) { TranslateSingle(s); continue; }
                        batch.Add(s);
                        chars += s.Length;
                    }
                    if (batch.Count == 0)
                    {
                        Thread.Sleep(200);
                        continue;
                    }
                    TranslateBatch(batch);
                    Thread.Sleep(ModConfig.AutoTranslateDelayMs.Value);
                }
                catch (ThreadAbortException) { }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("translator worker: " + e.Message);
                    Thread.Sleep(2000);
                }
            }
        }

        // ---------------- placeholder protection ----------------

        private sealed class Protector
        {
            private readonly List<string> _tokens = new List<string>();
            public string Encode(string s)
            {
                return RxTag.Replace(s, m =>
                {
                    _tokens.Add(m.Value);
                    return "⟦" + (_tokens.Count - 1) + "⟧";
                });
            }
            public string Decode(string s)
            {
                for (int i = 0; i < _tokens.Count; i++)
                    s = s.Replace("⟦" + i + "⟧", _tokens[i]);
                return s;
            }
        }

        private static void TranslateSingle(string src)
        {
            try
            {
                // translate each line separately to keep \n intact
                var parts = src.Split('\n');
                var prot = new Protector();
                for (int i = 0; i < parts.Length; i++)
                    parts[i] = parts[i].Length == 0 ? "" : prot.Decode(Google(prot.Encode(parts[i])));
                var joined = string.Join("\n", parts);
                Commit(src, joined);
            }
            catch (Exception e) { Fail(src, e); }
        }

        private static void TranslateBatch(List<string> batch)
        {
            try
            {
                var prot = new Protector();
                var encoded = new string[batch.Count];
                for (int i = 0; i < batch.Count; i++) encoded[i] = prot.Encode(batch[i]);
                var joined = string.Join("\n", encoded);
                var translatedJoined = prot.Decode(Google(joined));
                var parts = translatedJoined.Split('\n');
                if (parts.Length != batch.Count)
                {
                    // line alignment broke down; do them one by one
                    foreach (var s in batch) TranslateSingle(s);
                    return;
                }
                for (int i = 0; i < batch.Count; i++) Commit(batch[i], parts[i]);
            }
            catch (Exception e)
            {
                foreach (var s in batch) Fail(s, e);
            }
        }

        private static string Google(string text)
        {
            var url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&tl=zh-CN&dt=t&q="
                      + Uri.EscapeDataString(text);
            var json = _http.GetStringAsync(url).GetAwaiter().GetResult();
            using (var doc = JsonDocument.Parse(json))
            {
                var sb = new StringBuilder();
                foreach (var seg in doc.RootElement[0].EnumerateArray())
                    sb.Append(seg[0].GetString());
                return sb.ToString();
            }
        }

        private static void Commit(string src, string translated)
        {
            lock (_pending) _pending.Remove(src);
            // Reject empty results and results identical to the source: caching
            // those would permanently block a proper translation later.
            if (string.IsNullOrWhiteSpace(translated) ||
                string.Equals(src, translated, StringComparison.Ordinal) ||
                !TranslationStore.ContainsCjk(translated)) { Fail(src, null); return; }
            _results.Enqueue(new KeyValuePair<string, string>(src, translated));
            try
            {
                lock (AutoFileLock)
                    File.AppendAllText(_autoFile,
                        TranslationStore.Escape(src) + "=" + TranslationStore.Escape(translated) + "\n",
                        Encoding.UTF8);
            }
            catch { }
        }

        private static readonly object AutoFileLock = new object();

        private static void Fail(string src, Exception e)
        {
            lock (_pending)
            {
                _pending.Remove(src);
                _failures.TryGetValue(src, out var n);
                _failures[src] = n + 1;
            }
            if (e != null) Plugin.Log.LogDebug($"translate failed ({src.Substring(0, Math.Min(40, src.Length))}): {e.Message}");
        }
    }
}
