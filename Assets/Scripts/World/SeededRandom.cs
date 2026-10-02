using System.Collections.Generic;
using UnityEngine;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Deterministic pseudo-random number generator backed by System.Random.
    /// Uses FNV-1a hashing for platform-independent, stable seed derivation.
    /// Sub-streams are created via Hash(parentSeed, streamName) so adding a new
    /// system never alters the output of existing ones.
    /// </summary>
    public sealed class SeededRandom
    {
        private readonly System.Random _rng;
        private readonly int _seed;

        /// <summary>The integer seed this instance was created with.</summary>
        public int Seed => _seed;

        public SeededRandom(int seed)
        {
            _seed = seed;
            _rng = new System.Random(seed);
        }

        public SeededRandom(string textSeed)
            : this(HashString(textSeed))
        {
        }

        // ---------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------

        /// <summary>Returns a random int in [min, max) (exclusive upper bound, same as UnityEngine.Random.Range(int,int)).</summary>
        public int Range(int min, int max)
        {
            if (min >= max) return min;
            return _rng.Next(min, max);
        }

        /// <summary>Returns a random float in [min, max] (inclusive both ends).</summary>
        public float Range(float min, float max)
        {
            if (min >= max) return min;
            return min + (float)_rng.NextDouble() * (max - min);
        }

        /// <summary>Returns a random float in [0, 1).</summary>
        public float Value => (float)_rng.NextDouble();

        /// <summary>Returns a random point inside the unit circle (radius 1).</summary>
        public Vector2 InsideUnitCircle
        {
            get
            {
                // Rejection sampling (same technique Unity uses internally)
                Vector2 v;
                do
                {
                    v = new Vector2(Range(-1f, 1f), Range(-1f, 1f));
                } while (v.sqrMagnitude > 1f);
                return v;
            }
        }

        /// <summary>Returns true with the given probability [0-1].</summary>
        public bool Chance(float probability)
        {
            return Value < probability;
        }

        /// <summary>Picks a random element from the list.</summary>
        public T Pick<T>(IList<T> list)
        {
            if (list == null || list.Count == 0)
                return default;
            return list[Range(0, list.Count)];
        }

        /// <summary>Picks a random element from the array.</summary>
        public T Pick<T>(T[] array)
        {
            if (array == null || array.Length == 0)
                return default;
            return array[Range(0, array.Length)];
        }

        /// <summary>
        /// Weighted random pick. getWeight must return a non-negative weight for each element.
        /// </summary>
        public T WeightedPick<T>(IList<T> list, System.Func<T, float> getWeight)
        {
            if (list == null || list.Count == 0)
                return default;

            float total = 0f;
            for (int i = 0; i < list.Count; i++)
                total += Mathf.Max(0f, getWeight(list[i]));

            if (total <= 0f)
                return list[0];

            float roll = Value * total;
            for (int i = 0; i < list.Count; i++)
            {
                roll -= Mathf.Max(0f, getWeight(list[i]));
                if (roll <= 0f) return list[i];
            }
            return list[list.Count - 1];
        }

        // ---------------------------------------------------------------
        // FNV-1a hashing (32-bit, platform-independent)
        // ---------------------------------------------------------------

        private const uint FnvOffsetBasis = 2166136261u;
        private const uint FnvPrime = 16777619u;

        /// <summary>
        /// Hashes a string to a stable 32-bit integer.
        /// Unlike string.GetHashCode(), this gives identical results on every
        /// platform and .NET version.
        /// </summary>
        public static int HashString(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            uint hash = FnvOffsetBasis;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= FnvPrime;
            }
            return (int)hash;
        }

        /// <summary>
        /// Combines a parent seed with an integer index to produce a child seed.
        /// Used to derive chunk seeds: chunkSeed = Combine(mainSeed, chunkIndex).
        /// </summary>
        public static int Combine(int parentSeed, int index)
        {
            uint hash = (uint)parentSeed;
            // Mix in index bytes
            hash ^= (uint)(index & 0xFF);
            hash *= FnvPrime;
            hash ^= (uint)((index >> 8) & 0xFF);
            hash *= FnvPrime;
            hash ^= (uint)((index >> 16) & 0xFF);
            hash *= FnvPrime;
            hash ^= (uint)((index >> 24) & 0xFF);
            hash *= FnvPrime;
            return (int)hash;
        }

        /// <summary>
        /// Combines a parent seed with a stream name to produce a sub-stream seed.
        /// Used to derive per-system seeds: roadSeed = Combine(chunkSeed, "road").
        /// </summary>
        public static int Combine(int parentSeed, string streamName)
        {
            return Combine(parentSeed, HashString(streamName));
        }

        /// <summary>
        /// Convenience: creates a new SeededRandom for a named sub-stream of a parent seed.
        /// </summary>
        public SeededRandom SubStream(string streamName)
        {
            return new SeededRandom(Combine(_seed, streamName));
        }
    }
}
