using System.Collections.Generic;
using System.Linq;
using OSIsoft.AF;
using OSIsoft.AF.EventFrame;
using OSIsoft.AF.Time;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using OSIsoft.AF.PI;
using OSIsoft.AF.Search;

namespace EventFrameAnalysis
{
    class LimitCalculation
    {
        static PISystem pisystem = null;
        static AFDatabase afdatabse = null;
        static AFEnumerationValue nodata;
        static AFTimeSpan interval = new AFTimeSpan(seconds: 1);

        static AFEventFrameSearch eventFrameQuery;
        static AFEventFrameSearch timeLessQuery;
        static AFAttribute sensor;
        static List<AFValues> bounds = new List<AFValues> { new AFValues(), new AFValues() };
        static List<AFAttribute> boundAttributes;

        public LimitCalculation(string afattributepath, string eventQuery)
        {
            sensor = AFAttribute.FindAttribute(afattributepath, null);
            pisystem = sensor.PISystem;
            afdatabse = sensor.Database;
            nodata = (new PIServers().DefaultPIServer).StateSets["SYSTEM"]["NO Data"];
            boundAttributes = new List<AFAttribute> { sensor.GetAttributeByTrait(AFAttributeTrait.LimitLoLo), sensor.GetAttributeByTrait(AFAttributeTrait.LimitHiHi) };

            eventFrameQuery = new AFEventFrameSearch(afdatabse, "eventFrameSearch", eventQuery);
            List<AFSearchToken> tokens = eventFrameQuery.Tokens.ToList();
            tokens.RemoveAll(t => t.Filter == AFSearchFilter.InProgress || t.Filter == AFSearchFilter.Start || t.Filter == AFSearchFilter.End);
            timeLessQuery = new AFEventFrameSearch(afdatabse, "AllEventFrames", tokens);
            InitialRun();
        }

        internal static void InitialRun()
        {
            ComputeStatistics();
            AFEventFrameSearch currentEventFrameQuery = new AFEventFrameSearch(afdatabse, "currentEvent", eventFrameQuery.Tokens.ToList());
            currentEventFrameQuery.Tokens.Add(new AFSearchToken(AFSearchFilter.InProgress, AFSearchOperator.Equal, "True"));
            IEnumerable<AFEventFrame> currentEventFrames = currentEventFrameQuery.FindEventFrames(0, true, int.MaxValue);
            foreach (AFEventFrame currentEventFrame in currentEventFrames)
            {
                WriteValues(currentEventFrame.StartTime);
            }
        }

        internal static void ComputeStatistics()
        {
            IEnumerable<AFEventFrame> eventFrames = eventFrameQuery.FindEventFrames(0, true, int.MaxValue);
            List<AFValues> trends = new List<AFValues>();
            foreach (AFEventFrame EF in eventFrames)
            {
                trends.Add(sensor.Data.InterpolatedValues(EF.TimeRange, interval, null, "", true));
            }
            List<AFValues> slices = GetSlices(trends);

            bounds[0].Clear();
            bounds[1].Clear();
            foreach (AFValues slice in slices)
            {
                IDictionary<AFSummaryTypes, AFValue> statisticForSlice = GetStatistics(slice);
                AFTime time = statisticForSlice[AFSummaryTypes.Average].Timestamp;
                double mean = statisticForSlice[AFSummaryTypes.Average].ValueAsDouble();
                double stddev = statisticForSlice[AFSummaryTypes.StdDev].ValueAsDouble();
                bounds[0].Add(new AFValue(mean - 3 * stddev, time));
                bounds[1].Add(new AFValue(mean + 3 * stddev, time));
            }
        }

        internal static void WriteValues(AFTime startTime)
        {
            AFValue nodataValue = new AFValue(nodata);
            for (int i = 0; i < 2; i++)
            {
                nodataValue.Timestamp = timeShift(bounds[i], startTime);
                boundAttributes[i].PIPoint.UpdateValues(bounds[i], AFUpdateOption.Insert);
                boundAttributes[i].PIPoint.UpdateValue(nodataValue, AFUpdateOption.Insert);
            }
        }

        public static IDictionary<AFSummaryTypes, AFValue> GetStatistics(AFValues values)
        {
            AFTimeRange range = new AFTimeRange(values[0].Timestamp, values[values.Count - 1].Timestamp);
            return values.Summary(range, AFSummaryTypes.Average | AFSummaryTypes.StdDev, AFCalculationBasis.EventWeighted, AFTimestampCalculation.MostRecentTime);
        }

        internal static AFTime timeShift(AFValues values, AFTime startTime)
        {
            foreach (AFValue value in values)
            {
                value.Timestamp = startTime;
                startTime += interval;
            }
            return startTime;
        }

        public static List<AFValues> GetSlices(List<AFValues> trends)
        {
            List<AFValues> outer = new List<AFValues>();
            for (int j = 0; j < trends.Count; j++)
            {
                for (int i = 0; i < trends[j].Count; i++)
                {
                    if (outer.Count <= i)
                        outer.Add(new AFValues());
                    outer[i].Add(trends[j][i]);
                }
            }
            return outer;
        }

        public void performAction(AFEventFrame lastestEventFrame, AFChangeInfoAction action)
        {
            if (timeLessQuery.IsMatch(lastestEventFrame))
            {
                if (action == AFChangeInfoAction.Added)
                {
                    WriteValues(lastestEventFrame.StartTime);
                }
                else if (action == AFChangeInfoAction.Updated || action == AFChangeInfoAction.Removed)
                {
                    ComputeStatistics();
                }
            }
        }
    }
}
