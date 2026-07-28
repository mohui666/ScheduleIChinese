using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
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
        private static Il2CppSystem.Action<UnityEngine.Object> _textChangedHandler;

        private sealed class PendingText
        {
            public int InstanceId;
            public WeakReference<TMP_Text> Component;
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

        public static void CleanupCaches()
        {
            // Instance IDs can eventually be reused after objects are destroyed.
            // This cache is only a one-time mesh rebuild guard, so clearing it
            // occasionally is both safe and prevents stale IDs accumulating.
            if (_nullFontRebuilt.Count > 4096)
                _nullFontRebuilt.Clear();
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

                if (TranslationStore.ContainsCjk(current))
                {
                    // If the font setup just changed, the mesh was built while no
                    // CJK fallback was available (blank glyphs) and is stale.
                    if (FontService.ApplyCjkFont(comp))
                        MarkDirty(comp);
                    else if (comp.font == null && FontService.Ready &&
                             _nullFontRebuilt.Add(comp.GetInstanceID()))
                        MarkDirty(comp);

                    if (ModConfig.EnableRuntimeTranslationFallback.Value)
                    {
                        var partial = TranslationStore.TranslateDisplayText(current);
                        if (partial != null)
                            finalText = partial;
                    }
                }
                else if (ModConfig.EnableRuntimeTranslationFallback.Value)
                {
                    var translated = TranslationStore.TranslateDisplayText(current);
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

                if (TranslationStore.ContainsCjk(finalText) &&
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
                var translated = TranslationStore.TranslateDisplayText(current);
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
                var translated = TranslationStore.TranslateDisplayText(value);
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
    }
}
