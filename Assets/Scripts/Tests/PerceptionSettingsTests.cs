using NUnit.Framework;
using UnityEngine;

namespace PoRumble.Tests
{
    /// <summary>
    /// Guards the one project setting the ray sensor's usefulness depends on.
    ///
    /// RayPerceptionSensorComponent2D casts from the transform it sits on, which here is the
    /// Torso - the same GameObject that carries the boxer's own body collider, tagged Boxer.
    /// The sensor does nothing to exclude the caster, so with queriesStartInColliders on,
    /// every ray returns the agent's own torso at fraction 0 and the whole ray observation
    /// collapses to a constant: "a boxer is touching me, in all seventeen directions."
    /// The agent is then blind to opponents and walls alike, and only its eleven self
    /// scalars carry any signal at all.
    /// </summary>
    public sealed class PerceptionSettingsTests
    {
        [Test]
        public void CastsDoNotHitTheColliderTheyStartInside()
        {
            Assert.That(Physics2D.queriesStartInColliders, Is.False,
                "Physics2D.queriesStartInColliders must stay off, or every boxer's ray " +
                "sensor reads its own torso at zero distance and perceives nothing else");
        }

        /// <summary>
        /// The setting above is only half of it: the cast must actually reach past the body
        /// it starts in. This reproduces the sensor's own cast - a circle cast of the
        /// configured radius, from the centre of a body collider - and asserts it finds the
        /// thing behind rather than the thing it started in.
        /// </summary>
        [Test]
        public void ARayFromInsideABodySeesTheBoxerBeyondIt()
        {
            const float BODY_RADIUS = 0.71f;
            const float CAST_RADIUS = 0.1f;

            GameObject self = NewBody("SelfBody", Vector2.zero, BODY_RADIUS);
            GameObject other = NewBody("OtherBody", new Vector2(0f, 4f), BODY_RADIUS);
            Physics2D.SyncTransforms();

            try
            {
                RaycastHit2D hit = Physics2D.CircleCast(
                    Vector2.zero, CAST_RADIUS, Vector2.up, 14f, Physics2D.AllLayers);

                Assert.That(hit.collider, Is.Not.Null, "the ray should reach the other boxer");
                Assert.That(hit.collider.gameObject, Is.EqualTo(other),
                    "the ray must see past the body it started inside");
                Assert.That(hit.fraction, Is.GreaterThan(0f),
                    "a fraction of zero means the cast stopped on its own collider");
            }
            finally
            {
                Object.DestroyImmediate(self);
                Object.DestroyImmediate(other);
            }
        }

        private static GameObject NewBody(string name, Vector2 position, float radius)
        {
            GameObject body = new(name);
            body.transform.position = position;
            body.AddComponent<CircleCollider2D>().radius = radius;
            return body;
        }
    }
}
