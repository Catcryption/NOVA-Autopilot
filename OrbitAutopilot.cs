using System;
using DeltaV_Calculator;
using HarmonyLib;
using SFS.UI;
using SFS.World;
using SmartSASMod;
using UnityEngine;

namespace NOVA_Autopilot
{
    public class OrbitAutopilot
    {
        private Rocket rocket;

        public bool IsActive { get; private set; }

        // ── Constructor ────────────────────────────────────────────────────────

        public OrbitAutopilot(Rocket rocket)
        {
            this.rocket = rocket;
        }

        public void SetRocket(Rocket rocket)
        {
            this.rocket = rocket;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        // Call before Start() to validate pre-launch conditions.
        // Returns true only if both checks pass.
        public bool PreLaunchCheck()
        {
            if (rocket == null) return false;

            bool hasTarget     = rocket.GetSAS().Target != null;
            double availableDV = DeltaV_Simulator.CalculateDV(rocket);
            double requiredDV  = GetAnaisRequiredDV();

            // requiredDV is 0 if ANAIS hasn't planned a transfer yet (no target / not computed).
            // In that case the ΔV check is skipped and only the target check applies.
            bool dvCheckValid  = requiredDV <= 0 || availableDV >= requiredDV;

            if (!hasTarget && !dvCheckValid)
            {
                MsgDrawer.main.Log(
                    "NOVA Autopilot: No target selected and insufficient DV. " +
                    "Select a target on the map and add more fuel stages.");
                return false;
            }

            if (!hasTarget)
            {
                MsgDrawer.main.Log(
                    "NOVA Autopilot: No target selected. " +
                    "Select a target on the map before launching.");
                return false;
            }

            if (!dvCheckValid)
            {
                MsgDrawer.main.Log(
                    $"NOVA Autopilot: Insufficient DV. " +
                    $"Have {availableDV:F0} m/s, need ~{requiredDV:F0} m/s. " +
                    "Add more fuel stages.");
                return false;
            }

            return true;
        }

        public void Start()
        {
            if (rocket == null) return;
            IsActive = true;
            Debug.Log("[OrbitAutopilot] Started");
        }

        public void Stop()
        {
            if (rocket != null)
            {
                SASComponent sas = rocket.GetSAS();
                sas.Direction = DirectionMode.Default;
                sas.Offset    = 0f;
            }
            IsActive = false;
            Debug.Log("[OrbitAutopilot] Stopped");
        }

        public void Update() { }

        // ── Main loop (runs every physics frame) ───────────────────────────────

        public void FixedUpdate()
        {
            if (!IsActive || rocket == null) return;

            double apoAlt    = GetApoapsisAltitude();
            float  scale     = GetDifficultyScale();
            SASComponent sas = rocket.GetSAS();

            // Once apoapsis clears 30 km (scaled), hand off to SSAS Target mode.
            if (apoAlt >= 30000 * scale)
            {
                sas.Direction = DirectionMode.Target;
                sas.Offset    = 0f;
                return;
            }

            // Gravity turn: Surface mode with a pitch offset toward the horizon.
            // Thresholds are for Normal (1:20) and scale linearly for other difficulties.
            float pitchOffset;
            if      (apoAlt < 350   * scale) pitchOffset = 5f;
            else if (apoAlt < 1100  * scale) pitchOffset = 10f;
            else if (apoAlt < 2500  * scale) pitchOffset = 30f;
            else if (apoAlt < 5000  * scale) pitchOffset = 45f;
            else if (apoAlt < 8250  * scale) pitchOffset = 50f;
            else if (apoAlt < 13000 * scale) pitchOffset = 60f;
            else if (apoAlt < 20000 * scale) pitchOffset = 75f;
            else                             pitchOffset = 90f;

            // Surface = straight up. Negative offset tilts toward prograde (east).
            // Flip the sign if the rocket tilts the wrong way on your launchpad.
            sas.Direction = DirectionMode.Surface;
            sas.Offset    = -pitchOffset;
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        // Reads the required DV for the current ANAIS transfer plan directly from
        // the same Traverse SmartSAS uses — no redundant math needed.
        // Returns 0 if ANAIS is not installed, no transfer is planned, or the
        // nav state is not ANAIS_TRANSFER_PLANNED (e.g. final approach / default).
        private static double GetAnaisRequiredDV()
        {
            try
            {
                Traverse traverse = Main.ANAISTraverse;
                if (traverse == null) return 0;

                // Only read the magnitude when a transfer is actually planned.
                if (traverse.Field("_navState").GetValue().ToString() != "ANAIS_TRANSFER_PLANNED")
                    return 0;

                Double2 dv = traverse.Field<Double2>("_relativeVelocity").Value;
                return dv.magnitude;
            }
            catch
            {
                return 0;
            }
        }

        // Returns the difficulty scale multiplier.
        // Adjust the switch values to match your SettingsData.cs.
        private float GetDifficultyScale()
        {
            switch (Settings.data.difficulty)
            {
                case Difficulty.Hard:       return 2f;   // 1:10 scale
                case Difficulty.Realistic:  return 20f;  // 1:1  scale
                default:                    return 1f;   // 1:20 scale (Normal)
            }
        }

        private double GetApoapsisAltitude()
        {
            if (rocket?.location?.planet?.Value == null) return 0;
            double planetR  = rocket.location.planet.Value.Radius;
            double apoapsis = rocket.physics.loader.apoapsis;
            return apoapsis - planetR;
        }
    }
}