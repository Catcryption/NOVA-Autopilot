using System;
using SFS.UI;
using SFS.World;
using SmartSASMod;
using UnityEngine;

namespace NOVA_Autopilot
{
    public enum DeorbitState
    {
        Idle,
        DeorbitBurn,    // Retrograde burn until periapsis <= 0 m
        WaitForSlowWarp,// Coasting - wait until timewarp <= 3x
        FullBurn,       // Full throttle until speed <= 10 m/s
        SoftDescent,    // Throttle-hold ~10 m/s to the surface
        Landed
    }

    public class DeorbitAutopilot
    {
        private Rocket rocket;

        public bool IsActive { get; private set; }
        public DeorbitState State { get; private set; } = DeorbitState.Idle;

        // ── Constants ─────────────────────────────────────────────────────────

        private const double PERIAPSIS_TARGET   = 0.0;   // m - burn until Pe is at or below sea level
        private const double FULL_BURN_SPEED    = 10.0;  // m/s - hand off to soft-descent below this
        private const double SOFT_DESCENT_SPEED = 10.0;  // m/s - target speed during soft descent
        private const int    MAX_WARP_INDEX     = 3;     // index in the game's warp table (3x)
        private const double LANDED_ALTITUDE    = 0.5;   // m - treat as landed below this

        // ── Constructor ────────────────────────────────────────────────────────

        public DeorbitAutopilot(Rocket rocket)
        {
            this.rocket = rocket;
        }

        public void SetRocket(Rocket rocket)
        {
            this.rocket = rocket;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        public void Start()
        {
            if (rocket == null) return;
            IsActive = true;
            State    = DeorbitState.DeorbitBurn;
            Debug.Log("[DeorbitAutopilot] Started");
        }

        public void Stop()
        {
            if (rocket != null)
            {
                SetThrottle(0f);
                rocket.GetSAS().Direction = DirectionMode.Default;
                rocket.GetSAS().Offset    = 0f;
            }
            IsActive = false;
            State    = DeorbitState.Idle;
            Debug.Log("[DeorbitAutopilot] Stopped");
        }

        public void Update() { }

        // ── Main loop ─────────────────────────────────────────────────────────

        public void FixedUpdate()
        {
            if (!IsActive || rocket == null) return;

            double altitude  = GetAltitude();
            double speed     = rocket.location.velocity.Value.magnitude;
            double periapsis = GetPeriapsisAltitude();

            switch (State)
            {
                // ── Phase 1: burn retrograde until Pe <= 0 m ──────────────────
                case DeorbitState.DeorbitBurn:
                {
                    PointRetrograde();

                    if (periapsis <= PERIAPSIS_TARGET)
                    {
                        SetThrottle(0f);
                        State = DeorbitState.WaitForSlowWarp;
                        Debug.Log($"[DeorbitAutopilot] Pe={periapsis:F0}m - waiting for warp <= {MAX_WARP_INDEX}x");
                        break;
                    }

                    SetThrottle(1f);
                    break;
                }

                // ── Phase 2: coast until timewarp is 3x or less ───────────────
                case DeorbitState.WaitForSlowWarp:
                {
                    SetThrottle(0f);

                    if (GetCurrentWarpIndex() <= MAX_WARP_INDEX)
                    {
                        State = DeorbitState.FullBurn;
                        Debug.Log("[DeorbitAutopilot] Warp slow enough - starting full burn");
                    }
                    break;
                }

                // ── Phase 3: full throttle until speed <= 10 m/s ─────────────
                case DeorbitState.FullBurn:
                {
                    PointRetrograde();

                    if (speed <= FULL_BURN_SPEED)
                    {
                        State = DeorbitState.SoftDescent;
                        Debug.Log($"[DeorbitAutopilot] Speed={speed:F1}m/s - switching to soft descent");
                        break;
                    }

                    SetThrottle(1f);
                    break;
                }

                // ── Phase 4: throttle-hold ~10 m/s until altitude = 0 ─────────
                case DeorbitState.SoftDescent:
                {
                    PointRetrograde();

                    if (altitude <= LANDED_ALTITUDE)
                    {
                        SetThrottle(0f);
                        rocket.GetSAS().Direction = DirectionMode.Default;
                        State = DeorbitState.Landed;
                        MsgDrawer.main.Log("NOVA Autopilot: Landed.");
                        Debug.Log("[DeorbitAutopilot] Landed");
                        Stop();
                        break;
                    }

                    // Simple proportional throttle: hold SOFT_DESCENT_SPEED
                    // Positive error = falling too fast -> increase throttle
                    double speedError = speed - SOFT_DESCENT_SPEED;
                    float  throttle   = Mathf.Clamp(0.5f + (float)(speedError / SOFT_DESCENT_SPEED), 0f, 1f);
                    SetThrottle(throttle);
                    break;
                }

                case DeorbitState.Landed:
                    break;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private void PointRetrograde()
        {
            SASComponent sas = rocket.GetSAS();
            sas.Direction = DirectionMode.Prograde;
            sas.Offset    = 180f;
        }

        private void SetThrottle(float value)
        {
            rocket.throttle.value = Mathf.Clamp01(value);
        }

        private double GetAltitude()
        {
            if (rocket?.location?.planet?.Value == null) return 0;
            return rocket.location.position.Value.magnitude - rocket.location.planet.Value.Radius;
        }

        private double GetPeriapsisAltitude()
        {
            if (rocket?.location?.planet?.Value == null) return 0;
            double planetR = rocket.location.planet.Value.Radius;
            return rocket.physics.loader.periapsis - planetR;
        }

        // Returns the current warp index (0 = no warp, 1 = 2x, 2 = 5x, 3 = 10x ... etc.)
        // Adjust if your SFS version uses a different warp API.
        private static int GetCurrentWarpIndex()
        {
            if (TimeWarp.main == null) return 0;
            return TimeWarp.main.warpIndex;
        }

        public string StateDescription
        {
            get
            {
                switch (State)
                {
                    case DeorbitState.DeorbitBurn:     return "Deorbit burn";
                    case DeorbitState.WaitForSlowWarp: return "Waiting for slow warp";
                    case DeorbitState.FullBurn:        return "Full burn";
                    case DeorbitState.SoftDescent:     return "Soft descent";
                    case DeorbitState.Landed:          return "Landed";
                    default:                           return "Idle";
                }
            }
        }
    }
}