using System;
using TMPro;
using UnityEngine;

namespace ScheduleIChinese
{
    /// <summary>
    /// Shop listing name labels ship with a null font and refuse every font
    /// assignment path we tried (runtime font assets, fallback tables, mesh
    /// rebuilds). Instead of fighting that, mirror the label with our own
    /// overlay text component that we fully control: a real game font plus the
    /// registered CJK fallback renders the translation exactly like other UI.
    /// </summary>
    public static class NameOverlay
    {
        private const string OverlayName = "SIC_NameOverlay";

        public static bool IsShopNameLabel(TMP_Text comp)
        {
            try
            {
                if (comp == null || comp.name != "Name") return false;
                var t = comp.transform;
                for (int i = 0; i < 10 && t != null; i++)
                {
                    if (t.name.IndexOf("ShopEntry", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                    t = t.parent;
                }
            }
            catch { }
            return false;
        }

        public static void Sync(TMP_Text label)
        {
            if (label == null) return;
            try
            {
                string text = label.text;
                bool needCjk = !string.IsNullOrEmpty(text) && TranslationStore.ContainsCjk(text);

                var overlayTf = label.transform.Find(OverlayName);
                TextMeshProUGUI overlay = null;
                if (overlayTf != null) overlay = overlayTf.GetComponent<TextMeshProUGUI>();

                if (!needCjk)
                {
                    if (overlay != null && overlay.text.Length > 0) overlay.text = "";
                    return;
                }

                if (overlay == null) overlay = Create(label);
                if (overlay == null) return;
                if (overlay.text != text) overlay.text = text;
                FontService.EnsureCjkFont(overlay);
            }
            catch { }
        }

        private static TextMeshProUGUI Create(TMP_Text label)
        {
            try
            {
                var go = new GameObject(OverlayName);
                go.transform.SetParent(label.transform, false);
                var dst = go.AddComponent<RectTransform>();
                var src = (RectTransform)label.transform;
                dst.pivot = src.pivot;
                dst.anchorMin = src.anchorMin;
                dst.anchorMax = src.anchorMax;
                dst.anchoredPosition = src.anchoredPosition;
                dst.sizeDelta = src.sizeDelta;
                dst.localRotation = src.localRotation;
                dst.localScale = src.localScale;

                var overlay = go.AddComponent<TextMeshProUGUI>();
                var gameFont = FontService.GameFont;
                if (gameFont != null) overlay.font = gameFont;
                overlay.fontSize = label.fontSize;
                overlay.enableAutoSizing = label.enableAutoSizing;
                overlay.fontSizeMin = label.fontSizeMin;
                overlay.fontSizeMax = label.fontSizeMax;
                overlay.color = label.color;
                overlay.alignment = label.alignment;
                overlay.overflowMode = label.overflowMode;
                overlay.raycastTarget = false;
                return overlay;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("name overlay create failed: " + e.Message);
                return null;
            }
        }
    }
}
