using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ScheduleIChinese
{
    /// <summary>Intercepts text assignments on TMP components and swaps in Chinese.</summary>
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

        public static void ApplyPendingChanges(int budget = 256)
        {
            for (int i = 0; i < budget; i++)
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
            }
        }

        private static void MarkDirty(TMP_Text comp)
        {
            try { comp.havePropertiesChanged = true; } catch { }
        }

        private static void Transform(TMP_Text comp, ref string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (NameOverlay.IsShopNameLabel(comp)) NameOverlay.Sync(comp);
            if (TranslationStore.ContainsCjk(value))
            {
                if (FontService.ApplyCjkFont(comp))
                    MarkDirty(comp);
                if (ModConfig.EnableRuntimeTranslationFallback.Value)
                {
                    var partial = TranslationStore.TranslateDisplayText(value);
                    if (partial != null) value = partial;
                }
                return;
            }
            if (!ModConfig.EnableRuntimeTranslationFallback.Value) return;

            var translated = TranslationStore.TranslateDisplayText(value);
            if (translated != null)
            {
                if (FontService.ApplyCjkFont(comp))
                    MarkDirty(comp);
                value = translated;
            }
            else if (ModConfig.EnableAutoTranslate.Value &&
                     TranslationStore.IsTranslatable(value) &&
                     !TranslationStore.ContainsCjk(value))
            {
                // remember who is showing this text so a late auto-translation can be applied
                TranslationStore.RegisterLive(comp, value);
            }
        }

        public static void ApplyExisting(TMP_Text comp)
        {
            if (comp == null) return;
            if (NameOverlay.IsShopNameLabel(comp)) NameOverlay.Sync(comp);
            try
            {
                var current = comp.text;
                if (string.IsNullOrEmpty(current)) return;
                if (TranslationStore.ContainsCjk(current))
                {
                    // If the font setup just changed, the mesh was built while no
                    // CJK fallback was available (blank glyphs) and is stale.
                    if (FontService.ApplyCjkFont(comp))
                        MarkDirty(comp);
                    else if (comp.font == null && FontService.Ready &&
                             _nullFontRebuilt.Add(comp.GetInstanceID()))
                        MarkDirty(comp);
                    if (!ModConfig.EnableRuntimeTranslationFallback.Value) return;
                    var partial = TranslationStore.TranslateDisplayText(current);
                    if (partial == null || partial == current) return;
                    comp.text = partial;
                    return;
                }
                if (!ModConfig.EnableRuntimeTranslationFallback.Value) return;

                var translated = TranslationStore.TranslateDisplayText(current);
                if (translated == null || translated == current) return;
                FontService.ApplyCjkFont(comp);
                comp.text = translated;
            }
            catch { }
        }

        public static void ApplyExisting(UnityEngine.UI.Text comp)
        {
            if (comp == null) return;
            try
            {
                var current = comp.text;
                if (string.IsNullOrEmpty(current)) return;
                if (!ModConfig.EnableRuntimeTranslationFallback.Value) return;
                var translated = TranslationStore.TranslateDisplayText(current);
                if (translated == null || translated == current) return;
                if (TranslationStore.ContainsCjk(translated) && FontService.LegacyCjkFont != null)
                    comp.font = FontService.LegacyCjkFont;
                comp.text = translated;
            }
            catch { }
        }

        [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.text), MethodType.Setter)]
        public static class SetTextProp
        {
            public static bool Prefix(TMP_Text __instance, ref string value)
            {
                Transform(__instance, ref value);
                return true;
            }
        }

        /// <summary>
        /// Patch every TMP SetText overload whose first argument is a string. This
        /// includes the float-formatting overloads used for money, quantities and
        /// progress values, while avoiding brittle per-version overload lists.
        /// </summary>
        [HarmonyPatch]
        public static class SetTextStringOverloads
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(TMP_Text)))
                {
                    if (method.Name != nameof(TMP_Text.SetText)) continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length > 0 && parameters[0].ParameterType == typeof(string))
                        yield return method;
                }
            }

            public static void Prefix(TMP_Text __instance, object[] __args)
            {
                if (__args == null || __args.Length == 0 || !(__args[0] is string sourceText)) return;
                Transform(__instance, ref sourceText);
                __args[0] = sourceText;
            }
        }

        /// <summary>
        /// Patch SetText(StringBuilder) overloads. List-heavy UI (effect lists,
        /// inventory rows) is commonly built through StringBuilder, which never
        /// passes through the string overloads above; without this hook those
        /// strings stay English and never even reach the translation dump.
        /// </summary>
        [HarmonyPatch]
        public static class SetTextBuilderOverloads
        {
            public static IEnumerable<MethodBase> TargetMethods()
            {
                foreach (var method in AccessTools.GetDeclaredMethods(typeof(TMP_Text)))
                {
                    if (method.Name != nameof(TMP_Text.SetText)) continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length == 1 &&
                        parameters[0].ParameterType == typeof(Il2CppSystem.Text.StringBuilder))
                        yield return method;
                }
            }

            public static void Prefix(TMP_Text __instance, object[] __args)
            {
                if (__args == null || __args.Length == 0) return;
                var sb = __args[0] as Il2CppSystem.Text.StringBuilder;
                if (sb == null) return;
                try
                {
                    var sourceText = sb.ToString();
                    var before = sourceText;
                    Transform(__instance, ref sourceText);
                    if (sourceText != before)
                    {
                        sb.Clear();
                        sb.Append(sourceText);
                    }
                }
                catch { }
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
        /// without touching the managed setter, so neither the setter patch nor
        /// the change event ever fires for them; the periodic scan also misses
        /// panels that are inactive at scan time. OnEnable is the exact moment a
        /// baked label becomes visible (phone apps, shop panels, popups).
        /// TMP_Text itself does not declare OnEnable in the interop assemblies —
        /// only the concrete subclasses do — so each is patched explicitly.
        /// Every component seen here is also registered for the rolling rescan
        /// that catches text written natively (animation-driven banners).
        /// </summary>
        [HarmonyPatch(typeof(TextMeshProUGUI), "OnEnable")]
        [HarmonyPatch(typeof(TextMeshPro), "OnEnable")]
        [HarmonyPatch(typeof(UnityEngine.UI.Text), "OnEnable")]
        public static class BakedTextOnEnable
        {
            public static void Postfix(UnityEngine.UI.MaskableGraphic __instance)
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
                catch { }
            }
        }
    }
}
