using System;
using System.Numerics;

namespace PuppetMasterKK;

// Whether a player is walking or running, from where they are over time. The game only keeps a walk flag for your
// own character, so other players' pace comes from their ground speed: running is about 6 yalms a second, walking
// about 2.4.
internal sealed class PaceEstimator
{
    // Slower than this is walking, faster is running (mounts and sprinting are faster still).
    public const float WalkBelow = 4f;
    // Slower than this is standing still: the last pace is kept.
    public const float MovingAbove = 0.8f;
    // Measure over at least this long, so one frame's jitter can't flip the pace.
    public const double SampleSeconds = 0.3;
    // A gap longer than this (they were out of sight) starts over.
    public const double StaleSeconds = 2.0;

    private Vector3 lastPosition;
    private double lastSeconds;
    private bool hasSample;

    // True: walking. False: running. Null: not seen moving yet.
    public bool? Walking { get; private set; }

    public void Reset()
    {
        hasSample = false;
        Walking = null;
    }

    public void Add(Vector3 position, double seconds)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Z))
            return;
        if (!hasSample)
        {
            Take(position, seconds);
            return;
        }
        var elapsed = seconds - lastSeconds;
        if (elapsed < SampleSeconds)
            return;
        if (elapsed > StaleSeconds)
        {
            Take(position, seconds);
            return;
        }
        var ground = new Vector2(position.X - lastPosition.X, position.Z - lastPosition.Z);
        var speed = ground.Length() / elapsed;
        Take(position, seconds);
        if (speed >= MovingAbove)
            Walking = speed < WalkBelow;
    }

    private void Take(Vector3 position, double seconds)
    {
        lastPosition = position;
        lastSeconds = seconds;
        hasSample = true;
    }
}
