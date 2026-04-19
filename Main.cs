using System.Collections.Generic;
using HarmonyLib;
using ModLoader;
using ModLoader.Helpers;
using SFS.IO;
using UITools;
using UnityEngine;

namespace NOVA_Autopilot
{
    public class Main : Mod
    {
        public override string ModNameID => "NOVA_Autopilot";
        public override string DisplayName => "NOVA Autopilot";
        public override string Author => "YourNameHere";
        public override string MinimumGameVersionNecessary => "1.5.10.2";
        public override string ModVersion => "v0.1.0";
        public override string Description => "NOVA Autopilot - Autonomous orbital, deorbit, docking, and interplanetary maneuvers.";

        public override Dictionary<string, string> Dependencies { get; } = new Dictionary<string, string>
        {
            { "UITools", "1.1.6" },
            { "smartsas", "2.2" },
            { "DELTA_V_CALCULATOR", "V1.1.4" },
            { "ANAIS", "v1.4.2" }
        };

        public static FolderPath modFolder;
        public static AutopilotUpdater updater;

        private static Harmony patcher;

        public override void Early_Load()
        {
            modFolder = new FolderPath(ModFolder);
            patcher = new Harmony(ModNameID);
            patcher.PatchAll();
        }

        public override void Load()
        {
            Settings.Load();

            GameObject go = new GameObject("NOVA_Autopilot_Updater");
            Object.DontDestroyOnLoad(go);
            updater = go.AddComponent<AutopilotUpdater>();

            SceneHelper.OnWorldSceneLoaded += GUI.ShowGUI;
            SceneHelper.OnWorldSceneUnloaded += GUI.HideGUI;

            throw new System.Exception("Thank you for playing the NOVA Autopilot! :D I hope your pillow is cold on both sides tonight. :)");
        }
    }
}