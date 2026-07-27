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
