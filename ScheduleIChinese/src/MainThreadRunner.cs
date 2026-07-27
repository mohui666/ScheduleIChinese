using System;
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
        private int _sceneFingerprint = int.MinValue;
        private bool _fontWasReady;
        private static MainThreadRunner _instance;

        // Rolling registry of every text component that has been enabled. A few
        // are re-checked each frame; this catches text the game writes natively
        // (animation-driven banners such as WANTED / UNDER ARREST), which never
        // passes through any managed setter.
        private static readonly System.Collections.Generic.List<TMP_Text> _tmpRegistry =
            new System.Collections.Generic.List<TMP_Text>();
        private static readonly System.Collections.Generic.List<UnityEngine.UI.Text> _uguiRegistry =
            new System.Collections.Generic.List<UnityEngine.UI.Text>();
        private int _tmpScanIndex;
        private int _uguiScanIndex;

        public static void RegisterText(TMP_Text comp)
        {
            if (comp == null) return;
            if (_tmpRegistry.Count > 2048) _tmpRegistry.Clear();
            if (!_tmpRegistry.Contains(comp)) _tmpRegistry.Add(comp);
        }

        public static void RegisterText(UnityEngine.UI.Text comp)
        {
            if (comp == null) return;
            if (_uguiRegistry.Count > 512) _uguiRegistry.Clear();
            if (!_uguiRegistry.Contains(comp)) _uguiRegistry.Add(comp);
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

            // Setter patches cover live changes. Scan only once when the CJK font
            // becomes ready and once after the loaded-scene set changes. The old
            // two-second global scan caused avoidable stalls on large save loads.
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
            }

            RollingScan();
        }

        /// <summary>Re-check a small slice of registered components every frame.</summary>
        private void RollingScan()
        {
            if (!FontService.Ready) return;
            if (!ModConfig.EnableRuntimeTranslationFallback.Value) return;

            for (int n = 0; n < 24 && _tmpRegistry.Count > 0; n++)
            {
                if (_tmpScanIndex >= _tmpRegistry.Count) _tmpScanIndex = 0;
                var comp = _tmpRegistry[_tmpScanIndex++];
                bool dead;
                try { dead = comp == null || !comp.gameObject.activeInHierarchy; }
                catch { dead = true; }
                if (dead)
                {
                    _tmpRegistry.RemoveAt(--_tmpScanIndex);
                    continue;
                }
                TextPatch.ApplyExisting(comp);
            }

            for (int n = 0; n < 8 && _uguiRegistry.Count > 0; n++)
            {
                if (_uguiScanIndex >= _uguiRegistry.Count) _uguiScanIndex = 0;
                var comp = _uguiRegistry[_uguiScanIndex++];
                bool dead;
                try { dead = comp == null || !comp.gameObject.activeInHierarchy; }
                catch { dead = true; }
                if (dead)
                {
                    _uguiRegistry.RemoveAt(--_uguiScanIndex);
                    continue;
                }
                TextPatch.ApplyExisting(comp);
            }
        }

        private static void RescanActiveText()
        {
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                int tmpCount = 0;
                int legacyCount = 0;
                foreach (var text in UnityEngine.Object.FindObjectsOfType<TMP_Text>(false))
                {
                    if (text == null || text.gameObject == null) continue;
                    tmpCount++;
                    FontService.EnsureCjkFont(text);
                    if (ModConfig.EnableRuntimeTranslationFallback.Value)
                        TextPatch.ApplyExisting(text);
                }

                foreach (var text in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>(false))
                {
                    if (text == null || text.gameObject == null) continue;
                    legacyCount++;
                    if (ModConfig.EnableRuntimeTranslationFallback.Value)
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
