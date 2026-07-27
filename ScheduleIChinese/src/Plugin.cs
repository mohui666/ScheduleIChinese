using System;
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
        public const string Version = "1.3.16";

        public static Plugin Instance { get; private set; }
        public static new ManualLogSource Log => Instance?.BaseLog;
        private ManualLogSource BaseLog => base.Log;

        public static string DataDir { get; private set; }

        public override void Load()
        {
            Instance = this;
            DataDir = Path.Combine(Paths.PluginPath, "ScheduleIChinese");
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(Path.Combine(DataDir, "Translations"));

            ModConfig.Init(Config);
            if (ModConfig.EnableRuntimeTranslationFallback.Value || ModConfig.EnableAutoTranslate.Value)
                TranslationStore.Init();
            else
                Log.LogInfo("Static text edition: offline display-layer translation is disabled.");
            if (ModConfig.EnableAutoTranslate.Value)
            {
                AutoTranslator.Start();
                Log.LogWarning("Online auto-translation is ENABLED. Curated offline translations remain preferred.");
            }
            else
            {
                Log.LogInfo("Online auto-translation is disabled; running fully offline.");
            }

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(TextPatch).Assembly);

            ClassInjector.RegisterTypeInIl2Cpp<MainThreadRunner>();
            var go = new GameObject("ScheduleIChinese.Runner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<MainThreadRunner>();

            Log.LogInfo(
                $"ScheduleIChinese {Version} loaded. Font support active; " +
                $"runtime translation fallback: {ModConfig.EnableRuntimeTranslationFallback.Value}");
        }
    }
}
