using System;
using System.Globalization;

namespace PCL2Swap.Api
{
    /// <summary>
    /// Typed convenience over an addon's <see cref="IData"/> store: read with a fallback, write and
    /// persist in one call. The underlying store is per-addon, so keys never collide with other
    /// modules; values are stored as text and parsed back on read.
    /// </summary>
    public static class AddonSettings
    {
        public static bool GetBool(IData data, string key, bool fallback = false)
        {
            var raw = data?.Get(key);
            return bool.TryParse(raw, out var value) ? value : fallback;
        }

        public static void SetBool(IData data, string key, bool value)
        {
            data?.Set(key, value ? "true" : "false");
            data?.Save();
        }

        public static int GetInt(IData data, string key, int fallback = 0)
        {
            var raw = data?.Get(key);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        public static void SetInt(IData data, string key, int value)
        {
            data?.Set(key, value.ToString(CultureInfo.InvariantCulture));
            data?.Save();
        }

        public static double GetDouble(IData data, string key, double fallback = 0)
        {
            var raw = data?.Get(key);
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        public static void SetDouble(IData data, string key, double value)
        {
            data?.Set(key, value.ToString(CultureInfo.InvariantCulture));
            data?.Save();
        }

        public static string GetString(IData data, string key, string fallback = null) =>
            data?.Get(key, fallback);

        public static void SetString(IData data, string key, string value)
        {
            data?.Set(key, value);
            data?.Save();
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
