using BepInEx.Configuration;

namespace ScheduleIChinese
{
    public static class ModConfig
    {
        public static ConfigEntry<bool> EnableAutoTranslate;
        public static ConfigEntry<bool> EnableRuntimeTranslationFallback;
        public static ConfigEntry<bool> DumpUntranslated;
        public static ConfigEntry<int> AutoTranslateDelayMs;
        public static ConfigEntry<string> FontFile;

        public static void Init(ConfigFile cfg)
        {
            EnableAutoTranslate = cfg.Bind("General", "EnableAutoTranslate", false,
                "OFF by default: translations come from curated AI-translated files in Translations/. Enable only to machine-translate leftover text via Google (lower quality). Results are cached to Translations/Auto.zh_CN.txt");
            EnableRuntimeTranslationFallback = cfg.Bind(
                "General", "EnableRuntimeTranslationFallback", true,
                "Offline display-layer translation for dynamic TMP text. Does not alter game logic keys or call the network.");
            DumpUntranslated = cfg.Bind("General", "DumpUntranslated", false,
                "Write strings that have no translation yet to Untranslated.txt (for building translation files)");
            AutoTranslateDelayMs = cfg.Bind("General", "AutoTranslateDelayMs", 300,
                new ConfigDescription("Delay between auto-translation requests (ms)",
                    new AcceptableValueRange<int>(100, 10000)));
            FontFile = cfg.Bind("Font", "FontFile", "assets/NotoSansSC.otf",
                "Chinese font file, relative to the plugin data dir (BepInEx/plugins/ScheduleIChinese). Falls back to C:/Windows/Fonts/msyh.ttc");
        }
    }
}
