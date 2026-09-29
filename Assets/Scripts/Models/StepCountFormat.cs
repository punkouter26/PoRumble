using System.Globalization;

namespace PoRumble.Models
{
    /// <summary>
    /// Prints a training step count the way the run's own logs are talked about: 520K, 3.9M.
    /// A raw 3900000 on a fight card is a number to count digits in, not a generation.
    /// </summary>
    public static class StepCountFormat
    {
        public static string Label(long steps)
        {
            if (steps <= 0)
            {
                return string.Empty;
            }

            return Compact(steps) + " STEPS";
        }

        public static string Compact(long steps)
        {
            if (steps >= 1_000_000)
            {
                return Trim(steps / 1_000_000d) + "M";
            }

            if (steps >= 1_000)
            {
                return Trim(steps / 1_000d) + "K";
            }

            return steps.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>One decimal below ten, none above: 3.9M reads, 21.4M is noise.</summary>
        private static string Trim(double value)
        {
            string format = value < 10d ? "0.#" : "0";
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
