using System.Collections.Generic;
using System.Text;
using Unity.InferenceEngine;
using UnityEngine;

namespace PoRumble.Views
{
    /// <summary>
    /// Decides whether a training checkpoint can drive a boxer in this build.
    ///
    /// A checkpoint is only usable if it takes exactly the observations the shipped policy
    /// takes. That is not hypothetical here: every policy committed before the perception fix
    /// was compiled against a 10- or 11-wide vector, and the prefab now writes 15. ML-Agents
    /// does run its own check, but only in the inspector; at runtime a mismatched model fails
    /// on every decision instead of once, and the fighter stands in the ring doing nothing.
    ///
    /// So the checkpoint's input and output signature is compared against the shipped
    /// model's once, when it is first seated, and a mismatch falls back to the shipped policy
    /// with one warning naming what differed. Signatures rather than a hard-coded "15":
    /// whatever the prefab's model takes is by definition what this build can feed.
    ///
    /// A plain class owned by <see cref="BoxerSpawnPoints"/>, which is the one place seating
    /// happens; the cache lives as long as the scene does.
    /// </summary>
    internal sealed class PolicyCheckpointValidator
    {
        private readonly ModelAsset _reference;
        private readonly Dictionary<ModelAsset, bool> _verdicts = new();
        private string _referenceSignature;

        public PolicyCheckpointValidator(ModelAsset reference)
        {
            _reference = reference;
        }

        /// <summary>
        /// The checkpoint to run for a fighter, or null to run the shipped policy - either
        /// because none was asked for or because the one asked for cannot be fed.
        /// </summary>
        public ModelAsset Resolve(ModelAsset checkpoint)
        {
            if (checkpoint == null || checkpoint == _reference)
            {
                return null;
            }

            if (!_verdicts.TryGetValue(checkpoint, out bool usable))
            {
                usable = Check(checkpoint);
                _verdicts[checkpoint] = usable;
            }

            return usable ? checkpoint : null;
        }

        private bool Check(ModelAsset checkpoint)
        {
            if (_reference == null)
            {
                Debug.LogWarning(
                    $"[PoRumble] Cannot check checkpoint '{checkpoint.name}': the boxer prefab " +
                    "has no shipped model to compare it against. Running the shipped policy.");
                return false;
            }

            _referenceSignature ??= Signature(_reference);
            string candidate = Signature(checkpoint);

            if (candidate == _referenceSignature)
            {
                return true;
            }

            Debug.LogWarning(
                $"[PoRumble] Checkpoint '{checkpoint.name}' does not match the observations and " +
                $"actions this build feeds its policy, so that fighter runs the shipped " +
                $"'{_reference.name}' instead.\n  expected: {_referenceSignature}\n  checkpoint: {candidate}");
            return false;
        }

        /// <summary>Every input's name and shape, then every output's name, in model order.</summary>
        private static string Signature(ModelAsset asset)
        {
            Model model;

            try
            {
                model = ModelLoader.Load(asset);
            }
            catch (System.Exception exception)
            {
                return "unloadable: " + exception.Message;
            }

            StringBuilder builder = new(128);

            for (int index = 0; index < model.inputs.Count; index++)
            {
                Model.Input input = model.inputs[index];
                builder.Append(input.name).Append(input.shape.ToString()).Append(' ');
            }

            builder.Append("->");

            for (int index = 0; index < model.outputs.Count; index++)
            {
                builder.Append(' ').Append(model.outputs[index].name);
            }

            return builder.ToString();
        }
    }
}
