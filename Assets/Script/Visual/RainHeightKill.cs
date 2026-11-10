using UnityEngine;

namespace MrTGame.Visual
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class RainHeightKill : MonoBehaviour
    {
        [SerializeField] Vector2 worldDeathHeightRange = new Vector2(-2.6f, -2.4f);

        ParticleSystem rainParticles;
        ParticleSystem.Particle[] particleBuffer = new ParticleSystem.Particle[0];

        void Awake()
        {
            rainParticles = GetComponent<ParticleSystem>();
        }

        void OnValidate()
        {
            worldDeathHeightRange = new Vector2(
                Mathf.Min(worldDeathHeightRange.x, worldDeathHeightRange.y),
                Mathf.Max(worldDeathHeightRange.x, worldDeathHeightRange.y));
        }

        void LateUpdate()
        {
            if (rainParticles == null)
                rainParticles = GetComponent<ParticleSystem>();

            ParticleSystem.MainModule main = rainParticles.main;
            if (particleBuffer.Length < main.maxParticles)
                particleBuffer = new ParticleSystem.Particle[main.maxParticles];

            int particleCount = rainParticles.GetParticles(particleBuffer);
            bool changed = false;
            float terminalLifetime = Mathf.Max(Time.deltaTime * 0.25f, 0.0001f);

            for (int i = 0; i < particleCount; i++)
            {
                ParticleSystem.Particle particle = particleBuffer[i];
                float worldY = GetWorldPosition(particle.position, main).y;
                float deathY = Mathf.Lerp(
                    worldDeathHeightRange.x,
                    worldDeathHeightRange.y,
                    HashToUnitFloat(particle.randomSeed));

                if (worldY > deathY || particle.remainingLifetime <= terminalLifetime)
                    continue;

                particle.remainingLifetime = terminalLifetime;
                particleBuffer[i] = particle;
                changed = true;
            }

            if (changed)
                rainParticles.SetParticles(particleBuffer, particleCount);
        }

        Vector3 GetWorldPosition(Vector3 particlePosition, ParticleSystem.MainModule main)
        {
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.Local:
                    return transform.TransformPoint(particlePosition);
                case ParticleSystemSimulationSpace.Custom:
                    return main.customSimulationSpace != null
                        ? main.customSimulationSpace.TransformPoint(particlePosition)
                        : particlePosition;
                default:
                    return particlePosition;
            }
        }

        static float HashToUnitFloat(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            return (value & 0x00ffffff) / 16777215f;
        }
    }
}
