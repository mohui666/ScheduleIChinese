using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScheduleIChinese
{
    /// <summary>Runs main-thread jobs every frame (font init, applying translations).</summary>
    public class MainThreadRunner : MonoBehaviour
    {
        public MainThreadRunner(IntPtr ptr) : base(ptr) { }
        public MainThreadRunner() : base(ClassInjector.DerivedConstructorPointer<MainThreadRunner>())
        {
            ClassInjector.DerivedConstructorBody(this);
        }

        private float _nextCleanup;
        private float _scheduledRescan = -1f;
        private float _sceneFollowupRescan = -1f;
        private float _lastActiveRescan = -1000f;
        private float _nextRollingScan;
        private int _sceneFingerprint = int.MinValue;
        private bool _fontWasReady;
        private bool _performanceSnapshotLogged;
        private static MainThreadRunner _instance;

        // Rolling registry of every text component that has been enabled. A few
        // are re-checked each frame; this catches text the game writes natively
        // (animation-driven banners such as WANTED / UNDER ARREST), which never
        // passes through any managed setter.
        private sealed class TrackedTmpText
        {
            public int InstanceId;
            public WeakReference<TMP_Text> Component;
            public string LastText;
        }

        private sealed class TrackedUguiText
        {
            public int InstanceId;
            public WeakReference<UnityEngine.UI.Text> Component;
            public string LastText;
        }

        private static readonly List<TrackedTmpText> _tmpRegistry =
            new List<TrackedTmpText>();
        private static readonly Dictionary<int, TrackedTmpText> _tmpById =
            new Dictionary<int, TrackedTmpText>();
        private static readonly List<TrackedUguiText> _uguiRegistry =
            new List<TrackedUguiText>();
        private static readonly Dictionary<int, TrackedUguiText> _uguiById =
            new Dictionary<int, TrackedUguiText>();
        private int _tmpScanIndex;
        private int _uguiScanIndex;

        public static void RegisterText(TMP_Text comp)
        {
            if (comp == null) return;
            int id;
            try { id = comp.GetInstanceID(); }
            catch { return; }
            if (_tmpById.ContainsKey(id)) return;
            var tracked = new TrackedTmpText
            {
                InstanceId = id,
                Component = new WeakReference<TMP_Text>(comp)
            };
            _tmpById[id] = tracked;
            _tmpRegistry.Add(tracked);
        }

        public static void RegisterText(UnityEngine.UI.Text comp)
        {
            if (comp == null) return;
            int id;
            try { id = comp.GetInstanceID(); }
            catch { return; }
            if (_uguiById.ContainsKey(id)) return;
            var tracked = new TrackedUguiText
            {
                InstanceId = id,
                Component = new WeakReference<UnityEngine.UI.Text>(comp)
            };
            _uguiById[id] = tracked;
            _uguiRegistry.Add(tracked);
        }

        public static void RememberText(TMP_Text comp, string text)
        {
            if (comp == null) return;
            try
            {
                if (_tmpById.TryGetValue(comp.GetInstanceID(), out var tracked))
                    tracked.LastText = text;
            }
            catch { }
        }

        public static void RememberText(UnityEngine.UI.Text comp, string text)
        {
            if (comp == null) return;
            try
            {
                if (_uguiById.TryGetValue(comp.GetInstanceID(), out var tracked))
                    tracked.LastText = text;
            }
            catch { }
        }

        private void Awake()
        {
            _instance = this;
        }


        public static void RequestActiveTextRescan()
        {
            var instance = _instance;
            if (instance == null || !FontService.Ready) return;
            // Relation-circle hover events and rapid panel selections can arrive
            // several times in one gesture. Coalesce them so UI navigation never
            // turns into a stream of 20-40 ms scans.
            float requested = Mathf.Max(
                Time.unscaledTime + 0.1f,
                instance._lastActiveRescan + 0.35f);
            if (instance._scheduledRescan < 0f || requested < instance._scheduledRescan)
                instance._scheduledRescan = requested;
        }

        private void Update()
        {
            FontService.Tick();
            if (ModConfig.EnableRuntimeTranslationFallback.Value)
                TextPatch.ApplyPendingChanges();
            if (ModConfig.EnableAutoTranslate.Value)
                AutoTranslator.ApplyPendingOnMainThread();
            if (ModConfig.DumpUntranslated.Value)
                TranslationStore.FlushDumpIfDue();

            // The TMP change event covers live changes. Scan only once when the
            // CJK font becomes ready and once after the loaded-scene set changes.
            // The old two-second global scan caused avoidable stalls on large
            // save loads.
            int fingerprint = SceneManager.sceneCount;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                fingerprint = unchecked(fingerprint * 397 ^ scene.handle);
            }
            bool fontReady = FontService.Ready;
            if (fontReady && (!_fontWasReady || fingerprint != _sceneFingerprint))
            {
                _sceneFingerprint = fingerprint;
                _scheduledRescan = Time.unscaledTime + 1f;
                // Some menu/news widgets populate after the first scene scan.
                // One bounded follow-up catches them without patching the very
                // noisy UIPanel.Select/SelectSelectable navigation methods.
                _sceneFollowupRescan = Time.unscaledTime + 3f;
            }
            _fontWasReady = fontReady;

            if (fontReady &&
                ModConfig.EnableRuntimeTranslationFallback.Value &&
                !_performanceSnapshotLogged &&
                Time.unscaledTime >= 10f)
            {
                _performanceSnapshotLogged = true;
                Plugin.Log.LogInfo(TranslationStore.GetPerformanceSnapshot());
            }

            if (_sceneFollowupRescan >= 0f &&
                Time.unscaledTime >= _sceneFollowupRescan)
            {
                _sceneFollowupRescan = -1f;
                if (_scheduledRescan < 0f)
                    _scheduledRescan = Time.unscaledTime;
            }

            if (_scheduledRescan >= 0f && Time.unscaledTime >= _scheduledRescan)
            {
                _scheduledRescan = -1f;
                RescanActiveText();
                _lastActiveRescan = Time.unscaledTime;
            }

            if (Time.time >= _nextCleanup)
            {
                _nextCleanup = Time.time + 60f;
                TranslationStore.CleanupLive();
                TextPatch.CleanupCaches();
                NameOverlay.CleanupCache();
            }

            // Keep native-write detection independent of the rendered frame rate.
            // Running this on every frame made the same UI cost 2-4x more at
            // 120/144 Hz than at 60 Hz without improving visible responsiveness.
            if (Time.unscaledTime >= _nextRollingScan)
            {
                _nextRollingScan = Time.unscaledTime + (1f / 30f);
                RollingScan();
            }
        }

        /// <summary>Re-check a small slice of registered components at 30 Hz.</summary>
        private void RollingScan()
        {
            if (!FontService.Ready) return;
            if (!ModConfig.EnableRuntimeTranslationFallback.Value) return;

            for (int n = 0; n < 24 && _tmpRegistry.Count > 0; n++)
            {
                if (_tmpScanIndex >= _tmpRegistry.Count) _tmpScanIndex = 0;
                var tracked = _tmpRegistry[_tmpScanIndex++];
                TMP_Text comp = null;
                bool destroyed;
                try
                {
                    destroyed =
                        !tracked.Component.TryGetTarget(out comp) ||
                        comp == null ||
                        comp.gameObject == null;
                }
                catch { destroyed = true; }
                if (destroyed)
                {
                    _tmpRegistry.RemoveAt(--_tmpScanIndex);
                    _tmpById.Remove(tracked.InstanceId);
                    continue;
                }
                // Inactive components are skipped but stay registered, so
                // panels that close and reopen keep being covered.
                if (!comp.gameObject.activeInHierarchy) continue;
                string current;
                try { current = comp.text; }
                catch { continue; }
                if (string.Equals(current, tracked.LastText, StringComparison.Ordinal))
                {
                    // Shop selection can change visual style without changing
                    // the label text. Keep its cached overlay in sync while
                    // skipping translation work for every other stable label.
                    if (NameOverlay.IsShopNameLabel(comp))
                        NameOverlay.Sync(comp, current);
                    continue;
                }
                TextPatch.ApplyExisting(comp);
                try { tracked.LastText = comp.text; }
                catch { }
            }

            for (int n = 0; n < 8 && _uguiRegistry.Count > 0; n++)
            {
                if (_uguiScanIndex >= _uguiRegistry.Count) _uguiScanIndex = 0;
                var tracked = _uguiRegistry[_uguiScanIndex++];
                UnityEngine.UI.Text comp = null;
                bool destroyed;
                try
                {
                    destroyed =
                        !tracked.Component.TryGetTarget(out comp) ||
                        comp == null ||
                        comp.gameObject == null;
                }
                catch { destroyed = true; }
                if (destroyed)
                {
                    _uguiRegistry.RemoveAt(--_uguiScanIndex);
                    _uguiById.Remove(tracked.InstanceId);
                    continue;
                }
                if (!comp.gameObject.activeInHierarchy) continue;
                string current;
                try { current = comp.text; }
                catch { continue; }
                if (string.Equals(current, tracked.LastText, StringComparison.Ordinal))
                    continue;
                TextPatch.ApplyExisting(comp);
                try { tracked.LastText = comp.text; }
                catch { }
            }
        }

        private static void RescanActiveText()
        {
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                int tmpCount = 0;
                int legacyCount = 0;
                bool runtimeFallback =
                    ModConfig.EnableRuntimeTranslationFallback.Value;
                foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None))
                {
                    if (text == null || text.gameObject == null) continue;
                    tmpCount++;
                    RegisterText(text);
                    if (runtimeFallback)
                        TextPatch.ApplyExisting(text);
                    else
                    {
                        string current;
                        try { current = text.text; }
                        catch { continue; }
                        if (!string.IsNullOrEmpty(current) &&
                            TranslationStore.ContainsCjk(current))
                            FontService.EnsureCjkFont(text);
                    }
                }

                foreach (var text in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Text>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None))
                {
                    if (text == null || text.gameObject == null) continue;
                    legacyCount++;
                    RegisterText(text);
                    if (runtimeFallback)
                        TextPatch.ApplyExisting(text);
                }
                timer.Stop();
                Plugin.Log.LogInfo(
                    $"Active UI text scan: {tmpCount} TMP, {legacyCount} uGUI, " +
                    $"{timer.ElapsedMilliseconds} ms.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogDebug("text rescan failed: " + e.Message);
            }
        }
    }
}
