#region Copyright
//  Copyright 2016 OSIsoft, LLC
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
#endregion

using System;
using System.Timers;
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
    class Program
    {
        static Timer refreshTimer = new Timer(1000);
        static object cookie;
        static ElapsedEventHandler elapsedEH = null;
        static EventHandler<AFChangedEventArgs> changedEH = null;

        static PISystem pisystem = null;
        static AFDatabase afdatabse = null;
        static AFEnumerationValue nodata;
        static AFTimeSpan interval = new AFTimeSpan(seconds: 1);

        static AFEventFrameSearch eventFrameQuery;
        static AFEventFrameSearch currentEventFrameQuery;
        static AFAttribute sensor;
        static List<AFValues> bounds = new List<AFValues> { new AFValues(), new AFValues() };
        static List<AFAttribute> boundAttributes;

        public static void WaitForQuit()
        {
            do
            {
                Console.Write("Enter Q to exit the program: ");
                System.GC.Collect();
            } while (Console.ReadLine() != "Q");
        }

        static void Main(string[] args)
        {
            PISystems pisystems = new PISystems();
            pisystem = pisystems[Properties.Settings.Default.AFServer];
            afdatabse = pisystem.Databases[Properties.Settings.Default.Database];

            PIServer server = new PIServers().DefaultPIServer;
            nodata = server.StateSets["SYSTEM"]["NO Data"];

            eventFrameQuery = new AFEventFrameSearch(afdatabse, "eventFrameSearch", Properties.Settings.Default.EventFrameQuery);
            currentEventFrameQuery = new AFEventFrameSearch(afdatabse, "eventFrameSearch", Properties.Settings.Default.EventFrameCurrentQuery);

            sensor = AFAttribute.FindAttribute(Properties.Settings.Default.SensorPath, null);
            boundAttributes = new List<AFAttribute>
            {
                AFAttribute.FindAttribute(Properties.Settings.Default.LowerBoundPath, null),
                AFAttribute.FindAttribute(Properties.Settings.Default.UpperBoundPath, null)
            };

            InitialRun();

            // Initialize the cookie (bookmark)
            afdatabse.FindChangedItems(false, int.MaxValue, null, out cookie);

            // Initialize the timer, used to refresh the database
            elapsedEH = new System.Timers.ElapsedEventHandler(OnElapsed);
            refreshTimer.Elapsed += elapsedEH;

            // Set the function to be triggered once a change is detected
            changedEH = new EventHandler<AFChangedEventArgs>(OnChanged);
            afdatabse.Changed += changedEH;
            refreshTimer.Start();

            WaitForQuit();

            afdatabse.Changed -= changedEH;
            refreshTimer.Elapsed -= elapsedEH;
            refreshTimer.Stop();
        }

        internal static void InitialRun()
        {
            ComputeStatistics();
            IEnumerable <AFEventFrame> currentEventFrames = currentEventFrameQuery.FindEventFrames(0, true, int.MaxValue);
            foreach (AFEventFrame currentEventFrame in currentEventFrames) {
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
            for (int i = 0; i < 2; i++) {
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

        internal static void OnChanged(object sender, AFChangedEventArgs e)
        {
            // Find changes since the last refresh
            List<AFChangeInfo> changes = new List<AFChangeInfo>();
            changes.AddRange(afdatabse.FindChangedItems(true, int.MaxValue, cookie, out cookie));

            // Refresh objects that have been changed.
            AFChangeInfo.Refresh(pisystem, changes);

            // Go over all changes and only continue further if the new object is an newly added event frame of the stated event frame
            foreach (AFChangeInfo info in changes)
            {
                if (info.Identity == AFIdentity.EventFrame)
                {
                    AFEventFrame lastestEventFrame = (AFEventFrame)info.FindObject(pisystem, true);

                    if (currentEventFrameQuery.IsMatch(lastestEventFrame)) { 
                        if (info.Action == AFChangeInfoAction.Added)
                        {
                            WriteValues(lastestEventFrame.StartTime);
                        }
                        else if (info.Action == AFChangeInfoAction.Updated || info.Action == AFChangeInfoAction.Removed)
                        {
                            ComputeStatistics();
                        }
                    }
                }
            }
        }

        internal static void OnElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Refreshing Database will cause any external changes to be seen which will
            // result in the triggering of the OnChanged event handler
            lock (afdatabse)
            {
                afdatabse.Refresh();
            }
            refreshTimer.Start();
        }

        public static List<AFValues> GetSlices(List<AFValues> trends)
        {
            int longest = trends.Any() ? trends.Max(l => l.Count) : 0;
            List<AFValues> outer = new List<AFValues>();
            for (int i = 0; i < longest; i++)
            {
                outer.Add(new AFValues());
            }
            for (int j = 0; j < trends.Count; j++)
            {
                for (int i = 0; i < trends[j].Count; i++)
                {
                    outer[i].Add(trends[j][i]);
                }
            }
            return outer;
        }
    }
}