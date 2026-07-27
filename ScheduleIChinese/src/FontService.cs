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
            if (_gameFont == null)
                _gameFont = FindGameFont();
            if (_cjkFont == null)
            {
                _cjkFont = CreateNotoFont();
                _notoFont = _cjkFont;
                if (_cjkFont == null) { _initAttempts = 999; return; }
            }
            _forceAll = false;
            RegisterGlobalFallback();
            Plugin.Log.LogInfo("CJK font asset ready.");
        }

        private static bool _forceAll;

        private static TMP_FontAsset _gameFont;

        /// <summary>Real game font asset used to bridge null-font labels.</summary>
        public static TMP_FontAsset GameFont => _gameFont;

        /// <summary>
        /// Find a real game font asset (OpenSans-SemiBold). Runtime-created font
        /// assets silently refuse direct component assignment in this IL2CPP
        /// runtime, while game-native assets stick; we therefore put a real game
        /// font on null-font labels and let its fallback table carry the CJK
        /// glyphs from our runtime font.
        /// </summary>
        private static TMP_FontAsset FindGameFont()
        {
            try
            {
                TMP_FontAsset any = null;
                TMP_FontAsset sdf = null;
                foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (fa == null || fa.name == null) continue;
                    if (any == null) any = fa;
                    // The asset literally named "OpenSans-SemiBold" (no "SDF"
                    // suffix) is what the game's CJK-rendering components use;
                    // the "SDF"-suffixed variant does not render via fallback.
                    if (fa.name.Equals("OpenSans-SemiBold", StringComparison.OrdinalIgnoreCase))
                        return fa;
                    if (fa.name.Equals("OpenSans-SemiBold SDF", StringComparison.OrdinalIgnoreCase))
                        sdf = fa;
                }
                return sdf != null ? sdf : any;
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
            // faceIndex 0, 72pt sampling, dynamic atlas with multi-atlas growth
            var asset = TMP_FontAsset.CreateFontAsset(path, 0, 72, 7, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (asset == null) throw new Exception("CreateFontAsset returned null");
            asset.hideFlags = HideFlags.HideAndDontSave;
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

        private static int _fontAssignFails;

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
