using System;
using System.Linq;
using SFS.Parts;
using SFS.UI;
using SFS.World;
using SmartSASMod;
using UnityEngine;

namespace NOVA_Autopilot
{
    public enum DockingState
    {
        Idle,
        Aligning,   // Rotate until our port faces the target port
        Approaching,// Translate toward target at 1 m/s
        Docked
    }

    public class DockingAutopilot
    {
        private Rocket rocket;

        public bool         IsActive { get; private set; }
        public DockingState State    { get; private set; } = DockingState.Idle;

        // ── Constants ─────────────────────────────────────────────────────────

        // TODO: Replace with the exact part name/type string from Assembly-CSharp.
        // The check is case-insensitive and uses Contains(), so a substring works.
        private const string DOCKING_PORT_NAME = "dock";

        private const float  APPROACH_SPEED      = 1f;    // m/s - closing speed during approach
        private const float  ALIGN_TOLERANCE_DEG = 5f;    // degrees - attitude error to consider aligned
        private const float  FALLBACK_THROTTLE   = 0.001f;// 0.1% throttle if no RCS

        // ── Constructor ────────────────────────────────────────────────────────

        public DockingAutopilot(Rocket rocket)
        {
            this.rocket = rocket;
        }

        public void SetRocket(Rocket rocket)
        {
            this.rocket = rocket;
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        // Returns true if conditions are met to begin docking.
        public bool PreDockCheck()
        {
            if (rocket == null) return false;

            Rocket target = GetTargetRocket();
            if (target == null)
            {
                MsgDrawer.main.Log("NOVA Autopilot: No rocket target selected.");
                return false;
            }

            if (!HasDockingPort(rocket))
            {
                MsgDrawer.main.Log("NOVA Autopilot: This rocket has no docking port.");
                return false;
            }

            if (!HasDockingPort(target))
            {
                MsgDrawer.main.Log("NOVA Autopilot: Target rocket has no docking port.");
                return false;
            }

            return true;
        }

        public void Start()
        {
            if (rocket == null) return;
            IsActive = true;
            State    = DockingState.Aligning;
            Debug.Log("[DockingAutopilot] Started");
        }

        public void Stop()
        {
            if (rocket != null)
            {
                SetThrottle(0f);
                SetRCS(false);
                rocket.GetSAS().Direction = DirectionMode.Default;
                rocket.GetSAS().Offset    = 0f;
            }

            IsActive = false;
            State    = DockingState.Idle;
            Debug.Log("[DockingAutopilot] Stopped");
        }

        public void Update() { }

        // ── Main loop ─────────────────────────────────────────────────────────

        public void FixedUpdate()
        {
            if (!IsActive || rocket == null) return;

            Rocket target = GetTargetRocket();

            // No target means the rockets have merged - docking succeeded.
            if (target == null && State == DockingState.Approaching)
            {
                State = DockingState.Docked;
                MsgDrawer.main.Log("NOVA Autopilot: Docked.");
                Debug.Log("[DockingAutopilot] Docked - target deselected after merge");
                Stop();
                return;
            }

            switch (State)
            {
                // ── Phase 1: rotate our port to face the target port ──────────
                case DockingState.Aligning:
                {
                    SetThrottle(0f);
                    SetRCS(false);

                    if (target == null) break; // wait for a target to be selected

                    // Point toward the target using SmartSAS Target mode.
                    // The SAS will aim the nose; offset 0 means straight at target.
                    SASComponent sas = rocket.GetSAS();
                    sas.Direction = DirectionMode.Target;
                    sas.Offset    = 0f;

                    if (GetAttitudeError(target) <= ALIGN_TOLERANCE_DEG)
                    {
                        State = DockingState.Approaching;
                        Debug.Log("[DockingAutopilot] Aligned - beginning approach");
                    }
                    break;
                }

                // ── Phase 2: translate toward target at 1 m/s ─────────────────
                case DockingState.Approaching:
                {
                    if (target == null)
                    {
                        // Target lost before docking - cut thrust and wait.
                        SetThrottle(0f);
                        SetRCS(false);
                        break;
                    }

                    // Keep pointed at target while approaching.
                    SASComponent sas = rocket.GetSAS();
                    sas.Direction = DirectionMode.Target;
                    sas.Offset    = 0f;

                    double closingSpeed = GetClosingSpeed(target);

                    // Only thrust if we are not already closing fast enough.
                    if (closingSpeed < APPROACH_SPEED)
                    {
                        if (HasRCS(rocket))
                        {
                            SetRCS(true);
                            // RCS translation toward target (+forward).
                            // TODO: Confirm the axis/sign for your RCS API.
                            rocket.rcs.SetTranslation(new Vector2(0f, 1f));
                        }
                        else
                        {
                            SetRCS(false);
                            SetThrottle(FALLBACK_THROTTLE);
                        }
                    }
                    else
                    {
                        // At or above target speed - coast.
                        SetThrottle(0f);
                        if (HasRCS(rocket))
                        {
                            rocket.rcs.SetTranslation(Vector2.zero);
                        }
                    }
                    break;
                }

                case DockingState.Docked:
                    break;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        // Returns the SAS target cast to Rocket, or null if it is not a rocket.
        private Rocket GetTargetRocket()
        {
            object target = rocket?.GetSAS()?.Target;
            return target as Rocket;
        }

        // Case-insensitive substring search against the TODO part name.
        private static bool HasDockingPort(Rocket r)
        {
            if (r?.parts == null) return false;
            foreach (Part part in r.parts.parts)
            {
                if (part.name.IndexOf(DOCKING_PORT_NAME, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        // Returns the first docking port part on the rocket, or null.
        private static Part GetDockingPort(Rocket r)
        {
            if (r?.parts == null) return null;
            foreach (Part part in r.parts.parts)
            {
                if (part.name.IndexOf(DOCKING_PORT_NAME, StringComparison.OrdinalIgnoreCase) >= 0)
                    return part;
            }
            return null;
        }

        // Returns true if the rocket has at least one RCS part.
        // TODO: Replace "rcs" substring with the actual RCS part name if needed.
        private static bool HasRCS(Rocket r)
        {
            if (r?.parts == null) return false;
            foreach (Part part in r.parts.parts)
            {
                if (part.name.IndexOf("rcs", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        // Degrees between our current facing and the vector toward the target.
        private float GetAttitudeError(Rocket target)
        {
            if (target?.location == null || rocket?.location == null) return 180f;

            Double2 toTarget = target.location.position.Value - rocket.location.position.Value;
            Vector2 toTargetF = new Vector2((float)toTarget.x, (float)toTarget.y).normalized;

            // rocket.rb2d.transform.up is the rocket's nose direction in world space.
            Vector2 nose = rocket.rb2d.transform.up;

            return Vector2.Angle(nose, toTargetF);
        }

        // Positive = closing in on the target.
        private double GetClosingSpeed(Rocket target)
        {
            if (target?.location == null || rocket?.location == null) return 0;

            Double2 relPos = target.location.position.Value - rocket.location.position.Value;
            Double2 relVel = rocket.location.velocity.Value - target.location.velocity.Value;

            if (relPos.magnitude < 0.001) return 0;

            Double2 dir = relPos / relPos.magnitude;
            return Double2.Dot(relVel, dir);
        }

        private void SetThrottle(float value)
        {
            rocket.throttle.value = Mathf.Clamp01(value);
        }

        private void SetRCS(bool enabled)
        {
            // TODO: Confirm the correct property/method name for toggling RCS on Rocket.
            if (rocket?.rcs != null)
                rocket.rcs.enabled = enabled;
        }

        public string StateDescription
        {
            get
            {
                switch (State)
                {
                    case DockingState.Aligning:    return "Aligning";
                    case DockingState.Approaching: return "Approaching";
                    case DockingState.Docked:      return "Docked";
                    default:                       return "Idle";
                }
            }
        }
    }
}