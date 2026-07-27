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
            var path = Path.Combine(Plugin.DataDir, ModConfig.FontFile.Value);
            if (!File.Exists(path)) path = @"C:\Windows\Fonts\msyh.ttc";
            if (!File.Exists(path)) { Plugin.Log.LogError("no CJK font file found"); _initAttempts = 999; return; }

            Plugin.Log.LogInfo("creating CJK font asset from " + path);
            // faceIndex 0, 72pt sampling, dynamic atlas with multi-atlas growth
            _cjkFont = TMP_FontAsset.CreateFontAsset(path, 0, 72, 7, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            if (_cjkFont == null) throw new Exception("CreateFontAsset returned null");
            _cjkFont.hideFlags = HideFlags.HideAndDontSave;

            // global fallback list in TMP settings -> consulted for every missing glyph
            try
            {
                if (TMP_Settings.fallbackFontAssets == null)
                    TMP_Settings.fallbackFontAssets = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
                if (!TMP_Settings.fallbackFontAssets.Contains(_cjkFont))
                    TMP_Settings.fallbackFontAssets.Add(_cjkFont);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("TMP_Settings fallback registration failed: " + e.Message);
            }

            Plugin.Log.LogInfo("CJK font asset ready.");
        }

        /// <summary>Make sure the given component can render CJK (per-font-asset fallback).</summary>
        public static void EnsureCjkFont(TMP_Text comp)
        {
            if (!Ready || comp == null) return;
            try
            {
                var font = comp.font;
                if (font == null) return;
                var key = font.Pointer;
                if (_patchedFontAssets.Contains(key)) return;
                _patchedFontAssets.Add(key);

                var table = font.fallbackFontAssetTable;
                if (table == null)
                {
                    table = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
                    font.fallbackFontAssetTable = table;
                }
                if (!table.Contains(_cjkFont)) table.Add(_cjkFont);
            }
            catch { }
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
