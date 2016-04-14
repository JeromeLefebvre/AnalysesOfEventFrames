using OSIsoft.AF.Asset;
using System;

namespace EventFrameAnalysis
{
    class Statistics
    {
        public static double StandardDeviation(AFValues values)
        {
            // Returns the standard deviation of a collection of AFValues
            // As the timestamp for each value may vary, a time stamp must also be provided to return an AFValue
            double M = 0.0;
            double S = 0.0;
            int k = 1;
            foreach (AFValue value in values)
            {
                double rawValue = (double)value.Value;
                double previousM = M;
                M += (rawValue - previousM) / k;
                S += (rawValue - previousM) * (rawValue - M);
                k++;
            }
            // S can be zero if all values are identical
            return S == 0 ? 0 : Math.Sqrt(S / (k - 2));
        }

        public static double Mean(AFValues values)
        {
            // Returns the mean of a collection of AFValues
            // As the timestamp for each value may vary, a timestamp must also be provided to return an AFValue
            double total = 0;
            foreach (AFValue value in values)
            {
                // The data stored in the attribute is of type Float32, thus possible to convert to double 
                // Any shutdown or bad data is thrown away
                if (value.IsGood)
                {
                    total += value.ValueAsDouble();
                }
            }
            int count = values.Count;
            return total / count;
        }
    }
}
