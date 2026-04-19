using SFS.World;
using UITools;
using UnityEngine;
using UnityEngine.UI;

namespace NOVA_Autopilot
{
    public static class GUI
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const double MIN_ORBIT_ALTITUDE = 31000.0; // m

        // ── Windows ───────────────────────────────────────────────────────────

        private static GameObject mainWindowObject;
        private static UIWindow   mainWindow;

        private static GameObject settingsWindowObject;
        private static UIWindow   settingsWindow;

        // ── Autopilot instances ───────────────────────────────────────────────

        private static OrbitAutopilot          orbitAP;
        private static DeorbitAutopilot        deorbitAP;
        private static DockingAutopilot        dockingAP;
        private static InterplanetaryAutopilot interplanetaryAP;

        // ── Buttons ───────────────────────────────────────────────────────────

        private static Button orbitBtn;
        private static Button deorbitBtn;
        private static Button dockingBtn;
        private static Button interplanetaryBtn;

        // ── Settings state ────────────────────────────────────────────────────

        // Target orbit altitude in metres, set by the user via the settings window.
        public static double TargetOrbitAltitude { get; private set; } = MIN_ORBIT_ALTITUDE;

        // ── Active autopilot tag ──────────────────────────────────────────────

        private enum ActiveAP { None, Orbit, Deorbit, Docking, Interplanetary }
        private static ActiveAP activeAP = ActiveAP.None;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public static void ShowGUI()
        {
            Rocket rocket = GetActiveRocket();

            orbitAP          = new OrbitAutopilot(rocket);
            deorbitAP        = new DeorbitAutopilot(rocket);
            dockingAP        = new DockingAutopilot(rocket);
            interplanetaryAP = new InterplanetaryAutopilot(rocket);
            activeAP         = ActiveAP.None;

            BuildMainWindow();
            BuildSettingsWindow();
        }

        public static void HideGUI()
        {
            StopAll();
            DestroyWindow(ref mainWindowObject);
            DestroyWindow(ref settingsWindowObject);
            mainWindow     = null;
            settingsWindow = null;
        }

        // ── Main window ───────────────────────────────────────────────────────

        private static void BuildMainWindow()
        {
            if (mainWindowObject != null)
                Object.Destroy(mainWindowObject);

            mainWindowObject = UIToolsBuilder.CreateWindow(
                "NOVA Autopilot",
                new Vector2(200f, 0f),
                out mainWindow
            );

            orbitBtn          = AddButton(mainWindow, "Orbit AP",       OnOrbitClicked);
            deorbitBtn        = AddButton(mainWindow, "Deorbit",        OnDeorbitClicked);
            dockingBtn        = AddButton(mainWindow, "Docking",        OnDockingClicked);
            interplanetaryBtn = AddButton(mainWindow, "Interplanetary", OnInterplanetaryClicked);

            RefreshButtonVisibility();
        }

        // ── Settings window ───────────────────────────────────────────────────

        private static void BuildSettingsWindow()
        {
            if (settingsWindowObject != null)
                Object.Destroy(settingsWindowObject);

            settingsWindowObject = UIToolsBuilder.CreateWindow(
                "NOVA Settings",
                new Vector2(420f, 0f),
                out settingsWindow
            );

            // -- Orbit target altitude --
            UIToolsBuilder.CreateLabel(settingsWindow, "Target Orbit Altitude (m)");
            UIToolsBuilder.CreateInputField(
                settingsWindow,
                (MIN_ORBIT_ALTITUDE / 1000.0).ToString("F0") + " km",
                OnOrbitAltitudeChanged
            );

            // -- Target planet (used by Interplanetary AP) --
            UIToolsBuilder.CreateLabel(settingsWindow, "Target Planet");
            UIToolsBuilder.CreateInputField(
                settingsWindow,
                "e.g. Mars",
                OnTargetPlanetChanged
            );

            // -- Target ship (used by Docking AP) --
            UIToolsBuilder.CreateLabel(settingsWindow, "Target Ship");
            UIToolsBuilder.CreateInputField(
                settingsWindow,
                "e.g. Station Alpha",
                OnTargetShipChanged
            );
        }

        // ── Settings callbacks ────────────────────────────────────────────────

        private static void OnOrbitAltitudeChanged(string raw)
        {
            if (double.TryParse(raw, out double parsed))
            {
                // Accept either raw metres (>= 1000) or kilometres (< 1000).
                double metres = parsed >= 1000.0 ? parsed : parsed * 1000.0;
                TargetOrbitAltitude = metres >= MIN_ORBIT_ALTITUDE
                    ? metres
                    : MIN_ORBIT_ALTITUDE;
            }
        }

        private static void OnTargetPlanetChanged(string value)
        {
            // Forward to InterplanetaryAutopilot when that field is implemented.
            Settings.data.targetPlanetName = value;
        }

        private static void OnTargetShipChanged(string value)
        {
            // Forward to DockingAutopilot when that field is implemented.
            Settings.data.targetShipName = value;
        }

        // ── Per-frame refresh (called by AutopilotUpdater.Update) ─────────────

        public static void Tick()
        {
            if (mainWindow == null) return;

            Rocket rocket = GetActiveRocket();

            orbitAP.SetRocket(rocket);
            deorbitAP.SetRocket(rocket);
            dockingAP.SetRocket(rocket);
            interplanetaryAP.SetRocket(rocket);

            // Auto-stop Orbit AP once the target orbit is reached.
            if (activeAP == ActiveAP.Orbit && HasReachedTargetOrbit())
            {
                StopAll();
                MsgDrawer.main.Log(
                    $"NOVA Autopilot: Target orbit of {TargetOrbitAltitude / 1000.0:F0} km reached.");
            }

            RefreshButtonVisibility();
        }

        // ── Button visibility / swap logic ────────────────────────────────────

        private static void RefreshButtonVisibility()
        {
            bool inOrbit = IsInOrbit();

            SetButtonVisible(orbitBtn,          !inOrbit);
            SetButtonVisible(deorbitBtn,         inOrbit);
            SetButtonVisible(dockingBtn,         inOrbit);
            SetButtonVisible(interplanetaryBtn,  inOrbit);

            RefreshButtonLabels(inOrbit);
        }

        private static void RefreshButtonLabels(bool inOrbit)
        {
            if (!inOrbit)
                SetButtonLabel(orbitBtn, activeAP == ActiveAP.Orbit
                    ? "Stop Orbit AP" : "Orbit AP");

            if (inOrbit)
            {
                SetButtonLabel(deorbitBtn,        activeAP == ActiveAP.Deorbit        ? "Stop Deorbit"        : "Deorbit");
                SetButtonLabel(dockingBtn,        activeAP == ActiveAP.Docking        ? "Stop Docking"        : "Docking");
                SetButtonLabel(interplanetaryBtn, activeAP == ActiveAP.Interplanetary ? "Stop Interplanetary" : "Interplanetary");
            }
        }

        // ── Button callbacks ──────────────────────────────────────────────────

        private static void OnOrbitClicked()
        {
            if (activeAP == ActiveAP.Orbit) { StopAll(); return; }
            StopAll();
            if (!orbitAP.PreLaunchCheck()) return;
            orbitAP.Start();
            activeAP = ActiveAP.Orbit;
        }

        private static void OnDeorbitClicked()
        {
            if (activeAP == ActiveAP.Deorbit) { StopAll(); return; }
            StopAll();
            deorbitAP.Start();
            activeAP = ActiveAP.Deorbit;
        }

        private static void OnDockingClicked()
        {
            if (activeAP == ActiveAP.Docking) { StopAll(); return; }
            StopAll();
            dockingAP.Start();
            activeAP = ActiveAP.Docking;
        }

        private static void OnInterplanetaryClicked()
        {
            if (activeAP == ActiveAP.Interplanetary) { StopAll(); return; }
            StopAll();
            if (!interplanetaryAP.PreLaunchCheck()) return;
            interplanetaryAP.Start();
            activeAP = ActiveAP.Interplanetary;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void StopAll()
        {
            orbitAP?.Stop();
            deorbitAP?.Stop();
            dockingAP?.Stop();
            interplanetaryAP?.Stop();
            activeAP = ActiveAP.None;
        }

        // True once both apo and pe have cleared the user's target altitude.
        private static bool HasReachedTargetOrbit()
        {
            Rocket rocket = GetActiveRocket();
            if (rocket?.location?.planet?.Value == null) return false;

            double planetR = rocket.location.planet.Value.Radius;
            double apoAlt  = rocket.physics.loader.apoapsis  - planetR;
            double peAlt   = rocket.physics.loader.periapsis - planetR;

            return apoAlt >= TargetOrbitAltitude && peAlt >= TargetOrbitAltitude;
        }

        // True once both apo and pe clear the base 31 km threshold (switches button set).
        private static bool IsInOrbit()
        {
            Rocket rocket = GetActiveRocket();
            if (rocket?.location?.planet?.Value == null) return false;

            double planetR = rocket.location.planet.Value.Radius;
            double apoAlt  = rocket.physics.loader.apoapsis  - planetR;
            double peAlt   = rocket.physics.loader.periapsis - planetR;

            return apoAlt >= MIN_ORBIT_ALTITUDE && peAlt >= MIN_ORBIT_ALTITUDE;
        }

        private static Rocket GetActiveRocket()
        {
            return PlayerController.main?.player?.Value as Rocket;
        }

        private static Button AddButton(UIWindow win, string label, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btnObj = UIToolsBuilder.CreateButton(win, label, onClick);
            return btnObj.GetComponent<Button>();
        }

        private static void SetButtonVisible(Button btn, bool visible)
        {
            if (btn != null)
                btn.gameObject.SetActive(visible);
        }

        private static void SetButtonLabel(Button btn, string label)
        {
            if (btn == null) return;
            var tmp = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (tmp != null) tmp.text = label;
        }

        private static void DestroyWindow(ref GameObject obj)
        {
            if (obj != null)
            {
                Object.Destroy(obj);
                obj = null;
            }
        }
    }
}