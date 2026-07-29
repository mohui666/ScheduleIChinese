using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;

namespace ScheduleIChinese
{
    /// <summary>
    /// Holds the translation dictionary, the dump list of unknown strings and the
    /// registry of live text components (used to apply late auto-translations).
    /// File format: one entry per line, "original=translated", escapes: \\ \n \r
    /// </summary>
    public static class TranslationStore
    {
        private static readonly Dictionary<string, string> _dict = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _patternSources =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly List<PatternEntry> _patterns = new List<PatternEntry>();
        private static readonly List<PatternEntry> _fallbackPatterns =
            new List<PatternEntry>();
        private static readonly Dictionary<char, List<PatternEntry>> _patternCandidates =
            new Dictionary<char, List<PatternEntry>>();
        private static readonly Dictionary<string, string> _resultCache =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _effects =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _nameSources =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _preserveNames =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _noHit = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _dumped = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<string> _dumpPending = new List<string>();
        private static long _resultCacheHits;
        private static long _negativeCacheHits;
        private static long _regexEvaluations;
        private static long _regexMatches;

        // source string -> components currently showing it (waiting for auto translation)
        private static readonly Dictionary<string, List<WeakReference<TMP_Text>>> _live =
            new Dictionary<string, List<WeakReference<TMP_Text>>>(StringComparer.Ordinal);

        private static string _transDir;
        private static string _dumpFile;
        private static DateTime _lastDumpFlush = DateTime.MinValue;
        private static readonly Regex GuidLike = new Regex(
            "^[{(]?[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}[)}]?$",
            RegexOptions.Compiled);
        private static readonly Regex AssetLike = new Regex(
            @"\.(dll|cs|asset|prefab|mat|shader|png|jpg|wav|mp3|json|xml)(\b|$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DisplayValueLike = new Regex(
            @"^(?:\d{3,5}x\d{3,5}|\$[\d,.]+[KMB]?|\d+(?:\.\d+)?°\s?[CF]|v?\d+(?:\.\d+){1,4}[a-z]?\d*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex IdentifierLike = new Regex(
            @"^[A-Za-z_][A-Za-z0-9_]*\d[A-Za-z0-9_]*$",
            RegexOptions.Compiled);
        // Keys the game reads back in code must never be translated (quality
        // tiers like "Standard", input bindings like "Backspace", console
        // commands like "addxp", UI popup responses like "Accept"). They are
        // curated in Translations/deny_keys.txt, generated from the game's
        // enum metadata (see tools/restore_safe_keys.py). Junk keys such as
        // hex color blobs or digit-containing tokens are rejected as well.
        // Translating these has repeatedly broken saves and UI logic.
        private static readonly HashSet<string> _denyKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex JunkKey = new Regex(
            @"^(?:[0-9A-Fa-f]{6,8}|[A-Za-z0-9_]*\d[A-Za-z0-9_]*)$",
            RegexOptions.Compiled);
        private static readonly Regex PlaceholderLike = new Regex(
            @"^<[A-Z][A-Z0-9 _-]+>$",
            RegexOptions.Compiled);
        private static readonly Regex DecoratedEffect = new Regex(
            @"\A(?<prefix>\s*(?:<[^>]+>\s*)*(?:[•▪●\-]\s*(?:<[^>]+>\s*)*)?)(?<term>[A-Za-z][A-Za-z -]*?)(?<suffix>\s*(?:<[^>]+>\s*)*)\z",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CapitalizedWord = new Regex(
            @"\b[A-Z][A-Za-z'’\-]*\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex PersonRegionLabel = new Regex(
            @"\A(?<name>(?:(?:Mr|Mrs|Dr)\.\s+[A-Z][A-Za-z'’\-]*|[A-Z][A-Za-z'’\-]*(?:\s+[A-Z][A-Za-z'’\-]*){1,3}))(?=<color=[^>]+>\s*\((?:Northtown|Downtown|Suburbia|Uptown|Westville|Docks)\)</color>\z)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex QuantityDisplay = new Regex(
            @"\A(?<count>\d+)x\s+(?<item>.+)\z",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private sealed class PatternEntry
        {
            public string Source;
            public string Replacement;
            public Regex Regex;
            public char? RequiredFirstCharacter;
        }

        public static int Count => _dict.Count;
        public static int PatternCount => _patterns.Count;

        public static void Init()
        {
            // Reload-safe: drop every piece of previous state first.
            _dict.Clear();
            _patternSources.Clear();
            _patterns.Clear();
            _fallbackPatterns.Clear();
            _patternCandidates.Clear();
            _resultCache.Clear();
            _effects.Clear();
            _nameSources.Clear();
            _preserveNames.Clear();
            _denyKeys.Clear();
            _noHit.Clear();
            _dumped.Clear();
            _disabledPatterns.Clear();
            _resultCacheHits = 0;
            _negativeCacheHits = 0;
            _regexEvaluations = 0;
            _regexMatches = 0;
            lock (_dumpPending) _dumpPending.Clear();
            lock (_live) _live.Clear();

            _transDir = Path.Combine(Plugin.DataDir, "Translations");
            _dumpFile = Path.Combine(Plugin.DataDir, "Untranslated.txt");
            Directory.CreateDirectory(_transDir);

            // Auto-generated translations load first so curated files override them.
            var files = new List<string>(Directory.GetFiles(_transDir, "*.txt", SearchOption.AllDirectories));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            // The denylist must be fully populated before any translation file
            // loads, otherwise an alphabetically earlier file (Auto.zh_CN.txt)
            // could sneak denied keys in.
            foreach (var f in files)
                if (Path.GetFileName(f).Equals("deny_keys.txt", StringComparison.OrdinalIgnoreCase))
                    LoadDenyKeys(f);
            string effectsFile = null;
            foreach (var f in files)
            {
                if (Path.GetFileName(f).Equals("preserve_names.txt", StringComparison.OrdinalIgnoreCase))
                    LoadPreservedNames(f);
                else if (Path.GetFileName(f).Equals("effects_zh_CN.txt", StringComparison.OrdinalIgnoreCase))
                    effectsFile = f;
                else if (Path.GetFileName(f).Equals("deny_keys.txt", StringComparison.OrdinalIgnoreCase))
                    continue;
                else
                    LoadFile(f);
            }
            // The effect glossary is a curated override and must win over the
            // large generated table (for example Refreshing means 清爽 here).
            if (effectsFile != null) LoadFile(effectsFile);
            BuildPatternList();
            Plugin.Log.LogInfo(
                $"Loaded {_dict.Count} exact translations and {_patterns.Count} dynamic rules " +
                $"from {files.Count} file(s); {_effects.Count} effect terms, " +
                $"{_preserveNames.Count} names are protected; {_denyKeys.Count} keys are denied.");
            RunSelfTest();
        }

        private static void LoadFile(string path)
        {
            int n = 0;
            int malformed = 0;
            int blocked = 0;
            bool isEffects = Path.GetFileName(path).Equals(
                "effects_zh_CN.txt", StringComparison.OrdinalIgnoreCase);
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw;
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                int eq = FindSeparator(line);
                if (eq <= 0) { malformed++; continue; }
                var rawKey = Unescape(line.Substring(0, eq));
                var val = Unescape(line.Substring(eq + 1));
                if (rawKey.Length == 0 || val.Length == 0) { malformed++; continue; }

                if (TryGetPatternSource(rawKey, out var pattern))
                    _patternSources[pattern] = val;
                else
                {
                    if (!isEffects && (_denyKeys.Contains(rawKey) || JunkKey.IsMatch(rawKey)))
                    {
                        blocked++;
                        continue;
                    }
                    _dict[rawKey] = val;
                    if (isEffects) _effects[rawKey] = val;
                }
                n++;
            }
            Plugin.Log.LogInfo($"  {Path.GetFileName(path)}: {n} entries" +
                (malformed > 0 ? $", {malformed} malformed line(s) skipped" : "") +
                (blocked > 0 ? $", {blocked} denied/junk key(s) rejected" : ""));
        }

        private static void LoadDenyKeys(string path)
        {
            int n = 0;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;
                if (_denyKeys.Add(line)) n++;
            }
            Plugin.Log.LogInfo($"  {Path.GetFileName(path)}: {n} denied keys");
        }

        private static void LoadPreservedNames(string path)
        {
            int n = 0;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (raw.Length == 0 || raw.StartsWith("#") || raw.StartsWith("//")) continue;
                int eq = FindSeparator(raw);
                if (eq <= 0) continue;
                var source = Unescape(raw.Substring(0, eq));
                var translated = Unescape(raw.Substring(eq + 1));
                if (source.Length == 0 || translated.Length == 0) continue;
                _preserveNames.Add(source);
                _nameSources[source] = translated;
                n++;
            }
            Plugin.Log.LogInfo($"  {Path.GetFileName(path)}: {n} protected name mappings");
        }

        private static bool TryGetPatternSource(string key, out string pattern)
        {
            pattern = null;
            if (key.StartsWith("r:", StringComparison.Ordinal))
                pattern = key.Substring(2);
            else if (key.StartsWith("sr:", StringComparison.Ordinal))
                pattern = key.Substring(3);
            else
                return false;

            pattern = pattern.Trim();
            if (pattern.Length >= 2 && pattern[0] == '"' && pattern[pattern.Length - 1] == '"')
                pattern = pattern.Substring(1, pattern.Length - 2);
            return pattern.Length > 0;
        }

        private static void BuildPatternList()
        {
            _patterns.Clear();
            foreach (var kv in _patternSources)
            {
                try
                {
                    // A translation rule must consume the whole visible string. This keeps
                    // broad community rules such as "Assigned (.+)" from replacing a
                    // substring in unrelated dialogue.
                    // These rules are loaded from data files and most visible
                    // strings are resolved once, then served by _resultCache.
                    // Interpreted regex avoids cold-JITing hundreds of compiled
                    // expressions during the first UI frames.
                    var regex = new Regex(
                        @"\A(?:" + kv.Key + @")\z",
                        RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(10));
                    _patterns.Add(new PatternEntry
                    {
                        Source = kv.Key,
                        Replacement = kv.Value,
                        Regex = regex,
                        RequiredFirstCharacter =
                            TryGetRequiredFirstCharacter(kv.Key, out var first)
                                ? NormalizePatternKey(first)
                                : (char?)null
                    });
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Skipped invalid dynamic translation rule '{kv.Key}': {ex.Message}");
                }
            }

            // More specific rules win over generic capture-all rules.
            _patterns.Sort((a, b) => b.Source.Length.CompareTo(a.Source.Length));
            foreach (var entry in _patterns)
                if (!entry.RequiredFirstCharacter.HasValue)
                    _fallbackPatterns.Add(entry);
        }

        /// <summary>
        /// Return a literal first character only when the regex syntax makes it
        /// mandatory. Anything ambiguous stays in the fallback list, so this
        /// optimization can add candidates but can never exclude a valid rule.
        /// </summary>
        private static bool TryGetRequiredFirstCharacter(
            string pattern,
            out char first)
        {
            first = '\0';
            if (string.IsNullOrEmpty(pattern)) return false;

            int index = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                if (index < pattern.Length && pattern[index] == '^')
                {
                    index++;
                    continue;
                }
                if (index + 4 <= pattern.Length &&
                    string.CompareOrdinal(pattern, index, "(?i)", 0, 4) == 0)
                {
                    index += 4;
                    continue;
                }
                if (index + 2 <= pattern.Length &&
                    pattern[index] == '\\' &&
                    pattern[index + 1] == 'A')
                {
                    index += 2;
                    continue;
                }
                break;
            }

            if (index >= pattern.Length) return false;
            char token = pattern[index];
            if (token == '\\')
            {
                if (index + 1 >= pattern.Length) return false;
                char escaped = pattern[index + 1];
                // Escaped punctuation represents that exact character. Escape
                // classes such as \d, \s, \p and numeric backreferences do not.
                if (char.IsLetterOrDigit(escaped)) return false;
                first = escaped;
                return true;
            }

            if (token == '.' || token == '$' || token == '(' ||
                token == '[' || token == '{' || token == '|' ||
                token == '*' || token == '+' || token == '?')
                return false;

            first = token;
            return true;
        }

        private static char NormalizePatternKey(char value)
        {
            return char.ToUpperInvariant(value);
        }

        private static List<PatternEntry> GetPatternCandidates(string source)
        {
            if (string.IsNullOrEmpty(source)) return _fallbackPatterns;
            char key = NormalizePatternKey(source[0]);
            if (_patternCandidates.TryGetValue(key, out var candidates))
                return candidates;

            candidates = new List<PatternEntry>(_fallbackPatterns.Count + 16);
            foreach (var entry in _patterns)
            {
                if (!entry.RequiredFirstCharacter.HasValue ||
                    entry.RequiredFirstCharacter.Value == key)
                    candidates.Add(entry);
            }
            _patternCandidates[key] = candidates;
            return candidates;
        }

        private static void RunSelfTest()
        {
            var samples = new[]
            {
                "Hey, if you got 2x Meth I'll pay <color=#46CB4F>$135</color> for it. Deal?",
                "Great, I'll meet you <b>behind Hyland gun range</b> between 6:00 AM and 12:00 PM.",
                "Hello, I'm after 3x OG Kush. I can pay <color=#46CB4F>$110</color> for it.",
                "I've received $480 cash from you. Your debt is now paid off.",
                "Molly Presley\n(Dealer)",
                // Region names intentionally stay English in person labels
                // (they are EMapRegion members on the denylist); the dedicated
                // namedRegion check below only requires the name to survive.
                "Last save was 48 seconds ago",
                "<color=#FFD19BFF>•  Calming</color>",
                "•  Sedating",
                "•  Slippery",
                "•  Refreshing",
                "•  Calorie-Dense",
                "Munchies",
                "Smelly",
                "Paranoia",
                "Sneaky",
                "Tropic Thunder",
                "<color=#D6655A>•</color> Munchies",
                "• <color=#D6655A>Paranoia</color>",
                "The hell?!",
                "Psychoactive magic mushroom. Induces strong hallucinogenic effects.\t",
                "There is a <h1>map</h> on your phone. Press <Input_OpenMap> to quickly open it.",
                "If enabled, the product mixing algorithm \nwill be randomized using this save file's seed. \nThis will not affect your existing mixes."
            };

            int failed = 0;
            foreach (var sample in samples)
            {
                var result = Translate(sample);
                if (string.IsNullOrEmpty(result) || !ContainsCjk(result))
                {
                    failed++;
                    Plugin.Log.LogWarning(
                        $"Self-test untranslated: '{Escape(sample)}' => " +
                        $"'{Escape(result ?? "<null>")}'");
                }
            }
            var namedRole = Translate("Molly Presley\n(Dealer)");
            if (string.IsNullOrEmpty(namedRole) ||
                namedRole.IndexOf("Molly Presley", StringComparison.Ordinal) < 0)
                failed++;
            var namedRegion = Translate(
                "Elizabeth Homley<color=#A0A0A0FF> (Downtown)</color>");
            if (string.IsNullOrEmpty(namedRegion) ||
                namedRegion.IndexOf("Elizabeth Homley", StringComparison.Ordinal) < 0)
                failed++;
            if (Translate("Molly Presley") != null ||
                Translate("Thick Crack") != null ||
                Translate("Granddaddy Purple") != null ||
                Translate("40x Granddaddy Purple") != null ||
                Translate("OG Kush") != null ||
                Translate("Sour Diesel") != null ||
                Translate("Face") != null ||
                Translate("Jump") != null)
                failed++;
            var compositeEffects = TranslateDisplayText(
                "<color=#4CB0FF>• Focused</color>\n" +
                "<color=#5ACB4F>• Refreshing</color>\n" +
                "<color=#777777>• Sneaky</color>");
            if (string.IsNullOrEmpty(compositeEffects) ||
                compositeEffects.IndexOf("Focused", StringComparison.Ordinal) >= 0 ||
                compositeEffects.IndexOf("Refreshing", StringComparison.Ordinal) >= 0 ||
                compositeEffects.IndexOf("Sneaky", StringComparison.Ordinal) >= 0)
                failed++;
            var criticalDynamic = new[]
            {
                new[] { "$125K", "$125千" },
                new[] { "+$300 Bonus", "+$300 奖金" },
                new[]
                {
                    "Forfeit and collect <color=#54E717>$120</color>",
                    "没收并拿走 <color=#54E717>$120</color>"
                },
                new[]
                {
                    "Initial Offer $250",
                    "<size=28>初始出价: <color=#2FC443>$250</color></size>"
                },
                new[]
                {
                    "[1] Cheap Skateboard ($75)",
                    "[1] 廉价滑板 ($75)"
                },
                new[]
                {
                    "[6] Offroad Skateboard ($1,500)",
                    "[6] 越野滑板 ($1,500)"
                },
                new[]
                {
                    "<color=#6ED7FF>[3]</color> Lightweight Skateboard " +
                    "<color=#54E717>($500)</color>",
                    "<color=#6ED7FF>[3]</color> 轻型滑板 " +
                    "<color=#54E717>($500)</color>"
                },
                new[]
                {
                    "Pick up Cuke",
                    "拾取酷口可乐"
                },
                new[]
                {
                    "Egg Run",
                    "鸡蛋快跑"
                },
                new[]
                {
                    "EGG RUN",
                    "鸡蛋快跑"
                },
                new[]
                {
                    "Noodle",
                    "贪吃蛇"
                },
                new[]
                {
                    "OFFENSE NOTICE",
                    "处罚通知"
                },
                new[]
                {
                    "You have been convicted of the following:",
                    "你因以下行为被定罪："
                },
                new[]
                {
                    "failure to comply with police instruction",
                    "拒不服从警方指示"
                },
                new[]
                {
                    "possession of low-severity drug",
                    "持有低危毒品"
                },
                new[]
                {
                    "6 low-severity drugs confiscated",
                    "已没收 6 份低危毒品"
                },
                new[]
                {
                    "8 high-severity drugs confiscated",
                    "已没收 8 份高危毒品"
                },
                new[]
                {
                    "$400.00 fine (paid in cash)",
                    "罚款：$400.00（现金支付）"
                },
                new[]
                {
                    "Talk to Pearl",
                    "与 Pearl 交谈"
                },
                new[]
                {
                    "Talk to Uncle Nelson",
                    "与 Uncle Nelson 交谈"
                },
                new[]
                {
                    "Sewer Key required",
                    "需要下水道钥匙"
                },
                new[]
                {
                    "[1] [Complete Deal]",
                    "[1] [完成交易]"
                },
                new[]
                {
                    "[1] I want to buy a sewer access key",
                    "[1] 我想买一把下水道通行钥匙"
                },
                new[]
                {
                    "'FRIENDLY' RELATIONSHIP REQUIRED",
                    "需要达到“友好”关系"
                },
                new[]
                {
                    "Waiting for others...",
                    "正在等待其他玩家……"
                },
                new[]
                {
                    "Click and hold tap to fill (86%)",
                    "在水龙头处按住操作键，加水至 (86%)"
                },
                new[]
                {
                    "Pour into pot (32%)",
                    "倒入花盆中（32%）"
                },
                new[]
                {
                    "Ay bro you got 4x OG Kush? I'll pay <color=#46CB4F>$195</color> for it",
                    "嘿兄弟，你有 4 份 OG Kush 吗？我出 <color=#46CB4F>$195</color>。"
                },
                new[]
                {
                    "<color=#54E717>+$25</color> Exceeded Quality Bonus",
                    "<color=#54E717>+$25</color> 超额品质奖金"
                },
                new[]
                {
                    "<color=#54E717>+$33</color> Rainy Bonus",
                    "<color=#54E717>+$33</color> 雨天奖金"
                },
                new[]
                {
                    "Good!! I'll see u <b>at the north waterfront</b> between 12:00 PM and 6:00 PM.",
                    "好的！！12:00 PM 至 6:00 PM <b>在北滨水区</b>见。"
                },
                new[]
                {
                    "UNLOCK ONE OF BRAD'S CONNECTIONS",
                    "解锁 BRAD 的一位人脉"
                },
                new[]
                {
                    "Much appreciated. Maybe go talk to Chelsey. I think she'd like your product",
                    "多谢。去找 Chelsey 聊聊吧，我觉得对方会喜欢你的货。"
                }
            };
            foreach (var check in criticalDynamic)
            {
                var actual = Translate(check[0]);
                if (!string.Equals(actual, check[1], StringComparison.Ordinal))
                {
                    failed++;
                    Plugin.Log.LogWarning(
                        $"Self-test mismatch: '{Escape(check[0])}' => " +
                        $"'{Escape(actual ?? "<null>")}', expected '{Escape(check[1])}'");
                }
            }

            var criticalDisplay = new[]
            {
                new[] { "OG 库什种子", "OG Kush 种子" },
                new[] { "蓝梦种子", "Blue Dream 种子" },
                new[] { "请求秘密交货", "安排秘密交货" },
                new[]
                {
                    "从 Albert 那选择要订购的东西",
                    "选择向 Albert 订购的商品"
                }
            };
            foreach (var check in criticalDisplay)
            {
                var actual = TranslateDisplayText(check[0]);
                if (!string.Equals(actual, check[1], StringComparison.Ordinal))
                {
                    failed++;
                    Plugin.Log.LogWarning(
                        $"Display self-test mismatch: '{Escape(check[0])}' => " +
                        $"'{Escape(actual ?? "<null>")}', expected '{Escape(check[1])}'");
                }
            }

            if (Translate("Andy ") != null ||
                Translate("Chelsey") != null ||
                Translate("KAESUL") != null ||
                Translate("HYDROBRO") != null)
                failed++;

            var total = samples.Length + 5 +
                        criticalDynamic.Length + criticalDisplay.Length;
            if (failed == 0)
                Plugin.Log.LogInfo($"Offline translation self-test passed ({total}/{total}).");
            else
                Plugin.Log.LogWarning(
                    $"Offline translation self-test failed ({total - failed}/{total} passed).");
        }

        /// <summary>All CJK / full-width characters used by any translation value.</summary>
        public static string CollectCjkChars()
        {
            var set = new HashSet<char>();
            foreach (var kv in _dict)
                foreach (var c in kv.Value)
                    if (IsCjkOrFullwidth(c)) set.Add(c);
            foreach (var kv in _effects)
                foreach (var c in kv.Value)
                    if (IsCjkOrFullwidth(c)) set.Add(c);
            var sb = new StringBuilder(set.Count);
            foreach (var c in set) sb.Append(c);
            return sb.ToString();
        }

        private static bool IsCjkOrFullwidth(char c)
        {
            if (c >= '一' && c <= '鿿') return true;
            if (c >= '　' && c <= '〿') return true;
            if (c >= '＀' && c <= '￠') return true;
            return false;
        }

        public static bool ContainsCjk(string s)
        {
            foreach (var c in s)
                if (c >= '一' && c <= '鿿') return true;
            return false;
        }

        /// <summary>Is this string worth translating at all?</summary>
        public static bool IsTranslatable(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Length > 1000) return false;
            if (GuidLike.IsMatch(s)) return false;
            if (AssetLike.IsMatch(s)) return false;
            if (DisplayValueLike.IsMatch(s)) return false;
            if (IdentifierLike.IsMatch(s)) return false;
            if (PlaceholderLike.IsMatch(s)) return false;
            if (s.IndexOf('\u200B') >= 0 || s.IndexOf('\u200C') >= 0 || s.IndexOf('\u200D') >= 0)
                return false;
            if (s.IndexOf("://", StringComparison.Ordinal) >= 0) return false;
            bool hasLetter = false;
            foreach (var c in s)
            {
                if (c >= '一' && c <= '鿿') return false; // already CJK
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) hasLetter = true;
            }
            return hasLetter;
        }

        /// <summary>
        /// Look up a translation. Returns null when there is none yet
        /// (and records the string for dumping / auto translation).
        /// </summary>
        public static string Translate(string src)
        {
            if (string.IsNullOrEmpty(src) || src.Length > 1000) return null;
            if (_preserveNames.Contains(src)) return null;
            var protectedTrimmed = src.Trim();
            if (protectedTrimmed.Length > 0 &&
                _preserveNames.Contains(protectedTrimmed))
                return null;
            if (_resultCache.TryGetValue(src, out var cached))
            {
                _resultCacheHits++;
                return cached;
            }
            if (_noHit.Contains(src))
            {
                _negativeCacheHits++;
                return null;
            }

            // Curated dynamic rules must be allowed to handle display values such
            // as "$125K". Those are intentionally excluded from dumping and
            // automatic translation, but an explicit offline rule should still win.
            if (!IsTranslatable(src))
            {
                string filteredHit;
                if (TryTranslatePattern(src, out filteredHit))
                {
                    filteredHit = RestoreNames(src, filteredHit);
                    return CacheResult(src, filteredHit);
                }
                _noHit.Add(src);
                return null;
            }

            if (TryTranslateDecoratedEffect(src, out var hit))
                return CacheResult(src, hit);
            if (_dict.TryGetValue(src, out hit))
                return CacheResult(src, RestoreNames(src, hit));
            if (TryTranslateQuantityDisplay(src, out hit))
                return CacheResult(src, hit);

            // try trimmed lookup, preserving the surrounding whitespace
            var trimmed = src.Trim();
            if (trimmed.Length > 0 && trimmed.Length != src.Length && _dict.TryGetValue(trimmed, out hit))
            {
                int lead = src.Length - src.TrimStart().Length;
                int trail = src.Length - src.TrimEnd().Length;
                return CacheResult(
                    src,
                    src.Substring(0, lead) + RestoreNames(trimmed, hit) +
                    src.Substring(src.Length - trail));
            }

            if (TryTranslatePattern(src, out hit))
            {
                hit = RestoreNames(src, hit);
                return CacheResult(src, hit);
            }

            if (_noHit.Add(src))
            {
                if (ModConfig.DumpUntranslated.Value) QueueDump(src);
                if (ModConfig.EnableAutoTranslate.Value) AutoTranslator.Enqueue(src);
            }
            return null;
        }

        private static string CacheResult(string source, string translated)
        {
            // UI text has a bounded vocabulary in normal play. Keep the cache
            // bounded as a guard against rapidly changing player-authored text.
            if (_resultCache.Count < 16384)
                _resultCache[source] = translated;
            return translated;
        }

        public static string GetPerformanceSnapshot()
        {
            return
                $"translation cache: {_resultCacheHits} hit(s), " +
                $"{_negativeCacheHits} cached miss(es), " +
                $"{_resultCache.Count} result(s), {_noHit.Count} miss(es); " +
                $"dynamic regex: {_regexEvaluations} evaluation(s), {_regexMatches} match(es).";
        }

        /// <summary>
        /// Translate a displayed TMP value, including multi-line controls that mix
        /// already translated lines with newly assigned English lines. Translate()
        /// deliberately rejects mixed CJK strings, so this method works line by
        /// line and leaves user names or other mixed single-line values untouched.
        /// </summary>
        public static string TranslateDisplayText(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;
            return TranslateDisplayText(source, ContainsCjk(source));
        }

        /// <summary>
        /// Translate display text when the caller has already checked whether it
        /// contains CJK. The text setter is a hot path, so this avoids scanning
        /// every assigned string twice.
        /// </summary>
        public static string TranslateDisplayText(
            string source,
            bool containsCjk)
        {
            if (string.IsNullOrEmpty(source)) return null;
            if (!containsCjk)
            {
                var direct = Translate(source);
                if (direct != null || source.IndexOf('\n') < 0) return direct;
            }
            else if (source.IndexOf('\n') < 0)
            {
                // Curated corrections may intentionally use an already-Chinese
                // string as their key. This repairs stale Auto.zh_CN output and
                // inconsistent built-in terms without sending mixed text through
                // the normal English translation path.
                if (_dict.TryGetValue(source, out var corrected) &&
                    !string.Equals(source, corrected, StringComparison.Ordinal))
                    return corrected;
                return null;
            }

            var lines = source.Split('\n');
            bool changed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0 || ContainsCjk(line)) continue;
                var translated = Translate(line);
                if (translated == null || translated == line) continue;
                lines[i] = translated;
                changed = true;
            }
            return changed ? string.Join("\n", lines) : null;
        }

        private static bool TryTranslateQuantityDisplay(string source, out string translated)
        {
            translated = null;
            var match = QuantityDisplay.Match(source);
            if (!match.Success) return false;
            var item = match.Groups["item"].Value;
            if (_preserveNames.Contains(item) || !_dict.TryGetValue(item, out var itemTranslation))
                return false;
            translated = match.Groups["count"].Value + "x " +
                         RestoreNames(item, itemTranslation);
            return true;
        }

        private static bool TryTranslateDecoratedEffect(string source, out string translated)
        {
            translated = null;
            var match = DecoratedEffect.Match(source);
            if (!match.Success) return false;
            if (!_effects.TryGetValue(match.Groups["term"].Value.Trim(), out var effect))
                return false;
            translated = match.Groups["prefix"].Value + effect + match.Groups["suffix"].Value;
            return true;
        }

        private static string RestoreNames(string source, string translated)
        {
            if (string.IsNullOrEmpty(translated) || _nameSources.Count == 0)
                return translated;

            var personRegion = PersonRegionLabel.Match(source);
            int targetColor = translated.IndexOf("<color", StringComparison.Ordinal);
            if (personRegion.Success && targetColor >= 0)
                translated = personRegion.Groups["name"].Value +
                             translated.Substring(targetColor);

            var words = CapitalizedWord.Matches(source);
            if (words.Count == 0) return translated;

            // Try the longest proper-name phrase first, then its individual words.
            // This turns 莫莉·普雷斯利 back into Molly Presley without disturbing
            // translated locations, items or ordinary sentence text.
            for (int wordCount = Math.Min(4, words.Count); wordCount >= 1; wordCount--)
            {
                for (int start = 0; start + wordCount <= words.Count; start++)
                {
                    var first = words[start];
                    var last = words[start + wordCount - 1];
                    int length = last.Index + last.Length - first.Index;
                    var candidate = source.Substring(first.Index, length);
                    if (candidate.IndexOf('\n') >= 0 || candidate.IndexOf('\r') >= 0)
                        continue;
                    var displayName = candidate;
                    if (!_nameSources.TryGetValue(candidate, out var localized))
                    {
                        if (candidate.EndsWith("'s", StringComparison.Ordinal) ||
                            candidate.EndsWith("’s", StringComparison.Ordinal))
                        {
                            displayName = candidate.Substring(0, candidate.Length - 2);
                            if (!_nameSources.TryGetValue(displayName, out localized))
                                continue;
                        }
                        else continue;
                    }
                    if (translated.IndexOf(localized, StringComparison.Ordinal) >= 0)
                        translated = translated.Replace(localized, displayName);
                    translated = SpaceNameBoundaries(translated, displayName);
                }
            }
            return translated;
        }

        private static string SpaceNameBoundaries(string text, string name)
        {
            int searchFrom = 0;
            while (searchFrom < text.Length)
            {
                int index = text.IndexOf(name, searchFrom, StringComparison.Ordinal);
                if (index < 0) break;
                if (index > 0 && IsCjk(text[index - 1]))
                {
                    text = text.Insert(index, " ");
                    index++;
                }
                int end = index + name.Length;
                if (end < text.Length && IsCjk(text[end]))
                    text = text.Insert(end, " ");
                searchFrom = end + 1;
            }
            return text;
        }

        private static bool IsCjk(char c)
        {
            return c >= '一' && c <= '鿿';
        }

        private static readonly HashSet<string> _disabledPatterns =
            new HashSet<string>(StringComparer.Ordinal);

        private static bool TryTranslatePattern(string source, out string translated)
        {
            translated = null;
            foreach (var entry in GetPatternCandidates(source))
            {
                if (_disabledPatterns.Contains(entry.Source)) continue;
                try
                {
                    _regexEvaluations++;
                    var match = entry.Regex.Match(source);
                    if (!match.Success) continue;
                    _regexMatches++;
                    translated = ExpandReplacement(entry.Replacement, match);
                    return !string.IsNullOrEmpty(translated);
                }
                catch (RegexMatchTimeoutException)
                {
                    if (_disabledPatterns.Add(entry.Source))
                        Plugin.Log.LogWarning(
                            $"Disabled timed-out translation rule: {entry.Source}");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"Dynamic translation rule failed '{entry.Source}': {ex.Message}");
                }
            }
            return false;
        }

        /// <summary>
        /// Expands .NET-style $1/${name}/$$ replacement tokens while also translating
        /// captured products, locations and NPC names through the exact dictionary.
        /// </summary>
        private static string ExpandReplacement(string replacement, Match match)
        {
            var sb = new StringBuilder(replacement.Length + 32);
            for (int i = 0; i < replacement.Length; i++)
            {
                var c = replacement[i];
                if (c != '$' || i + 1 >= replacement.Length)
                {
                    sb.Append(c);
                    continue;
                }

                var next = replacement[i + 1];
                if (next == '$')
                {
                    sb.Append('$');
                    i++;
                    continue;
                }

                string groupName = null;
                if (next == '{')
                {
                    int close = replacement.IndexOf('}', i + 2);
                    if (close > i + 2)
                    {
                        groupName = replacement.Substring(i + 2, close - i - 2);
                        i = close;
                    }
                }
                else if (next >= '0' && next <= '9')
                {
                    int end = i + 1;
                    while (end + 1 < replacement.Length &&
                           replacement[end + 1] >= '0' && replacement[end + 1] <= '9')
                        end++;
                    groupName = replacement.Substring(i + 1, end - i);
                    i = end;
                }

                if (groupName == null)
                {
                    sb.Append('$');
                    continue;
                }

                Group group;
                try { group = match.Groups[groupName]; }
                catch { group = null; }
                if (group != null && group.Success)
                    sb.Append(TranslateCapturedFragment(group.Value));
            }
            return sb.ToString();
        }

        private static string TranslateCapturedFragment(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (_preserveNames.Contains(value)) return value;
            if (_dict.TryGetValue(value, out var translated)) return RestoreNames(value, translated);

            var trimmed = value.Trim();
            if (_preserveNames.Contains(trimmed)) return value;
            if (_dict.TryGetValue(trimmed, out translated))
                return value.Substring(0, value.Length - value.TrimStart().Length) +
                       RestoreNames(trimmed, translated) +
                       value.Substring(value.TrimEnd().Length);

            // Product request captures often contain "2x Meth" as one group.
            var quantity = Regex.Match(trimmed, @"\A(\d+)x\s+(.+)\z");
            if (quantity.Success && _dict.TryGetValue(quantity.Groups[2].Value, out translated))
                return quantity.Groups[1].Value + " 份 " +
                       RestoreNames(quantity.Groups[2].Value, translated);

            return value;
        }

        public static void AddRuntime(string src, string translated)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(translated)) return;
            _noHit.Remove(src);
            _resultCache.Remove(src);
            _dict[src] = translated;
        }

        // ---------------- live component registry ----------------

        public static void RegisterLive(TMP_Text comp, string src)
        {
            if (comp == null) return;
            lock (_live)
            {
                if (!_live.TryGetValue(src, out var list))
                {
                    list = new List<WeakReference<TMP_Text>>();
                    _live[src] = list;
                }
                foreach (var wr in list)
                    if (wr.TryGetTarget(out var c) && ReferenceEquals(c, comp)) return;
                if (list.Count < 12) list.Add(new WeakReference<TMP_Text>(comp));
            }
        }

        /// <summary>Called on the main thread when an auto-translation arrived.</summary>
        public static void ApplyToLive(string src, string translated)
        {
            List<WeakReference<TMP_Text>> list;
            lock (_live)
            {
                if (!_live.TryGetValue(src, out list)) return;
                _live.Remove(src);
            }
            foreach (var wr in list)
            {
                try
                {
                    if (!wr.TryGetTarget(out var c) || c == null) continue;
                    if (c.text == src)
                    {
                        FontService.EnsureCjkFont(c);
                        c.text = translated;
                    }
                }
                catch { }
            }
        }

        public static void CleanupLive()
        {
            lock (_live)
            {
                var dead = new List<string>();
                foreach (var kv in _live)
                {
                    kv.Value.RemoveAll(wr => !wr.TryGetTarget(out var c) || c == null);
                    if (kv.Value.Count == 0) dead.Add(kv.Key);
                }
                foreach (var k in dead) _live.Remove(k);
                if (_live.Count > 4000) _live.Clear();
            }
        }

        // ---------------- dump ----------------

        private static void QueueDump(string src)
        {
            lock (_dumpPending)
            {
                if (_dumped.Add(src)) _dumpPending.Add(src);
            }
        }

        public static void FlushDumpIfDue()
        {
            if ((DateTime.UtcNow - _lastDumpFlush).TotalSeconds < 5) return;
            string[] batch;
            lock (_dumpPending)
            {
                if (_dumpPending.Count == 0) return;
                batch = _dumpPending.ToArray();
                _dumpPending.Clear();
            }
            try
            {
                using (var w = new StreamWriter(_dumpFile, true, Encoding.UTF8))
                    foreach (var s in batch)
                        w.WriteLine(Escape(s) + "=");
                _lastDumpFlush = DateTime.UtcNow;
            }
            catch { }
        }

        // ---------------- escaping ----------------

        public static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("=", "\\=")
                .Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static int FindSeparator(string s)
        {
            bool escaped = false;
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '=' && !escaped) return i;
                if (c == '\\' && !escaped) escaped = true;
                else escaped = false;
            }
            return -1;
        }

        public static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    var n = s[++i];
                    if (n == 'n') sb.Append('\n');
                    else if (n == 'r') sb.Append('\r');
                    else if (n == '\\') sb.Append('\\');
                    else if (n == '=') sb.Append('=');
                    else { sb.Append('\\'); sb.Append(n); }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
