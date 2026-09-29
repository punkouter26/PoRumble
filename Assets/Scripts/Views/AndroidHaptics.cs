using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// A short, sized vibration on an Android phone, and nothing anywhere else.
    ///
    /// <see cref="Handheld.Vibrate"/> is a fixed, long buzz with no control over strength,
    /// which on every hit turns the phone into a pager. VibrationEffect.createOneShot takes a
    /// duration and an amplitude, so a counter can be a tap and a knockout a thump. It needs
    /// API 26; older devices fall back to Handheld.Vibrate for the knockout only.
    ///
    /// The reference to Handheld.Vibrate is also what makes Unity add the VIBRATE permission
    /// to the manifest - without it the JNI call would be refused by the OS and fail silently.
    /// </summary>
    internal sealed class AndroidHaptics
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const int ONE_SHOT_API = 26;

        private readonly AndroidJavaObject _vibrator;
        private readonly AndroidJavaClass _effectClass;
        private readonly int _sdk;
#endif

        internal AndroidHaptics()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                _sdk = version.GetStatic<int>("SDK_INT");

                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

                if (_vibrator != null && !_vibrator.Call<bool>("hasVibrator"))
                {
                    _vibrator.Dispose();
                    _vibrator = null;
                }

                if (_sdk >= ONE_SHOT_API)
                {
                    _effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                }
            }
            catch (AndroidJavaException)
            {
                // A device that will not hand over a vibrator simply has no haptics.
                _vibrator = null;
            }
#endif
        }

        /// <summary>True on a device that can vibrate.</summary>
        internal bool IsAvailable
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return _vibrator != null;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Vibrates for <paramref name="milliseconds"/> at <paramref name="strength"/>, 0..1.
        /// On a device too old for amplitude control only strong pulses are played, as the one
        /// fixed-length buzz the platform offers.
        /// </summary>
        internal void Pulse(int milliseconds, float strength)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_vibrator == null)
            {
                return;
            }

            if (_effectClass == null)
            {
                if (strength >= 0.8f)
                {
                    Handheld.Vibrate();
                }

                return;
            }

            int amplitude = Mathf.Clamp(Mathf.RoundToInt(strength * 255f), 1, 255);

            using AndroidJavaObject effect = _effectClass.CallStatic<AndroidJavaObject>(
                "createOneShot", (long)milliseconds, amplitude);

            _vibrator.Call("vibrate", effect);
#endif
        }

        internal void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_vibrator != null)
            {
                _vibrator.Dispose();
            }

            if (_effectClass != null)
            {
                _effectClass.Dispose();
            }
#endif
        }
    }
}
