using System;
using Puffin.Core.Events;

namespace Puffin.Core.Ecs.Components
{
    /// <summary>
    /// Allows an entity to play an audio file (short or long), optionally at a modified pitch.
    /// You should be able to play WAV files, OGG files, and MP3 files.
    /// </summary>
    public class AudioComponent : Component
    {
        public float Volume
        {
            get
            {
                return _volume;
            }
            set
            {
                _volume = value;
                this.Parent.Scene.EventBus.Broadcast(EventBusSignal.VolumeChanged, this);
            }
        }

        internal readonly string FileName;
        internal float Pitch = 0f;
        internal bool ShouldLoop = false;

        private float _volume = 1.0f;

        public AudioComponent(Entity parent, string fileName, bool shouldLoop = false) : base(parent)
        {
            this.FileName = fileName;
            this.ShouldLoop = shouldLoop;
        }

        /// <summary>
        /// Plays the audio file specified in <c>fileName</c> at the specified volume and pitch.
        /// </summary>
        /// <param name="volume">A volume of 1.0 is 100%; a volume of 0 is 0% (completely muted).</param>
        /// <param name="pitch">A pitch of 0 is 100% (normal); -1 plays at half the pitch, 1 plays at double pitch.</param>
        public void Play(float? volume = null, float? pitch = null)
        {
            if (volume.HasValue)
            {
                if (volume.Value < 0 || volume.Value > 1)
                {
                    throw new ArgumentException("Volume must be in the range [0..1]");
                }
                _volume = volume.Value;
            }

            if (pitch.HasValue)
            {
                if (pitch.Value < -1 || pitch.Value > 1)
                {
                    throw new ArgumentException("Pitch must be in the range [-1..1].");
                }
                this.Pitch = pitch.Value;
            }

            this.Parent.Scene.EventBus.Broadcast(EventBusSignal.PlayAudio, this);
        }

        /// <summary>
        /// Stops all instances of the current sound effect that are playing.
        /// </summary>
        public void Stop()
        {
            this.Parent.Scene.EventBus.Broadcast(EventBusSignal.StopAudio, this);
        }
    }
}