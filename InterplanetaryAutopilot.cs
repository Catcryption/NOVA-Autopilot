using System;
using DeltaV_Calculator;
using HarmonyLib;
using SFS.UI;
using SFS.World;
using SmartSASMod;
using UnityEngine;

namespace NOVA_Autopilot
{
    public enum InterplanetaryState
    {
        Idle,
        WaitForOrbit,
        WarpToWindow,
        TransferBurn,
        Done
    }

    public class InterplanetaryAutopilot
    {
        private Rocket rocket;

        public bool IsActive { get; private set; }
        public InterplanetaryState State { get; private set; } = InterplanetaryState.Idle;

        // ── Constants ─────────────────────────────────────────────────────────

        private const double ORBIT_ALTITUDE_MIN = 31000.0; // m - both apo & pe must clear this
        private const double DONE_DV_THRESHOLD  = 1.0;     // m/s - close enough to zero

        // ── Constructor ────────────────────────────────────────────────────────

        public InterplanetaryAutopilot(Rocket rocket)
        {
            this.rocket = rocket;
        }

        public void SetRocket(Rocket rocket)
        {
            this.rocket = rocket;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public bool PreLaunchCheck()
        {
            if (rocket == null) return false;

            bool hasTarget     = rocket.GetSAS().Target != null;
            double availableDV = DeltaV_Simulator.CalculateDV(rocket);
            double requiredDV  = GetAnaisRequiredDV();

            bool dvCheckValid = requiredDV <= 0 || availableDV >= requiredDV;

            if (!hasTarget && !dvCheckValid)
            {
                MsgDrawer.main.Log(
                    "NOVA Autopilot: No target selected and insufficient DV. " +
                    "Select a planet/star on the map and add more fuel stages.");
                return false;
            }

            if (!hasTarget)
            {
                MsgDrawer.main.Log(
                    "NOVA Autopilot: No target selected. " +
                    "Select a planet or star on the map before launching.");
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
            State    = InterplanetaryState.WaitForOrbit;
            Debug.Log("[InterplanetaryAutopilot] Started");
        }

        public void Stop()
        {
            if (rocket != null)
            {
                SetThrottle(0f);
                SASComponent sas = rocket.GetSAS();
                sas.Direction = DirectionMode.Default;
                sas.Offset    = 0f;
            }
            IsActive = false;
            State    = InterplanetaryState.Idle;
            Debug.Log("[InterplanetaryAutopilot] Stopped");
        }

        public void Update() { }

        // ── Main loop ─────────────────────────────────────────────────────────

        public void FixedUpdate()
        {
            if (!IsActive || rocket == null) return;

            switch (State)
            {
                // ── Phase 1: wait until the rocket is in orbit ────────────────
                case InterplanetaryState.WaitForOrbit:
                {
                    SetThrottle(0f);

                    if (IsInOrbit())
                    {
                        State = InterplanetaryState.WarpToWindow;
                        Debug.Log("[InterplanetaryAutopilot] In orbit - warping to transfer window");
                    }
                    break;
                }

                // ── Phase 2: timewarp until ANAIS marks the window open ───────
                case InterplanetaryState.WarpToWindow:
                {
                    SetThrottle(0f);

                    if (IsTransferWindowReady())
                    {
                        SetWarp(0);
                        State = InterplanetaryState.TransferBurn;
                        Debug.Log("[InterplanetaryAutopilot] Transfer window open - starting burn");
                        break;
                    }

                    SetWarp(GetMaxWarpIndex());
                    break;
                }

                // ── Phase 3: point at target and burn until DV = 0 ───────────
                case InterplanetaryState.TransferBurn:
                {
                    double requiredDV = GetAnaisRequiredDV();

                    if (requiredDV <= DONE_DV_THRESHOLD)
                    {
                        SetThrottle(0f);
                        rocket.GetSAS().Direction = DirectionMode.Default;
                        State = InterplanetaryState.Done;
                        MsgDrawer.main.Log("NOVA Autopilot: Transfer burn complete.");
                        Debug.Log("[InterplanetaryAutopilot] Transfer burn complete");
                        Stop();
                        break;
                    }

                    SASComponent sas = rocket.GetSAS();
                    sas.Direction = DirectionMode.Target;
                    sas.Offset    = 0f;
                    SetThrottle(1f);
                    break;
                }

                case InterplanetaryState.Done:
                    break;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private bool IsInOrbit()
        {
            double planetR = rocket?.location?.planet?.Value?.Radius ?? 0;
            if (planetR == 0) return false;

            double apoAlt = rocket.physics.loader.apoapsis  - planetR;
            double peAlt  = rocket.physics.loader.periapsis - planetR;

            return apoAlt >= ORBIT_ALTITUDE_MIN && peAlt >= ORBIT_ALTITUDE_MIN;
        }

        private static bool IsTransferWindowReady()
        {
            try
            {
                Traverse traverse = Main.ANAISTraverse;
                if (traverse == null) return false;

                return traverse.Field("_navState").GetValue().ToString() == "ANAIS_TRANSFER_PLANNED";
            }
            catch
            {
                return false;
            }
        }

        private static double GetAnaisRequiredDV()
        {
            try
            {
                Traverse traverse = Main.ANAISTraverse;
                if (traverse == null) return 0;

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

        private static void SetWarp(int index)
        {
            if (TimeWarp.main == null) return;
            TimeWarp.main.warpIndex = index;
        }

        private static int GetMaxWarpIndex()
        {
            if (TimeWarp.main == null) return 0;
            return TimeWarp.main.GetMaxWarpIndex(); // adjust if this method name differs in your SFS version
        }

        private void SetThrottle(float value)
        {
            rocket.throttle.value = Mathf.Clamp01(value);
        }

        public string StateDescription
        {
            get
            {
                switch (State)
                {
                    case InterplanetaryState.WaitForOrbit:  return "Waiting for orbit";
                    case InterplanetaryState.WarpToWindow:  return "Warping to transfer window";
                    case InterplanetaryState.TransferBurn:  return "Transfer burn";
                    case InterplanetaryState.Done:          return "Done";
                    default:                                return "Idle";
                }
            }
        }
    }
}