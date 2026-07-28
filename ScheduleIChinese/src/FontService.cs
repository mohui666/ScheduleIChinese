using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace ScheduleIChinese
{
    /// <summary>
    /// Builds a dynamic CJK TMP font asset at runtime from a bundled font file and
    /// registers it as a global fallback so Chinese glyphs render everywhere.
    /// </summary>
    public static class FontService
    {
        private static TMP_FontAsset _cjkFont;
        private static TMP_FontAsset _notoFont;
        private static Font _legacyCjkFont;
        private static readonly HashSet<IntPtr> _patchedFontAssets = new HashSet<IntPtr>();
        private static int _initAttempts;
        private static float _nextInitAttempt;

        public static bool Ready => _cjkFont != null;

        /// <summary>Called every frame by MainThreadRunner until initialization succeeds.</summary>
        public static void Tick()
        {
            if (Ready || _initAttempts >= 120 || Time.unscaledTime < _nextInitAttempt) return;
            _nextInitAttempt = Time.unscaledTime + 0.5f;
            _initAttempts++;
            try { Init(); }
            catch (Exception e)
            {
                if (_initAttempts % 10 == 0)
                    Plugin.Log.LogWarning("font init retry: " + e.Message);
            }
        }

        private static void Init()
        {
            var initTimer = System.Diagnostics.Stopwatch.StartNew();
            if (_gameFont == null)
                _gameFont = FindGameFont();
            if (_gameFont != null && !_gameFontCompleted)
            {
                _gameFontCompleted = TryCompleteGameFont(_gameFont);
                if (_gameFontCompleted)
                    Plugin.Log.LogInfo("game font atlas completed with CJK glyphs");
            }
            if (_cjkFont == null)
            {
                _cjkFont = CreateNotoFont();
                _notoFont = _cjkFont;
                if (_cjkFont == null) { _initAttempts = 999; return; }
            }
            RegisterGlobalFallback();
            Plugin.Log.LogInfo(
                $"CJK font asset ready in {initTimer.ElapsedMilliseconds} ms.");
        }

        private static TMP_FontAsset _gameFont;

        /// <summary>Real game font asset used to bridge null-font labels.</summary>
        public static TMP_FontAsset GameFont => _gameFont;

        /// <summary>Complete-coverage CJK font asset (Noto, runtime-created).</summary>
        public static TMP_FontAsset CjkFont => _cjkFont;

        /// <summary>
        /// Find a real game font asset (OpenSans-SemiBold). Runtime-created font
        /// assets silently refuse direct component assignment in this IL2CPP
        /// runtime, while game-native assets stick; we therefore put a real game
        /// font on null-font labels and let its fallback table carry the CJK
        /// glyphs from our runtime font.
        /// </summary>
        private static bool _gameFontCompleted;

        /// <summary>
        /// Inject every CJK character our translations use into the game-native
        /// font asset's atlas. Game-native fonts are the only ones TMP accepts
        /// for direct component assignment in this runtime; once the atlas
        /// carries CJK, bridged labels render Chinese from the font itself with
        /// no fallback submeshes. APIs are touched via reflection since the
        /// exact interop surface varies.
        /// </summary>
        private static bool TryCompleteGameFont(TMP_FontAsset font)
        {
            try
            {
                var type = font.GetType();
                var pop = type.GetProperty("atlasPopulationMode");
                if (pop != null && pop.CanWrite)
                    pop.SetValue(font, (int)AtlasPopulationMode.Dynamic, null);
                foreach (var propName in new[] { "IsMultiAtlasEnabled", "multiAtlas" })
                {
                    try
                    {
                        var p = type.GetProperty(propName);
                        if (p != null && p.CanWrite) p.SetValue(font, true, null);
                    }
                    catch { }
                }

                var chars = TranslationStore.CollectCjkChars();
                if (string.IsNullOrEmpty(chars)) return false;

                System.Reflection.MethodInfo add = null;
                object addArgs = null;
                foreach (var m in type.GetMethods())
                {
                    if (m.Name != "TryAddCharacters") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1) continue;
                    Plugin.Log.LogInfo("TryAddCharacters overload: " + ps[0].ParameterType.FullName);
                    if (ps[0].ParameterType == typeof(string))
                    {
                        add = m;
                        addArgs = chars;
                        break;
                    }
                }
                if (add == null)
                {
                    Plugin.Log.LogWarning("game font: no TryAddCharacters(string) overload");
                    return false;
                }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool ok = add.Invoke(font, new object[] { addArgs }) is bool b && b;
                sw.Stop();
                Plugin.Log.LogInfo(
                    $"game font completion: {chars.Length} chars, ok={ok}, {sw.ElapsedMilliseconds} ms");
                return ok;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("game font completion failed: " + e.Message);
                return false;
            }
        }

        private static TMP_FontAsset FindGameFont()
        {
            try
            {
                TMP_FontAsset sdf = null;
                TMP_FontAsset openSans = null;
                foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (fa == null || fa.name == null) continue;
                    // Priority: exact OpenSans-SemiBold, its SDF variant, TMP
                    // settings default, then any OpenSans family member. Never
                    // return an unrelated (icon/digit) font.
                    if (fa.name.Equals("OpenSans-SemiBold", StringComparison.OrdinalIgnoreCase))
                        return fa;
                    if (fa.name.Equals("OpenSans-SemiBold SDF", StringComparison.OrdinalIgnoreCase))
                        sdf = fa;
                    else if (openSans == null &&
                             fa.name.IndexOf("OpenSans", StringComparison.OrdinalIgnoreCase) >= 0)
                        openSans = fa;
                }
                if (sdf != null) return sdf;
                try
                {
                    var def = TMP_Settings.defaultFontAsset;
                    if (def != null) return def;
                }
                catch { }
                return openSans;
            }
            catch { }
            return null;
        }

        private static TMP_FontAsset CreateNotoFont()
        {
            var path = Path.Combine(Plugin.DataDir, ModConfig.FontFile.Value);
            if (!File.Exists(path)) path = @"C:\Windows\Fonts\msyh.ttc";
            if (!File.Exists(path)) { Plugin.Log.LogError("no CJK font file found"); return null; }

            Plugin.Log.LogInfo("creating CJK font asset from " + path);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            // faceIndex 0, 72pt sampling, dynamic atlas with multi-atlas growth
            var asset = TMP_FontAsset.CreateFontAsset(path, 0, 72, 7, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (asset == null) throw new Exception("CreateFontAsset returned null");
            asset.hideFlags = HideFlags.HideAndDontSave;
            Plugin.Log.LogInfo(
                $"CJK font asset creation: {timer.ElapsedMilliseconds} ms.");
            return asset;
        }

        private static void RegisterGlobalFallback()
        {
            try
            {
                if (TMP_Settings.fallbackFontAssets == null)
                    TMP_Settings.fallbackFontAssets = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
                if (_cjkFont != null && !TMP_Settings.fallbackFontAssets.Contains(_cjkFont))
                    TMP_Settings.fallbackFontAssets.Add(_cjkFont);
                if (_notoFont != null && _notoFont != _cjkFont &&
                    !TMP_Settings.fallbackFontAssets.Contains(_notoFont))
                    TMP_Settings.fallbackFontAssets.Add(_notoFont);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("TMP_Settings fallback registration failed: " + e.Message);
            }
            // Components whose font field is null (shop listing name labels) are
            // laid out with TMP_Settings.defaultFontAsset; without a CJK fallback
            // on that default they render Chinese as blank.
            try
            {
                var def = TMP_Settings.defaultFontAsset;
                if (def != null)
                {
                    var table = def.fallbackFontAssetTable;
                    if (table == null)
                    {
                        table = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
                        def.fallbackFontAssetTable = table;
                    }
                    if (_cjkFont != null && !table.Contains(_cjkFont)) table.Add(_cjkFont);
                    if (_notoFont != null && _notoFont != _cjkFont && !table.Contains(_notoFont))
                        table.Add(_notoFont);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("default font fallback registration failed: " + e.Message);
            }
        }

        /// <summary>
        /// Ensure the component can render CJK. Screen-space UI relies on the
        /// fallback tables (original font kept, uniform look). World-space
        /// TextMeshPro (shop kiosks and other in-store screens) silently refuses
        /// the fallback chain for injected fonts, so there the font is assigned
        /// directly. Returns true when the font setup actually changed, meaning
        /// any already-built text mesh is stale and must be rebuilt.
        /// </summary>
        public static bool ApplyCjkFont(TMP_Text comp)
        {
            // Runtime-created font assets refuse direct assignment in this
            // runtime; EnsureCjkFont bridges null-font components to a real
            // game font and registers CJK fallbacks on every font table.
            return EnsureCjkFont(comp);
        }

        /// <summary>Make sure the given component can render CJK (per-font-asset fallback).</summary>
        public static bool EnsureCjkFont(TMP_Text comp)
        {
            if (!Ready || comp == null) return false;
            try
            {
                var font = comp.font;
                bool changed = false;
                if (font == null)
                {
                    // The game font may not be loaded at plugin init; retry
                    // discovery lazily instead of failing forever.
                    if (_gameFont == null)
                        _gameFont = FindGameFont();
                    if (_gameFont == null) return false;
                    comp.font = _gameFont;
                    font = _gameFont;
                    changed = true;
                }
                var key = font.Pointer;
                if (_patchedFontAssets.Contains(key)) return changed;

                var table = font.fallbackFontAssetTable;
                if (table == null)
                {
                    table = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
                    font.fallbackFontAssetTable = table;
                }
                if (_cjkFont != null && !table.Contains(_cjkFont)) table.Add(_cjkFont);
                if (_notoFont != null && _notoFont != _cjkFont && !table.Contains(_notoFont))
                    table.Add(_notoFont);
                _patchedFontAssets.Add(key);
                return true;
            }
            catch { }
            return false;
        }

        /// <summary>Dynamic OS font for legacy uGUI Text components.</summary>
        public static Font LegacyCjkFont
        {
            get
            {
                if (_legacyCjkFont == null)
                {
                    try
                    {
                        foreach (var name in new[] { "Microsoft YaHei", "Noto Sans SC", "SimSun" })
                        {
                            var f = Font.CreateDynamicFontFromOSFont(name, 16);
                            if (f != null && f.HasCharacter('中')) { _legacyCjkFont = f; break; }
                        }
                    }
                    catch { }
                }
                return _legacyCjkFont;
            }
        }
    }
}
