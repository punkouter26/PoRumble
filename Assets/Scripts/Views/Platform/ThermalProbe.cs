using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// How hot the phone is: Android's thermal status and the battery temperature.
    ///
    /// Thermal status is the OS's own verdict (API 29+) and is the one that predicts
    /// throttling - "moderate" is where most devices start pulling clocks. Battery temperature
    /// is a sensor reading, available on every Android version, and the only warning an older
    /// phone gives. Both are read through JNI and both allocate, so the caller polls every few
    /// seconds rather than every frame.
    ///
    /// Everywhere else both read as unknown: -1 and NaN.
    /// </summary>
    internal sealed class ThermalProbe
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private const int THERMAL_STATUS_API = 29;

        private readonly AndroidJavaObject _activity;
        private readonly AndroidJavaObject _powerManager;
#endif

        internal ThermalProbe()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                _activity = player.GetStatic<AndroidJavaObject>("currentActivity");

                using var version = new AndroidJavaClass("android.os.Build$VERSION");

                if (version.GetStatic<int>("SDK_INT") >= THERMAL_STATUS_API)
                {
                    _powerManager = _activity.Call<AndroidJavaObject>("getSystemService", "power");
                }
            }
            catch (AndroidJavaException)
            {
                _activity = null;
                _powerManager = null;
            }
#endif
        }

        /// <summary>PowerManager.getCurrentThermalStatus, 0..6, or -1 where it is not available.</summary>
        internal int ReadThermalStatus()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_powerManager == null)
            {
                return -1;
            }

            try
            {
                return _powerManager.Call<int>("getCurrentThermalStatus");
            }
            catch (AndroidJavaException)
            {
                return -1;
            }
#else
            return -1;
#endif
        }

        /// <summary>
        /// Battery temperature in Celsius, or NaN. Read from the sticky BATTERY_CHANGED
        /// broadcast, which needs no receiver and no permission.
        /// </summary>
        internal float ReadBatteryCelsius()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_activity == null)
            {
                return float.NaN;
            }

            // Raw JNI rather than AndroidJavaObject.Call, because the receiver argument has to be
            // null, and Call infers each argument's Java type from its value - a null has no
            // type, so the overload lookup for registerReceiver fails.
            try
            {
                using var filter = new AndroidJavaObject(
                    "android.content.IntentFilter", "android.intent.action.BATTERY_CHANGED");

                System.IntPtr register = AndroidJNIHelper.GetMethodID(
                    _activity.GetRawClass(),
                    "registerReceiver",
                    "(Landroid/content/BroadcastReceiver;Landroid/content/IntentFilter;)Landroid/content/Intent;");

                jvalue[] registerArgs = new jvalue[2];
                registerArgs[0].l = System.IntPtr.Zero;
                registerArgs[1].l = filter.GetRawObject();

                System.IntPtr intent = AndroidJNI.CallObjectMethod(_activity.GetRawObject(), register, registerArgs);

                if (AndroidJNI.ExceptionOccurred() != System.IntPtr.Zero)
                {
                    AndroidJNI.ExceptionClear();
                    return float.NaN;
                }

                if (intent == System.IntPtr.Zero)
                {
                    return float.NaN;
                }

                System.IntPtr intentClass = AndroidJNI.GetObjectClass(intent);
                System.IntPtr getIntExtra = AndroidJNI.GetMethodID(intentClass, "getIntExtra", "(Ljava/lang/String;I)I");
                System.IntPtr key = AndroidJNI.NewStringUTF("temperature");

                jvalue[] extraArgs = new jvalue[2];
                extraArgs[0].l = key;
                extraArgs[1].i = int.MinValue;

                // Tenths of a degree.
                int tenths = AndroidJNI.CallIntMethod(intent, getIntExtra, extraArgs);

                AndroidJNI.DeleteLocalRef(key);
                AndroidJNI.DeleteLocalRef(intentClass);
                AndroidJNI.DeleteLocalRef(intent);

                return tenths == int.MinValue ? float.NaN : tenths / 10f;
            }
            catch (AndroidJavaException)
            {
                return float.NaN;
            }
#else
            return float.NaN;
#endif
        }

        /// <summary>The status as the word Android's documentation uses for it.</summary>
        internal static string Describe(int status)
        {
            switch (status)
            {
                case 0: return "none";
                case 1: return "light";
                case 2: return "moderate";
                case 3: return "severe";
                case 4: return "critical";
                case 5: return "emergency";
                case 6: return "shutdown";
                default: return "n/a";
            }
        }

        internal void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_powerManager != null)
            {
                _powerManager.Dispose();
            }

            if (_activity != null)
            {
                _activity.Dispose();
            }
#endif
        }
    }
}
