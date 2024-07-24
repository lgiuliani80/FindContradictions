using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindContradictions
{
    public static class VectorExtensions
    {
        public static float DistanceTo(this ReadOnlyMemory<float> source, ReadOnlyMemory<float> dest)
        {
            float distance = 0;

            for (int i = 0; i < source.Length; i++) 
            {
                float diff = source.Span[i] - dest.Span[i];
                distance += diff * diff;
            }

            return MathF.Sqrt(distance);
        }

        public static float CosAngleTo(this ReadOnlyMemory<float> source, ReadOnlyMemory<float> dest)
        {
            float dotProduct = 0;
            float sourceLength = 0;
            float destLength = 0;

            for (int i = 0; i < source.Length; i++)
            {
                dotProduct += source.Span[i] * dest.Span[i];
                sourceLength += source.Span[i] * source.Span[i];
                destLength += dest.Span[i] * dest.Span[i];
            }

            return dotProduct / (MathF.Sqrt(sourceLength) * MathF.Sqrt(destLength));
        }
    }
}
