using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;

namespace ScheduleIChinese
{
    /// <summary>
    /// 商店商品名覆盖层：原标签字体为空且无法渲染 CJK，
    /// 用同级 TextMeshProUGUI 覆盖显示中文。
    /// 1.3.44 已证实可显示；本版只修重复创建问题。
    /// </summary>
    public static class NameOverlay
    {
        private const string OverlayName = "SIC_NameOverlay";
        private static readonly Dictionary<int, WeakReference<TextMeshProUGUI>> OverlayByLabelId =
            new Dictionary<int, WeakReference<TextMeshProUGUI>>();

        public static bool IsShopNameLabel(TMP_Text comp)
        {
            try
            {
                if (comp == null || comp.name != "Name")
                    return false;

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

        public static void Sync(TMP_Text label, string text)
        {
            if (label == null)
                return;

            try
            {
                // Find the overlay as sibling (current) or child (legacy);
                // the 1.3.44 bug searched only children of the label, so a new
                // overlay was created on every sync.
                TextMeshProUGUI overlay = GetCached(label);
                var parent = label.transform.parent;
                if (overlay == null && parent != null)
                {
                    var tf = parent.Find(OverlayName);
                    if (tf != null) overlay = tf.GetComponent<TextMeshProUGUI>();
                }
                if (overlay == null)
                {
                    var tf = label.transform.Find(OverlayName);
                    if (tf != null) overlay = tf.GetComponent<TextMeshProUGUI>();
                }
                if (overlay != null)
                    Cache(label, overlay);

                bool needsCjk =
                    !string.IsNullOrEmpty(text) &&
                    TranslationStore.ContainsCjk(text);

                if (!needsCjk)
                {
                    if (overlay != null)
                    {
                        overlay.text = string.Empty;
                        overlay.gameObject.SetActive(false);
                    }
                    return;
                }

                if (!FontService.Ready)
                    return;

                if (overlay == null)
                    overlay = Create(label);
                if (overlay == null)
                    return;

                if (!overlay.gameObject.activeSelf)
                    overlay.gameObject.SetActive(true);

                CopyVisualStyle(label, overlay);

                bool dirty = false;
                if (FontService.EnsureCjkFont(overlay))
                    dirty = true;

                if (overlay.text != text)
                {
                    overlay.text = text;
                    dirty = true;
                }

                if (dirty)
                {
                    overlay.havePropertiesChanged = true;
                    overlay.SetVerticesDirty();
                    overlay.SetLayoutDirty();
                }
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning("shop name overlay sync failed: " + e);
            }
        }

        private static TextMeshProUGUI Create(TMP_Text label)
        {
            try
            {
                var componentTypes = new Il2CppReferenceArray<Il2CppSystem.Type>(1);
                componentTypes[0] = Il2CppType.Of<RectTransform>();
                var go = new GameObject(OverlayName, componentTypes);

                go.layer = label.gameObject.layer;
                go.hideFlags = HideFlags.DontSave;

                var rect = go.GetComponent<RectTransform>();
                if (rect == null)
                    throw new InvalidOperationException("Overlay GameObject has no RectTransform.");

                // Child of the label, filling its rect: the rect position is
                // the only reliable anchor (its size is degenerate, but TMP
                // draws overflowing text anyway). This keeps names exactly
                // where the original layout put them.
                rect.SetParent(label.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                rect.SetAsLastSibling();

                var renderer = go.AddComponent<CanvasRenderer>();
                if (renderer == null)
                    throw new InvalidOperationException("Failed to create CanvasRenderer.");

                var overlay = go.AddComponent<TextMeshProUGUI>();
                if (overlay == null)
                    throw new InvalidOperationException("Failed to create TextMeshProUGUI.");

                overlay.raycastTarget = false;
                overlay.maskable = false;
                overlay.overflowMode = TextOverflowModes.Overflow;

                var cjkFont = FontService.CjkFont;
                if (cjkFont != null)
                    overlay.font = cjkFont;
                else
                {
                    var gameFont = FontService.GameFont;
                    if (gameFont != null) overlay.font = gameFont;
                }

                CopyVisualStyle(label, overlay);

                Cache(label, overlay);
                Plugin.Log?.LogDebug(
                    "shop name overlay created for " +
                    GetTransformPath(label.transform));

                return overlay;
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning(
                    "shop name overlay create failed: " + e);

                return null;
            }
        }

        private static TextMeshProUGUI GetCached(TMP_Text label)
        {
            int id;
            try { id = label.GetInstanceID(); }
            catch { return null; }

            if (!OverlayByLabelId.TryGetValue(id, out var weak))
                return null;

            try
            {
                if (weak.TryGetTarget(out var overlay) &&
                    overlay != null &&
                    overlay.gameObject != null)
                    return overlay;
            }
            catch { }

            OverlayByLabelId.Remove(id);
            return null;
        }

        private static void Cache(TMP_Text label, TextMeshProUGUI overlay)
        {
            try
            {
                OverlayByLabelId[label.GetInstanceID()] =
                    new WeakReference<TextMeshProUGUI>(overlay);
            }
            catch { }
        }

        private static void CopyVisualStyle(TMP_Text source, TMP_Text destination)
        {
            destination.fontSize = source.fontSize;
            destination.fontStyle = source.fontStyle;
            destination.enableAutoSizing = source.enableAutoSizing;
            destination.fontSizeMin = source.fontSizeMin;
            destination.fontSizeMax = source.fontSizeMax;

            destination.color = source.color;
            destination.alignment = source.alignment;
            destination.overflowMode = TextOverflowModes.Overflow;
            destination.richText = source.richText;
            destination.margin = source.margin;

            destination.characterSpacing = source.characterSpacing;
            destination.wordSpacing = source.wordSpacing;
            destination.lineSpacing = source.lineSpacing;
            destination.paragraphSpacing = source.paragraphSpacing;
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
                return "<null>";

            string path = transform.name;
            var parent = transform.parent;

            for (int i = 0; i < 20 && parent != null; i++)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }
    }
}
