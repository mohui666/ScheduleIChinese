using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using ScheduleOne.UI;
using TMPro;
using UnityEngine;

namespace ScheduleIChinese
{
    /// <summary>
    /// Observes TMP text changes and component activation, then applies Chinese
    /// translations on the main thread.
    /// </summary>
    public static class TextPatch
    {
        private static readonly object PendingLock = new object();
        private static readonly Queue<PendingText> PendingChanges =
            new Queue<PendingText>();
        private static readonly HashSet<int> PendingInstanceIds = new HashSet<int>();
        private static readonly HashSet<int> _nullFontRebuilt = new HashSet<int>();
        private static readonly Dictionary<int, FaceContextEntry> FaceContextById =
            new Dictionary<int, FaceContextEntry>();
        private static readonly Dictionary<int, WeakReference<TMP_Text>> ArcadeControlById =
            new Dictionary<int, WeakReference<TMP_Text>>();
        private static readonly Dictionary<int, SafeUiContextEntry> SafeUiContextById =
            new Dictionary<int, SafeUiContextEntry>();
        private static Il2CppSystem.Action<UnityEngine.Object> _textChangedHandler;

        private sealed class PendingText
        {
            public int InstanceId;
            public WeakReference<TMP_Text> Component;
        }

        private sealed class FaceContextEntry
        {
            public WeakReference<TMP_Text> Component;
        }

        [Flags]
        private enum SafeUiContext
        {
            None = 0,
            CharacterCreator = 1,
            Phone = 2,
            ItemQuality = 4,
            Hotbar = 8,
            InputPrompt = 16,
            CustomerStandard = 32,
            Settings = 64
        }

        private sealed class SafeUiContextEntry
        {
            public WeakReference<TMP_Text> Component;
            public SafeUiContext Contexts;
        }

        /// <summary>
        /// TMP assignments performed entirely inside IL2CPP can bypass Harmony's
        /// managed setter bridge. TMP's own change event still observes them.
        /// Queue the component and translate it on the next frame to avoid
        /// mutating text re-entrantly while TMP is dispatching the event.
        /// </summary>
        public static void InitializeChangeListener()
        {
            if (_textChangedHandler != null) return;
            _textChangedHandler = (System.Action<UnityEngine.Object>)OnTextChanged;
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(_textChangedHandler);
            Plugin.Log.LogInfo("TMP text-change listener enabled.");
        }

        private static void OnTextChanged(UnityEngine.Object changed)
        {
            var comp = changed as TMP_Text;
            if (comp == null) return;
            try
            {
                MainThreadRunner.RegisterText(comp);
                int id = comp.GetInstanceID();
                lock (PendingLock)
                {
                    if (PendingChanges.Count >= 2048 || !PendingInstanceIds.Add(id))
                        return;
                    PendingChanges.Enqueue(new PendingText
                    {
                        InstanceId = id,
                        Component = new WeakReference<TMP_Text>(comp)
                    });
                }
            }
            catch { }
        }

        public static void ApplyPendingChanges(
            int maxItems = 128,
            double maxMilliseconds = 1.5)
        {
            long deadline = Stopwatch.GetTimestamp() +
                (long)(maxMilliseconds * Stopwatch.Frequency / 1000d);

            for (int i = 0; i < maxItems; i++)
            {
                PendingText pending;
                lock (PendingLock)
                {
                    if (PendingChanges.Count == 0) return;
                    pending = PendingChanges.Dequeue();
                    PendingInstanceIds.Remove(pending.InstanceId);
                }

                TMP_Text comp = null;
                try
                {
                    pending.Component.TryGetTarget(out comp);
                }
                catch { }

                if (comp != null) ApplyExisting(comp);

                // Checking the clock in batches keeps the budget itself cheap.
                if ((i & 7) == 7 && Stopwatch.GetTimestamp() >= deadline)
                    return;
            }
        }

        private static void MarkDirty(TMP_Text comp)
        {
            try { comp.havePropertiesChanged = true; } catch { }
        }

        private static string TranslateForComponent(
            TMP_Text comp,
            string source,
            bool sourceContainsCjk)
        {
            var contextual = TranslateContextual(comp, source);
            if (contextual != null) return contextual;
            return TranslationStore.TranslateDisplayText(
                source,
                sourceContainsCjk);
        }

        private static string TranslateForComponent(
            UnityEngine.UI.Text comp,
            string source,
            bool sourceContainsCjk)
        {
            var contextual = TranslateLegacyContextual(comp, source);
            if (contextual != null) return contextual;
            return TranslationStore.TranslateDisplayText(
                source,
                sourceContainsCjk);
        }

        private static string TranslateLegacyContextual(
            UnityEngine.UI.Text comp,
            string source)
        {
            if (comp == null || string.IsNullOrEmpty(source)) return null;

            if (StorefrontTranslations.TryGet(source, out var storefront))
                return storefront;

            if (HasLegacyUiContext(comp, SafeUiContext.CustomerStandard))
            {
                switch (source)
                {
                    case "Very Low": return "非常低";
                    case "Low": return "低";
                    case "Moderate": return "中等";
                    case "High": return "高";
                    case "Very High": return "非常高";
                }
            }

            if (source == "Benzies" &&
                HasLegacyUiContext(comp, SafeUiContext.Phone))
                return "本齐帮";

            return null;
        }

        private static bool HasLegacyUiContext(
            UnityEngine.UI.Text comp,
            SafeUiContext expected)
        {
            try
            {
                var ancestor = comp.transform;
                for (int depth = 0; depth < 12 && ancestor != null; depth++)
                {
                    var name = ancestor.name ?? string.Empty;
                    if (expected == SafeUiContext.CustomerStandard &&
                        (string.Equals(
                             name,
                             "StandardIItem",
                             StringComparison.OrdinalIgnoreCase) ||
                         ContainsIgnoreCase(name, "StandardContainer") ||
                         ContainsIgnoreCase(name, "CustomerStandard")))
                        return true;

                    if (expected == SafeUiContext.Phone &&
                        (ContainsIgnoreCase(name, "Phone") ||
                         ContainsIgnoreCase(name, "AppsCanvas") ||
                         ContainsIgnoreCase(name, "ContactsApp")))
                        return true;

                    ancestor = ancestor.parent;
                }
            }
            catch { }
            return false;
        }

        private static string TranslateContextual(TMP_Text comp, string source)
        {
            if (StorefrontTranslations.TryGet(source, out var storefront))
                return storefront;

            if ((string.Equals(source, "Jump", StringComparison.Ordinal) ||
                 string.Equals(source, "Drop", StringComparison.Ordinal)) &&
                IsArcadeControlLabel(comp))
                return source == "Jump" ? "跳跃" : "下落";

            var safeLabel = TranslateSafeUiLabel(comp, source);
            if (safeLabel != null) return safeLabel;

            // "Face" is an appearance enum value and therefore deliberately
            // remains on the global denylist. It is safe to localize only the
            // visible tattoo-shop category label.
            if (!string.Equals(source, "Face", StringComparison.Ordinal) ||
                comp == null)
                return null;

            try
            {
                int id = comp.GetInstanceID();
                if (FaceContextById.TryGetValue(id, out var cached))
                {
                    if (cached.Component.TryGetTarget(out var cachedComp) &&
                        cachedComp != null &&
                        cachedComp.gameObject != null)
                        return "面部";
                    FaceContextById.Remove(id);
                }

                bool isTattooCategory = false;
                var ancestor = comp.transform;
                for (int depth = 0; depth < 8 && ancestor != null; depth++)
                {
                    if (ancestor.name.IndexOf(
                            "Tattoo",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isTattooCategory = true;
                        break;
                    }

                    int categoryMatches = 0;
                    foreach (var label in ancestor.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (label == null) continue;
                        var text = label.text;
                        if (text == "Tattoo Shop" || text == "纹身店" ||
                            text == "Chest" || text == "胸部" ||
                            text == "Left Arm" || text == "左臂" ||
                            text == "Right Arm" || text == "右臂")
                            categoryMatches++;
                        if (categoryMatches >= 2)
                        {
                            isTattooCategory = true;
                            break;
                        }
                    }
                    if (isTattooCategory) break;

                    ancestor = ancestor.parent;
                }

                // Cache only a positive match. The first assignment can happen
                // before the tattoo panel and its sibling labels are assembled;
                // caching that early negative would make the English label stick.
                if (isTattooCategory)
                    FaceContextById[id] = new FaceContextEntry
                    {
                        Component = new WeakReference<TMP_Text>(comp)
                    };
                return isTattooCategory ? "面部" : null;
            }
            catch { }
            return null;
        }

        private static string TranslateSafeUiLabel(TMP_Text comp, string source)
        {
            if (comp == null || string.IsNullOrEmpty(source)) return null;

            switch (source)
            {
                case "Phone":
                    return HasSafeUiContext(comp, SafeUiContext.Phone)
                        ? "手机" : null;
                case "Character":
                    return HasSafeUiContext(comp, SafeUiContext.Phone)
                        ? "角色" : null;
                case "Yes":
                    return HasSafeUiContext(comp, SafeUiContext.Phone)
                        ? "接受" : null;
                case "Benzies":
                    return HasSafeUiContext(comp, SafeUiContext.Phone)
                        ? "本齐帮" : null;
                case "Gamepad":
                    return HasSafeUiContext(comp, SafeUiContext.Settings)
                        ? "手柄" : null;
                case "Imperial":
                    return HasSafeUiContext(comp, SafeUiContext.Settings)
                        ? "英制" : null;
                case "Metric":
                    return HasSafeUiContext(comp, SafeUiContext.Settings)
                        ? "公制" : null;
                case "Mouse":
                    return HasSafeUiContext(comp, SafeUiContext.Settings)
                        ? "鼠标" : null;
                case "Auto":
                    return HasSafeUiContext(comp, SafeUiContext.Settings)
                        ? "自动" : null;
                case "None":
                    return HasSafeUiContext(comp, SafeUiContext.Settings)
                        ? "无" : null;
                case "Normal":
                    return HasSafeUiContext(comp, SafeUiContext.Settings)
                        ? "标准" : null;
                case "On":
                    return HasSafeUiContext(comp, SafeUiContext.Settings)
                        ? "开" : null;
                case "Off":
                    return HasSafeUiContext(comp, SafeUiContext.Settings)
                        ? "关" : null;
                case "[Counter-offer]":
                    return HasSafeUiContext(comp, SafeUiContext.Phone)
                        ? "还价" : null;
                case "Back":
                    return HasSafeUiContext(comp, SafeUiContext.CharacterCreator) ||
                           HasSafeUiContext(comp, SafeUiContext.Phone) ||
                           HasSafeUiContext(comp, SafeUiContext.InputPrompt)
                        ? "返回" : null;
                case "Body":
                    return HasSafeUiContext(comp, SafeUiContext.CharacterCreator)
                        ? "身体" : null;
                case "Hair":
                    return HasSafeUiContext(comp, SafeUiContext.CharacterCreator)
                        ? "发型" : null;
                case "Eyebrows":
                    return HasSafeUiContext(comp, SafeUiContext.CharacterCreator)
                        ? "眉毛" : null;
                case "Eyes":
                    return HasSafeUiContext(comp, SafeUiContext.CharacterCreator)
                        ? "眼睛" : null;
                case "Clothing":
                    return HasSafeUiContext(comp, SafeUiContext.CharacterCreator)
                        ? "服装" : null;
                case "Top":
                    return HasSafeUiContext(comp, SafeUiContext.CharacterCreator)
                        ? "上装" : null;
                case "Cash":
                    return HasSafeUiContext(comp, SafeUiContext.Hotbar)
                        ? "现金" : null;
                case "Trash":
                    return HasSafeUiContext(comp, SafeUiContext.ItemQuality)
                        ? "垃圾" : null;
                case "Poor":
                    return HasSafeUiContext(comp, SafeUiContext.ItemQuality)
                        ? "劣质" : null;
                case "Standard":
                    return HasSafeUiContext(comp, SafeUiContext.ItemQuality)
                        ? "标准" : null;
                case "Premium":
                    return HasSafeUiContext(comp, SafeUiContext.ItemQuality)
                        ? "优质" : null;
                case "Heavenly":
                    return HasSafeUiContext(comp, SafeUiContext.ItemQuality)
                        ? "极品" : null;
                case "Very Low":
                    return HasSafeUiContext(comp, SafeUiContext.CustomerStandard)
                        ? "非常低" : null;
                case "Low":
                    return HasSafeUiContext(comp, SafeUiContext.CustomerStandard)
                        ? "低" : null;
                case "Moderate":
                    return HasSafeUiContext(comp, SafeUiContext.CustomerStandard)
                        ? "中等" : null;
                case "High":
                    return HasSafeUiContext(comp, SafeUiContext.CustomerStandard)
                        ? "高" : null;
                case "Very High":
                    return HasSafeUiContext(comp, SafeUiContext.CustomerStandard)
                        ? "非常高" : null;
                default:
                    return null;
            }
        }

        private static bool HasSafeUiContext(
            TMP_Text comp,
            SafeUiContext expected)
        {
            try
            {
                int id = comp.GetInstanceID();
                if (SafeUiContextById.TryGetValue(id, out var cached))
                {
                    if (cached.Component.TryGetTarget(out var cachedComp) &&
                        cachedComp != null &&
                        cachedComp.gameObject != null)
                    {
                        if ((cached.Contexts & expected) != 0)
                            return true;
                    }
                    else
                    {
                        SafeUiContextById.Remove(id);
                    }
                }

                bool matched = DetectSafeUiContext(comp, expected);
                if (!matched) return false;

                if (!SafeUiContextById.TryGetValue(id, out cached))
                {
                    cached = new SafeUiContextEntry
                    {
                        Component = new WeakReference<TMP_Text>(comp)
                    };
                    SafeUiContextById[id] = cached;
                }
                cached.Contexts |= expected;
                return true;
            }
            catch { }
            return false;
        }

        private static bool DetectSafeUiContext(
            TMP_Text comp,
            SafeUiContext expected)
        {
            var ancestor = comp.transform;
            for (int depth = 0; depth < 9 && ancestor != null; depth++)
            {
                var name = ancestor.name ?? string.Empty;
                if (expected == SafeUiContext.CharacterCreator &&
                    (ContainsIgnoreCase(name, "CharacterCreator") ||
                     ContainsIgnoreCase(name, "Character Creator") ||
                     ContainsIgnoreCase(name, "CharacterCreation") ||
                     ContainsIgnoreCase(name, "AppearanceCreator")))
                    return true;

                if (expected == SafeUiContext.Phone &&
                    (ContainsIgnoreCase(name, "Phone") ||
                     ContainsIgnoreCase(name, "AppsCanvas")))
                    return true;

                if (expected == SafeUiContext.ItemQuality &&
                    (ContainsIgnoreCase(name, "QualityLabel") ||
                     ContainsIgnoreCase(name, "QualityUI") ||
                     ContainsIgnoreCase(name, "ProductQuality")))
                    return true;

                if (expected == SafeUiContext.CustomerStandard &&
                    (string.Equals(name, "Standards", StringComparison.OrdinalIgnoreCase) ||
                     ContainsIgnoreCase(name, "StandardsLabel") ||
                     ContainsIgnoreCase(name, "CustomerStandard") ||
                     ContainsIgnoreCase(name, "CustomerRequirement")))
                    return true;

                if (expected == SafeUiContext.Hotbar &&
                    (ContainsIgnoreCase(name, "Hotbar") ||
                     string.Equals(name, "Cash", StringComparison.OrdinalIgnoreCase) ||
                     ContainsIgnoreCase(name, "CashSlot") ||
                     ContainsIgnoreCase(name, "CashDisplay")))
                    return true;

                if (expected == SafeUiContext.InputPrompt &&
                    (ContainsIgnoreCase(name, "InputPrompt") ||
                     ContainsIgnoreCase(name, "ControlPrompt") ||
                     ContainsIgnoreCase(name, "ActionPrompt") ||
                     ContainsIgnoreCase(name, "BindingDisplay")))
                    return true;

                if (expected == SafeUiContext.Settings &&
                    (ContainsIgnoreCase(name, "Settings") ||
                     ContainsIgnoreCase(name, "OptionsMenu") ||
                     ContainsIgnoreCase(name, "Options Menu")))
                    return true;

                ancestor = ancestor.parent;
            }

            // Prefab object names vary between game builds. Fall back to a
            // distinctive set of visible sibling labels, caching only a
            // positive match so partially assembled panels can be retried.
            ancestor = comp.transform;
            for (int depth = 0; depth < 6 && ancestor != null; depth++)
            {
                bool first = false;
                bool second = false;
                foreach (var label in ancestor.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label == null) continue;
                    var text = label.text;
                    if (expected == SafeUiContext.CharacterCreator)
                    {
                        if (text == "Customize Appearance" || text == "自定义外观")
                            first = true;
                        else if (text == "Next" || text == "下一个")
                            second = true;
                    }
                    else if (expected == SafeUiContext.Phone)
                    {
                        if (text == "Phone" || text == "手机")
                            first = true;
                        else if (text == "Character" || text == "角色")
                            second = true;
                    }
                    else if (expected == SafeUiContext.ItemQuality)
                    {
                        if (text == "Quality" || text == "品质")
                            first = true;
                        else if (text == "Trash" || text == "Poor" ||
                                 text == "Standard" || text == "Premium" ||
                                 text == "Heavenly")
                            second = true;
                    }
                    else if (expected == SafeUiContext.CustomerStandard)
                    {
                        if (text == "Standards" || text == "标准要求")
                            first = true;
                        else if (text == "Very Low" || text == "Low" ||
                                 text == "Moderate" || text == "High" ||
                                 text == "Very High" ||
                                 text == "非常低" || text == "低" ||
                                 text == "中等" || text == "高" ||
                                 text == "非常高")
                            second = true;
                    }
                    else if (expected == SafeUiContext.Hotbar)
                    {
                        if (text == "Cash" || text == "现金")
                            first = true;
                        else if (text == "$0")
                            second = true;
                    }
                    else if (expected == SafeUiContext.InputPrompt)
                    {
                        if (text == "Back" || text == "返回")
                            first = true;
                        else if (text == "Close" || text == "关闭" ||
                                 text == "Continue" || text == "继续")
                            second = true;
                    }
                    else if (expected == SafeUiContext.Settings)
                    {
                        if (text == "Settings" || text == "设置")
                            first = true;
                        else if (text == "Display" || text == "显示" ||
                                 text == "Graphics" || text == "图像" ||
                                 text == "Controls" || text == "控制")
                            second = true;
                    }

                    if (first && second) return true;
                }
                ancestor = ancestor.parent;
            }
            return false;
        }

        private static bool ContainsIgnoreCase(string value, string fragment)
        {
            return value.IndexOf(
                fragment,
                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsArcadeControlLabel(TMP_Text comp)
        {
            if (comp == null) return false;
            try
            {
                int id = comp.GetInstanceID();
                if (ArcadeControlById.TryGetValue(id, out var cached))
                {
                    if (cached.TryGetTarget(out var cachedComp) &&
                        cachedComp != null &&
                        cachedComp.gameObject != null)
                        return true;
                    ArcadeControlById.Remove(id);
                }

                bool isArcadeControl = false;
                var ancestor = comp.transform;
                for (int depth = 0; depth < 8 && ancestor != null; depth++)
                {
                    var ancestorName = ancestor.name;
                    if (ancestorName.IndexOf(
                            "Arcade",
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                        ancestorName.IndexOf(
                            "Minigame",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isArcadeControl = true;
                        break;
                    }

                    bool hasSpace = false;
                    bool hasCtrl = false;
                    bool hasScore = false;
                    foreach (var label in ancestor.GetComponentsInChildren<TMP_Text>(true))
                    {
                        if (label == null) continue;
                        var text = label.text;
                        if (text == "Space") hasSpace = true;
                        else if (text == "Ctrl") hasCtrl = true;
                        else if (text == "High Score" || text == "HIGH SCORE" ||
                                 text == "最高分" || text == "最高分数" ||
                                 text == "Score" || text == "SCORE" ||
                                 text == "分数")
                            hasScore = true;

                        if (hasSpace && hasCtrl && hasScore)
                        {
                            isArcadeControl = true;
                            break;
                        }
                    }
                    if (isArcadeControl) break;
                    ancestor = ancestor.parent;
                }

                if (isArcadeControl)
                    ArcadeControlById[id] =
                        new WeakReference<TMP_Text>(comp);
                return isArcadeControl;
            }
            catch { }
            return false;
        }

        private static void TransformImmediate(TMP_Text comp, ref string value)
        {
            if (comp == null || string.IsNullOrEmpty(value))
                return;

            bool hasCjk = TranslationStore.ContainsCjk(value);
            if (ModConfig.EnableRuntimeTranslationFallback.Value)
            {
                var translated = TranslateForComponent(comp, value, hasCjk);
                if (translated != null)
                {
                    value = translated;
                    hasCjk = TranslationStore.ContainsCjk(value);
                }
                else if (!hasCjk &&
                         ModConfig.EnableAutoTranslate.Value &&
                         TranslationStore.IsTranslatable(value))
                {
                    TranslationStore.RegisterLive(comp, value);
                }
            }

            if (hasCjk &&
                FontService.ApplyCjkFont(comp))
                MarkDirty(comp);

            if (NameOverlay.IsShopNameLabel(comp))
                NameOverlay.Sync(comp, value);
        }

        public static void CleanupCaches()
        {
            // Instance IDs can eventually be reused after objects are destroyed.
            // This cache is only a one-time mesh rebuild guard, so clearing it
            // occasionally is both safe and prevents stale IDs accumulating.
            if (_nullFontRebuilt.Count > 4096)
                _nullFontRebuilt.Clear();

            if (FaceContextById.Count > 0)
            {
                var dead = new List<int>();
                foreach (var pair in FaceContextById)
                {
                    try
                    {
                        if (!pair.Value.Component.TryGetTarget(out var comp) ||
                            comp == null ||
                            comp.gameObject == null)
                            dead.Add(pair.Key);
                    }
                    catch
                    {
                        dead.Add(pair.Key);
                    }
                }
                foreach (int id in dead)
                    FaceContextById.Remove(id);
            }

            if (ArcadeControlById.Count > 0)
            {
                var dead = new List<int>();
                foreach (var pair in ArcadeControlById)
                {
                    try
                    {
                        if (!pair.Value.TryGetTarget(out var comp) ||
                            comp == null ||
                            comp.gameObject == null)
                            dead.Add(pair.Key);
                    }
                    catch
                    {
                        dead.Add(pair.Key);
                    }
                }
                foreach (int id in dead)
                    ArcadeControlById.Remove(id);
            }

            if (SafeUiContextById.Count > 0)
            {
                var dead = new List<int>();
                foreach (var pair in SafeUiContextById)
                {
                    try
                    {
                        if (!pair.Value.Component.TryGetTarget(out var comp) ||
                            comp == null ||
                            comp.gameObject == null)
                            dead.Add(pair.Key);
                    }
                    catch
                    {
                        dead.Add(pair.Key);
                    }
                }
                foreach (int id in dead)
                    SafeUiContextById.Remove(id);
            }
        }

        public static void ApplyExisting(TMP_Text comp)
        {
            if (comp == null)
                return;

            try
            {
                var current = comp.text;

                if (string.IsNullOrEmpty(current))
                {
                    if (NameOverlay.IsShopNameLabel(comp))
                        NameOverlay.Sync(comp, current);
                    MainThreadRunner.RememberText(comp, current);
                    return;
                }

                string finalText = current;
                bool currentHasCjk = TranslationStore.ContainsCjk(current);
                bool cjkFontChecked = false;

                if (currentHasCjk)
                {
                    cjkFontChecked = true;
                    // If the font setup just changed, the mesh was built while no
                    // CJK fallback was available (blank glyphs) and is stale.
                    if (FontService.ApplyCjkFont(comp))
                        MarkDirty(comp);
                    else if (comp.font == null && FontService.Ready &&
                             _nullFontRebuilt.Add(comp.GetInstanceID()))
                        MarkDirty(comp);

                    if (ModConfig.EnableRuntimeTranslationFallback.Value)
                    {
                        var partial = TranslateForComponent(
                            comp,
                            current,
                            true);
                        if (partial != null)
                            finalText = partial;
                    }
                }
                else if (ModConfig.EnableRuntimeTranslationFallback.Value)
                {
                    var translated = TranslateForComponent(
                        comp,
                        current,
                        false);
                    if (translated != null)
                    {
                        finalText = translated;
                    }
                    else if (ModConfig.EnableAutoTranslate.Value &&
                             TranslationStore.IsTranslatable(current))
                    {
                        TranslationStore.RegisterLive(comp, current);
                    }
                }

                TryCompactSpacebarPrompt(comp, ref finalText);

                if (!cjkFontChecked &&
                    TranslationStore.ContainsCjk(finalText) &&
                    FontService.ApplyCjkFont(comp))
                    MarkDirty(comp);

                if (finalText != current)
                {
                    comp.text = finalText;
                    if (NameOverlay.IsShopNameLabel(comp))
                        NameOverlay.Sync(comp, finalText);
                    MainThreadRunner.RememberText(comp, finalText);
                    return;
                }

                // 已经是中文，或者商品格被复用成英文时，直接同步状态。
                if (NameOverlay.IsShopNameLabel(comp))
                    NameOverlay.Sync(comp, finalText);
                MainThreadRunner.RememberText(comp, finalText);
            }
            catch (Exception e)
            {
                Plugin.Log?.LogDebug("ApplyExisting TMP failed: " + e.Message);
            }
        }

        private static void TryCompactSpacebarPrompt(
            TMP_Text comp,
            ref string finalText)
        {
            if (comp == null || string.IsNullOrEmpty(finalText)) return;

            try
            {
                var parent = comp.transform.parent;
                if (parent == null ||
                    !string.Equals(
                        parent.name,
                        "Background",
                        StringComparison.OrdinalIgnoreCase))
                    return;

                if (string.Equals(
                        comp.gameObject.name,
                        "Index",
                        StringComparison.OrdinalIgnoreCase) &&
                    (finalText == "[空格]" || finalText == "[空格键]"))
                {
                    var choiceTransform = parent.Find("ChoiceText");
                    var choice = choiceTransform?.GetComponent<TMP_Text>();
                    if (choice == null) return;

                    var choiceText = choice.text;
                    if (choiceText != "Continue" && choiceText != "继续")
                        return;

                    finalText = "[空格] 继续";
                    choice.text = string.Empty;

                    // The stock prompt reserves a fixed 100 px key column. Once
                    // SPACEBAR is localized, that leaves an obvious visual hole.
                    // Put this one prompt in a single left-aligned label instead.
                    var rect = comp.rectTransform;
                    var size = rect.sizeDelta;
                    var position = rect.anchoredPosition;
                    rect.sizeDelta = new Vector2(180f, size.y);
                    rect.anchoredPosition = new Vector2(100f, position.y);
                    comp.alignment = TextAlignmentOptions.Left;
                    return;
                }

                if (!string.Equals(
                        comp.gameObject.name,
                        "ChoiceText",
                        StringComparison.OrdinalIgnoreCase) ||
                    (finalText != "Continue" && finalText != "继续"))
                    return;

                var indexTransform = parent.Find("Index");
                var index = indexTransform?.GetComponent<TMP_Text>();
                if (index == null ||
                    (index.text != "[空格]" &&
                     index.text != "[空格键]" &&
                     index.text != "[空格] 继续"))
                    return;

                index.text = "[空格] 继续";
                var indexRect = index.rectTransform;
                var indexSize = indexRect.sizeDelta;
                var indexPosition = indexRect.anchoredPosition;
                indexRect.sizeDelta = new Vector2(180f, indexSize.y);
                indexRect.anchoredPosition =
                    new Vector2(100f, indexPosition.y);
                index.alignment = TextAlignmentOptions.Left;
                finalText = string.Empty;
            }
            catch (Exception e)
            {
                Plugin.Log?.LogDebug(
                    "Compact spacebar prompt failed: " + e.Message);
            }
        }

        public static void ApplyExisting(UnityEngine.UI.Text comp)
        {
            if (comp == null) return;
            try
            {
                var current = comp.text;
                if (string.IsNullOrEmpty(current))
                {
                    MainThreadRunner.RememberText(comp, current);
                    return;
                }
                if (!ModConfig.EnableRuntimeTranslationFallback.Value)
                {
                    MainThreadRunner.RememberText(comp, current);
                    return;
                }
                bool currentHasCjk = TranslationStore.ContainsCjk(current);
                var translated = TranslateForComponent(
                    comp,
                    current,
                    currentHasCjk);
                if (translated == null || translated == current)
                {
                    MainThreadRunner.RememberText(comp, current);
                    return;
                }
                if (TranslationStore.ContainsCjk(translated) && FontService.LegacyCjkFont != null)
                    comp.font = FontService.LegacyCjkFont;
                comp.text = translated;
                MainThreadRunner.RememberText(comp, translated);
            }
            catch { }
        }

        [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.text), MethodType.Setter)]
        public static class SetTextProp
        {
            public static bool Prefix(TMP_Text __instance, ref string value)
            {
                TransformImmediate(__instance, ref value);
                return true;
            }
        }

        /// <summary>
        /// Patch every TMP SetText overload whose first argument is a string.
        /// Several document-style panels use formatting overloads even for their
        /// static headings; handling only SetText(string, bool) leaves those
        /// labels English until a later scan, or lets refresh loops overwrite
        /// them every frame.
        /// </summary>
        [HarmonyPatch]
        public static class SetTextString
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(TMP_Text)))
                {
                    if (method.Name != nameof(TMP_Text.SetText)) continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length > 0 &&
                        parameters[0].ParameterType == typeof(string))
                        yield return method;
                }
            }

            public static void Prefix(TMP_Text __instance, ref string __0)
            {
                TransformImmediate(__instance, ref __0);
            }
        }

        /// <summary>Legacy uGUI Text support.</summary>
        [HarmonyPatch(typeof(UnityEngine.UI.Text), "text", MethodType.Setter)]
        public static class LegacyText
        {
            public static bool Prefix(UnityEngine.UI.Text __instance, ref string value)
            {
                if (string.IsNullOrEmpty(value)) return true;
                if (!ModConfig.EnableRuntimeTranslationFallback.Value) return true;
                // Must go through TranslateDisplayText: panels such as the contacts
                // detail view append lines incrementally ("• Calming" -> "...舒缓\n•
                // Munchies"), and plain Translate() rejects any string that already
                // contains CJK, so every line after the first would stay English.
                var translated = TranslateForComponent(
                    __instance,
                    value,
                    TranslationStore.ContainsCjk(value));
                if (translated != null)
                {
                    if (TranslationStore.ContainsCjk(translated) && FontService.LegacyCjkFont != null)
                        __instance.font = FontService.LegacyCjkFont;
                    value = translated;
                }
                return true;
            }
        }

        /// <summary>
        /// Translate labels baked into prefabs. Deserialization fills TMP fields
        /// without dispatching the change event; the periodic scan also misses
        /// panels that are inactive at scan time. OnEnable is the exact moment
        /// a baked label becomes visible (phone apps, shop panels, popups).
        /// TMP_Text itself does not declare OnEnable in the interop assemblies —
        /// only the concrete subclasses do — so each is patched explicitly via
        /// TargetMethods. Every component seen here is also registered for the
        /// rolling rescan that catches text written natively (animation-driven
        /// banners).
        /// </summary>
        [HarmonyPatch]
        public static class BakedTextOnEnable
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                var targets = new[]
                {
                    AccessTools.Method(typeof(TextMeshProUGUI), "OnEnable"),
                    AccessTools.Method(typeof(TextMeshPro), "OnEnable"),
                    AccessTools.Method(typeof(UnityEngine.UI.Text), "OnEnable")
                };
                foreach (var method in targets)
                    if (method != null)
                        yield return method;
            }

            public static void Postfix(object __instance)
            {
                try
                {
                    if (__instance is TMP_Text tmp)
                    {
                        MainThreadRunner.RegisterText(tmp);
                        ApplyExisting(tmp);
                    }
                    else if (__instance is UnityEngine.UI.Text ugui)
                    {
                        MainThreadRunner.RegisterText(ugui);
                        ApplyExisting(ugui);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log?.LogDebug("OnEnable translation failed: " + e.Message);
                }
            }
        }

        /// <summary>
        /// The offence notice is populated by native IL2CPP code after its
        /// legacy Text components are enabled. Rescan the finished document so
        /// both its static headings and generated fine lines are translated.
        /// </summary>
        [HarmonyPatch(
            typeof(OffenceNoticeUI),
            nameof(OffenceNoticeUI.ShowOffenceNotice))]
        public static class OffenceNoticeRefresh
        {
            public static void Postfix(OffenceNoticeUI __instance)
            {
                try
                {
                    if (__instance?.container == null) return;
                    foreach (var text in
                        __instance.container.GetComponentsInChildren<
                            UnityEngine.UI.Text>(true))
                    {
                        if (text == null) continue;
                        MainThreadRunner.RegisterText(text);
                        ApplyExisting(text);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log?.LogDebug(
                        "Offence notice translation failed: " + e.Message);
                }
            }
        }
    }
}
