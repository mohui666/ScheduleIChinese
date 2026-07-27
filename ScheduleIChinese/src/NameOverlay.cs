using System;
using TMPro;
using UnityEngine;

namespace ScheduleIChinese
{
    /// <summary>
    /// 为无法正常渲染 CJK 的商店商品名创建独立覆盖文本。
    /// </summary>
    public static class NameOverlay
    {
        private const string OverlayName = "SIC_NameOverlay";

        public static bool IsShopNameLabel(TMP_Text comp)
        {
            try
            {
                if (comp == null || comp.name != "Name")
                    return false;

                var t = comp.transform;

                for (int i = 0; i < 10 && t != null; i++)
                {
                    if (t.name.IndexOf(
                            "ShopEntry",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }

                    t = t.parent;
                }
            }
            catch
            {
                // Ignore destroyed IL2CPP objects.
            }

            return false;
        }

        /// <summary>
        /// text 必须是完成翻译后的最终文本，不能重新读取 label.text。
        /// </summary>
        public static void Sync(TMP_Text label, string text)
        {
            if (label == null)
                return;

            try
            {
                var overlayTransform = label.transform.Find(OverlayName);
                var overlay = overlayTransform != null
                    ? overlayTransform.GetComponent<TextMeshProUGUI>()
                    : null;

                bool needsCjk =
                    !string.IsNullOrEmpty(text) &&
                    TranslationStore.ContainsCjk(text);

                // 商品格被复用成英文内容时，必须清理旧中文。
                if (!needsCjk)
                {
                    if (overlay != null)
                    {
                        overlay.text = string.Empty;
                        overlay.gameObject.SetActive(false);
                    }

                    return;
                }

                // 字体尚未初始化时不要创建一个永久空字体组件。
                // 后续滚动扫描会再次调用 Sync。
                if (!FontService.Ready)
                    return;

                if (overlay == null)
                    overlay = Create(label);

                if (overlay == null)
                    return;

                if (!overlay.gameObject.activeSelf)
                    overlay.gameObject.SetActive(true);

                CopyVisualStyle(label, overlay);

                // 只在文本或字体真的变化时重建，避免每帧强制 Canvas 重排。
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
                Plugin.Log?.LogWarning(
                    "shop name overlay sync failed: " + e);
            }
        }

        private static TextMeshProUGUI Create(TMP_Text label)
        {
            try
            {
                /*
                 * 在 IL2CPP 下 GameObject 的 Type[] 构造函数需要
                 * Il2CppReferenceArray<Type>，逐个 AddComponent 也可以，
                 * 关键是 CanvasRenderer 必须存在，否则 TMP 不渲染。
                 */
                var go = new GameObject(OverlayName);
                go.layer = label.gameObject.layer;
                go.hideFlags = HideFlags.DontSave;

                var rect = go.AddComponent<RectTransform>();
                go.AddComponent<CanvasRenderer>();
                var overlay = go.AddComponent<TextMeshProUGUI>();

                // Overlay 是原标签的子对象，所以直接铺满原标签。
                rect.SetParent(label.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localPosition = Vector3.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                rect.SetAsLastSibling();

                overlay.raycastTarget = false;

                var gameFont = FontService.GameFont;
                if (gameFont != null)
                    overlay.font = gameFont;

                CopyVisualStyle(label, overlay);

                Plugin.Log?.LogInfo(
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

        private static void CopyVisualStyle(
            TMP_Text source,
            TextMeshProUGUI destination)
        {
            destination.fontSize = source.fontSize;
            destination.fontStyle = source.fontStyle;
            destination.enableAutoSizing = source.enableAutoSizing;
            destination.fontSizeMin = source.fontSizeMin;
            destination.fontSizeMax = source.fontSizeMax;

            destination.color = source.color;
            destination.alignment = source.alignment;
            destination.overflowMode = source.overflowMode;
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
