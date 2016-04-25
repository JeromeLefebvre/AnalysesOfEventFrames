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
        static PISystem pisystem = null;
        static AFDatabase afdatabse = null;

        static Timer refreshTimer = new Timer(1000);
        static object cookie;
        static ElapsedEventHandler elapsedEH = null;
        static EventHandler<AFChangedEventArgs> changedEH = null;

        static AFNamedCollectionList<AFEventFrame> eventFrames = null;
        static string extendedPropertiesKey = EventFrameAnalysis.Properties.Settings.Default.RecalculationInterval;
        static IEnumerable<string> extendedPropertiesValues = new string[] { EventFrameAnalysis.Properties.Settings.Default.EventFrameQuery };

        static AFAttribute sensor;
        static List<AFValues> slices;
        static List<IDictionary<AFSummaryTypes, AFValue>> statisticsSlices = new List<IDictionary<AFSummaryTypes, AFValue>> { };
        static IDictionary<AFSummaryTypes, AFValues> statisticsTrends = new Dictionary<AFSummaryTypes, AFValues> { };                      

        static AFTimeSpan interval = new AFTimeSpan(seconds: 1);

        static List<AFSummaryTypes> types = new List<AFSummaryTypes> { AFSummaryTypes.Average, AFSummaryTypes.StdDev, AFSummaryTypes.Maximum, AFSummaryTypes.Minimum };
        static AFSummaryTypes typesCheck = AFSummaryTypes.Average | AFSummaryTypes.StdDev | AFSummaryTypes.Maximum | AFSummaryTypes.Minimum;

        static List<AFValues> bounds = new List<AFValues> { };
        static List<AFAttribute> boundAttributes;

        static AFEnumerationValue nodata;

        static AFEventFrameSearch eventFrameQuery;

        public static void WaitForQuit()
        {
            do
            {
                Console.Write("Enter Q to exit the program: ");
            } while (Console.ReadLine() != "Q");
        }

        static void Main(string[] args)
        {
            PISystems pisystems = new PISystems();
            pisystem = pisystems[EventFrameAnalysis.Properties.Settings.Default.AFServer];
            afdatabse = pisystem.Databases[EventFrameAnalysis.Properties.Settings.Default.Database];

            PIServer server = new PIServers().DefaultPIServer;
            nodata = server.StateSets["SYSTEM"]["NO Data"];

            sensor = AFAttribute.FindAttribute(EventFrameAnalysis.Properties.Settings.Default.SensorPath, afdatabse);

            eventFrameQuery = new AFEventFrameSearch(afdatabse, "eventFrameSearch", EventFrameAnalysis.Properties.Settings.Default.EventFrameQuery);

            boundAttributes = new List<AFAttribute>
            {
                AFAttribute.FindAttribute(EventFrameAnalysis.Properties.Settings.Default.LowerBoundPath, afdatabse),
                AFAttribute.FindAttribute(EventFrameAnalysis.Properties.Settings.Default.UpperBoundPath, afdatabse)
            };
            bounds.Add(new AFValues());
            bounds.Add(new AFValues());

            //InitialRun();

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

            // Clean up
            afdatabse.Changed -= changedEH;
            refreshTimer.Elapsed -= elapsedEH;
            refreshTimer.Stop();
        }

        internal static void InitialRun()
        {
            // Populate the mean and standard distribution tags base on the current pending event frame.

          
            GetEventFrames();
            GetTrends();
            ComputeSatistics();

            /*
            IEnumerable<AFEventFrame> recentEventFrames = GetCurrentEventFrame();

            AFEventFrame newEF = new AFEventFrame(afdatabse);

            if (recentEventFrames. > 0) {
                AFEventFrame mostRecent = recentEventFrames[0];
                WriteValues(mostRecent.StartTime);
            }
            */
        }


        internal static AFEventFrames GetEventFrames()
        {
            IEnumerable<AFEventFrame> eventFrames = eventFrameQuery.FindEventFrames(0, true, int.MaxValue);
            eventFrames.OrderBy(frame => frame.StartTime); // Needed?
        }

        internal static AFEventFrames GetCurrentEventFrame()
        {
            return currentEventFrameQuery.FindEventFrames();
        }


        internal static void GetTrends()
        {
            // Gets all the data for the sensor attribute for the various event frames
            List<AFValues> trends = new List<AFValues>();
            foreach (AFEventFrame EF in eventFrames)
            {
                trends.Add(sensor.Data.InterpolatedValues(EF.TimeRange, interval, null, "", true));
            }
            slices = GetSlices(trends);
        }

        internal static void ComputeSatistics()
        {
            statisticsSlices.Clear();
            bounds[0].Clear();
            bounds[1].Clear();
            foreach (AFValues slice in slices)
            {
                IDictionary<AFSummaryTypes, AFValue> statisticForSlice = GetStatistics(slice);
                AFTime time = statisticForSlice[AFSummaryTypes.Average].Timestamp;
                Double mean = statisticForSlice[AFSummaryTypes.Average].ValueAsDouble();
                Double stddev = statisticForSlice[AFSummaryTypes.StdDev].ValueAsDouble();
                bounds[0].Add(new AFValue(mean + 3 * stddev, time));
                bounds[1].Add(new AFValue(mean - 3 * stddev, time));
            }
        }


        internal static void WriteValues(AFTime startTime)
        {
            AFTime lastTime;
            AFValue nodataValue;

            for (int i = 0; i < 2; i++) { 
                lastTime = timeShift(bounds[i], startTime);
                boundAttributes[i].Data.UpdateValues(bounds[i], AFUpdateOption.Insert);
                nodataValue = new AFValue(nodata, lastTime);
                boundAttributes[i].PIPoint.UpdateValue(nodataValue, AFUpdateOption.Insert);
            }
        }

        public static IDictionary<AFSummaryTypes, AFValue> GetStatistics(AFValues values)
        {
            AFTimeRange range = new AFTimeRange(values[0].Timestamp, values[values.Count - 1].Timestamp);
            return values.Summary(range, typesCheck, AFCalculationBasis.EventWeighted, AFTimestampCalculation.MostRecentTime);
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

                    if (info.Action == AFChangeInfoAction.Added)
                    {
                        if (lastestEventFrame.Template.Name == EventFrameAnalysis.Properties.Settings.Default.LowerBoundPath && lastestEventFrame.Name.StartsWith(EventFrameAnalysis.Properties.Settings.Default.EventFrameName))
                        {
                            WriteValues(lastestEventFrame.StartTime);
                        }
                    }
                    else if (info.Action == AFChangeInfoAction.Updated || info.Action == AFChangeInfoAction.Removed)
                    {
                        GetEventFrames();
                        GetTrends();
                        ComputeSatistics();
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