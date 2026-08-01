using System;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace ScheduleIChinese
{
    [BepInPlugin(Guid, "ScheduleIChinese", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "com.schedulei.chinesemod";
        public const string Version = "1.3.57";

        public static Plugin Instance { get; private set; }
        public static new ManualLogSource Log => Instance?.BaseLog;
        private ManualLogSource BaseLog => base.Log;

        public static string DataDir { get; private set; }

        public override void Load()
        {
            var startupTimer = Stopwatch.StartNew();
            Instance = this;
            DataDir = Path.Combine(Paths.PluginPath, "ScheduleIChinese");
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(Path.Combine(DataDir, "Translations"));

            long phaseStart = startupTimer.ElapsedMilliseconds;
            ModConfig.Init(Config);
            long configMs = startupTimer.ElapsedMilliseconds - phaseStart;

            phaseStart = startupTimer.ElapsedMilliseconds;
            if (ModConfig.EnableRuntimeTranslationFallback.Value || ModConfig.EnableAutoTranslate.Value)
                TranslationStore.Init();
            else
                Log.LogInfo("Static text edition: offline display-layer translation is disabled.");
            long translationsMs = startupTimer.ElapsedMilliseconds - phaseStart;

            if (ModConfig.EnableAutoTranslate.Value)
            {
                AutoTranslator.Start();
                Log.LogWarning("Online auto-translation is ENABLED. Curated offline translations remain preferred.");
            }
            else
            {
                Log.LogInfo("Online auto-translation is disabled; running fully offline.");
            }

            phaseStart = startupTimer.ElapsedMilliseconds;
            var harmony = new Harmony(Guid);
            ApplyHarmonyPatches(harmony);
            long patchesMs = startupTimer.ElapsedMilliseconds - phaseStart;

            phaseStart = startupTimer.ElapsedMilliseconds;
            try
            {
                TextPatch.InitializeChangeListener();
            }
            catch (Exception e)
            {
                Log.LogWarning("TMP text-change listener initialization failed: " + e);
            }

            ClassInjector.RegisterTypeInIl2Cpp<MainThreadRunner>();
            var go = new GameObject("ScheduleIChinese.Runner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<MainThreadRunner>();
            long runnerMs = startupTimer.ElapsedMilliseconds - phaseStart;

            Log.LogInfo(
                $"ScheduleIChinese {Version} loaded. Font support active; " +
                $"runtime translation fallback: {ModConfig.EnableRuntimeTranslationFallback.Value}");
            Log.LogInfo(
                $"Startup timings: config {configMs} ms, translations {translationsMs} ms, " +
                $"patches {patchesMs} ms, runner {runnerMs} ms, " +
                $"total {startupTimer.ElapsedMilliseconds} ms.");
        }

        private static void ApplyHarmonyPatches(Harmony harmony)
        {
            var patchTypes = new[]
            {
                typeof(TextPatch.SetTextProp),
                typeof(TextPatch.SetTextString),
                typeof(TextPatch.LegacyText),
                typeof(TextPatch.BakedTextOnEnable),
                typeof(TextPatch.OffenceNoticeRefresh)
            };

            foreach (var patchType in patchTypes)
            {
                var timer = Stopwatch.StartNew();
                harmony.CreateClassProcessor(patchType).Patch();
                Log.LogInfo(
                    $"Harmony patch {patchType.Name}: {timer.ElapsedMilliseconds} ms.");
            }
        }
    }
}
