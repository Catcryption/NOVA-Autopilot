using Newtonsoft.Json;
using UnityEngine;

namespace NOVA_Autopilot
{
    public enum Difficulty
    {
        Normal,
        Hard,
        Realistic
    }

    [System.Serializable]
    public class SettingsData
    {
        // ── Window positions ──────────────────────────────────────────────────

        public float mainWindowX     = 200f;
        public float mainWindowY     = 0f;
        public float settingsWindowX = 420f;
        public float settingsWindowY = 0f;

        // ── Keybinds ──────────────────────────────────────────────────────────

        public KeyCode orbitToggleKey          = KeyCode.F1;
        public KeyCode deorbitToggleKey        = KeyCode.F2;
        public KeyCode dockingToggleKey        = KeyCode.F3;
        public KeyCode interplanetaryToggleKey = KeyCode.F4;

        // ── Misc ──────────────────────────────────────────────────────────────

        public Difficulty difficulty       = Difficulty.Normal;
        public string     targetPlanetName = "";
        public string     targetShipName   = "";
    }

    public static class Settings
    {
        private const string PREFS_KEY = "NOVA_Autopilot_Settings";

        public static SettingsData data = new SettingsData();

        // ── Window position helpers ───────────────────────────────────────────

        public static Vector2 MainWindowPos
        {
            get => new Vector2(data.mainWindowX, data.mainWindowY);
            set { data.mainWindowX = value.x; data.mainWindowY = value.y; }
        }

        public static Vector2 SettingsWindowPos
        {
            get => new Vector2(data.settingsWindowX, data.settingsWindowY);
            set { data.settingsWindowX = value.x; data.settingsWindowY = value.y; }
        }

        // ── Persistence ───────────────────────────────────────────────────────

        public static void Load()
        {
            if (!PlayerPrefs.HasKey(PREFS_KEY))
            {
                data = new SettingsData();
                return;
            }

            try
            {
                string json = PlayerPrefs.GetString(PREFS_KEY);
                data = JsonConvert.DeserializeObject<SettingsData>(json) ?? new SettingsData();
            }
            catch
            {
                data = new SettingsData();
                Debug.LogWarning("[NOVA_Autopilot] Failed to load settings, using defaults.");
            }
        }

        public static void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(data);
                PlayerPrefs.SetString(PREFS_KEY, json);
                PlayerPrefs.Save();
            }
            catch
            {
                Debug.LogWarning("[NOVA_Autopilot] Failed to save settings.");
            }
        }

        public static void Reset()
        {
            data = new SettingsData();
            Save();
        }
    }
}