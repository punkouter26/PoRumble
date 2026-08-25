namespace PoRumble.Models
{
    /// <summary>
    /// One arm's extension state machine. Left and right arms own separate instances,
    /// so they extend and cool down independently.
    /// </summary>
    public sealed class ArmModel
    {
        public ArmSide Side { get; }
        public ArmPhase Phase { get; private set; } = ArmPhase.Idle;

        /// <summary>0 = fully retracted, 1 = fully extended.</summary>
        public float Extension { get; private set; }

        /// <summary>
        /// How far the arm is cocked back before a charged punch, 0..1. Purely a telegraph:
        /// hit resolution still happens at full extension, so this changes what the swing
        /// looks like without changing what it does.
        /// </summary>
        public float Windup { get; private set; }

        /// <summary>
        /// Charge carried by the swing currently in flight, 0..1. Latched when the punch
        /// starts so that releasing the button mid-swing cannot rescale a punch that has
        /// already been committed to.
        /// </summary>
        public float ChargeLevel { get; private set; }

        /// <summary>True on the single tick the arm reaches full extension.</summary>
        public bool ReachedPeakThisTick { get; private set; }

        private float _phaseElapsed;

        public ArmModel(ArmSide side)
        {
            Side = side;
        }

        public bool CanPunch => Phase == ArmPhase.Idle;

        public void TryPunch()
        {
            TryPunch(0f);
        }

        /// <summary>Starts a swing carrying the given charge, 0 for an ordinary punch.</summary>
        public void TryPunch(float chargeLevel)
        {
            if (!CanPunch)
            {
                return;
            }

            ChargeLevel = Clamp01(chargeLevel);
            Windup = 0f;
            Phase = ArmPhase.Extending;
            _phaseElapsed = 0f;
        }

        /// <summary>
        /// Cocks the arm while its owner winds up. Ignored once the arm is swinging, so a
        /// charge that is still building cannot drag back a punch already on its way.
        /// </summary>
        public void SetWindup(float windup)
        {
            Windup = Phase == ArmPhase.Idle ? Clamp01(windup) : 0f;
        }

        public void Tick(float deltaTime, float extendDuration, float retractDuration, float cooldownDuration)
        {
            ReachedPeakThisTick = false;

            if (Phase == ArmPhase.Idle)
            {
                return;
            }

            _phaseElapsed += deltaTime;

            switch (Phase)
            {
                case ArmPhase.Extending:
                    Extension = extendDuration <= 0f ? 1f : Clamp01(_phaseElapsed / extendDuration);
                    if (_phaseElapsed >= extendDuration)
                    {
                        Extension = 1f;
                        ReachedPeakThisTick = true;
                        Phase = ArmPhase.Retracting;
                        _phaseElapsed = 0f;
                    }
                    break;

                case ArmPhase.Retracting:
                    Extension = retractDuration <= 0f ? 0f : 1f - Clamp01(_phaseElapsed / retractDuration);
                    if (_phaseElapsed >= retractDuration)
                    {
                        Extension = 0f;
                        Phase = ArmPhase.CoolingDown;
                        _phaseElapsed = 0f;
                    }
                    break;

                case ArmPhase.CoolingDown:
                    if (_phaseElapsed >= cooldownDuration)
                    {
                        Phase = ArmPhase.Idle;
                        ChargeLevel = 0f;
                        _phaseElapsed = 0f;
                    }
                    break;
            }
        }

        /// <summary>Forces the arm back to rest, e.g. when its owner is eliminated.</summary>
        public void ForceRetract()
        {
            Phase = ArmPhase.Idle;
            Extension = 0f;
            Windup = 0f;
            ChargeLevel = 0f;
            _phaseElapsed = 0f;
            ReachedPeakThisTick = false;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }
    }
}
