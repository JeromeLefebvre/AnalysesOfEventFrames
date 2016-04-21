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

namespace EventFrameAnalysis
{
    class Program
    {
        static PISystem pisystem = null;
        static AFDatabase dataDB = null;
        static AFDatabase statisticDB = null;

        static Timer refreshTimer = new Timer(1000);
        static object cookie;
        static ElapsedEventHandler elapsedEH = null;
        static EventHandler<AFChangedEventArgs> changedEH = null;

        static AFElementTemplate eventFrameTemplate;
        static AFNamedCollectionList<AFEventFrame> eventFrames = null;
        static string extendedPropertiesKey = EventFrameAnalysis.Properties.Settings.Default.ExtendedPropertyKey;
        static IEnumerable<string> extendedPropertiesValues = new string[] { EventFrameAnalysis.Properties.Settings.Default.ExtendedPropertyValue };

        static AFAttribute sensor;
        static List<AFValues> slices;
        static List<IDictionary<AFSummaryTypes, AFValue>> statisticsSlices = new List<IDictionary<AFSummaryTypes, AFValue>> { };
        static IDictionary<AFSummaryTypes, AFValues> statisticsTrends = new Dictionary<AFSummaryTypes, AFValues> { };                      
        static Dictionary<AFSummaryTypes, AFAttribute> attributes;

        static AFTimeSpan interval = new AFTimeSpan(seconds: 1);

        static List<AFSummaryTypes> types = new List<AFSummaryTypes> { AFSummaryTypes.Average, AFSummaryTypes.StdDev }; // AFSummaryTypes.Maximum, AFSummaryTypes.Minimum
        static AFSummaryTypes typesCheck = AFSummaryTypes.Average | AFSummaryTypes.StdDev; // AFSummaryTypes.Maximum | AFSummaryTypes.Minimum | 

        static AFEnumerationValue nodata;

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
            pisystem = pisystems[EventFrameAnalysis.Properties.Settings.Default.AFSystemName];
            dataDB = pisystem.Databases[EventFrameAnalysis.Properties.Settings.Default.AFDataDB];
            statisticDB = pisystem.Databases[EventFrameAnalysis.Properties.Settings.Default.AFStatisticsDB];

            PIServers servers = new PIServers();
            PIServer server = servers["localhost"];
            nodata = server.StateSets["SYSTEM"]["NO Data"];

            AFElement element = statisticDB.Elements["DataStatistic"];

            attributes = new Dictionary<AFSummaryTypes, AFAttribute> {
                //{ AFSummaryTypes.Minimum, element.Attributes["Minimum"] },
                //{ AFSummaryTypes.Maximum, element.Attributes["Maximum"] },
                { AFSummaryTypes.Average, element.Attributes["Mean"] },
                { AFSummaryTypes.StdDev, element.Attributes["StandardDev"] }
            };

            eventFrameTemplate = dataDB.ElementTemplates[EventFrameAnalysis.Properties.Settings.Default.EFTemplate];

            sensor = element.Attributes["Sensor"];

            InitialRun();

            // Initialize the cookie (bookmark)
            dataDB.FindChangedItems(false, int.MaxValue, null, out cookie);

            // Initialize the timer, used to refresh the database
            elapsedEH = new System.Timers.ElapsedEventHandler(OnElapsed);
            refreshTimer.Elapsed += elapsedEH;

            // Set the function to be triggered once a change is detected
            changedEH = new EventHandler<AFChangedEventArgs>(OnChanged);
            dataDB.Changed += changedEH;

            refreshTimer.Start();

            WaitForQuit();

            // Clean up
            dataDB.Changed -= changedEH;
            refreshTimer.Elapsed -= elapsedEH;
            refreshTimer.Stop();
        }

        internal static void InitialRun()
        {
            // Populate the mean and standard distribution tags base on the current pending event frame.
            AFNamedCollectionList<AFEventFrame> recentEventFrames = AFEventFrame.FindEventFrames(database: dataDB,
                                                                                                 searchRoot: null,
                                                                                                 startTime: new AFTime("*"),
                                                                                                 startIndex: 0,
                                                                                                 maxCount: 1,
                                                                                                 searchMode: AFEventFrameSearchMode.BackwardInProgress,
                                                                                                 nameFilter: EventFrameAnalysis.Properties.Settings.Default.NameFilter,
                                                                                                 referencedElementNameFilter: "",
                                                                                                 elemCategory: null,
                                                                                                 elemTemplate: eventFrameTemplate,
                                                                                                 searchFullHierarchy: true);

            GetEventFrames();
            GetTrends();
            ComputeSatistics();
            Stitch();

            if (recentEventFrames.Count > 0) {
                AFEventFrame mostRecent = recentEventFrames[0];
                WriteValues(mostRecent.StartTime);
            }
        }

        internal static void GetEventFrames()
        {
            // Captures all strings with the correct extended property
            eventFrames = new AFNamedCollectionList<AFEventFrame>();
            string setting = EventFrameAnalysis.Properties.Settings.Default.WhichEventFramesToUse;

            if (setting == "Recent" || setting == "Both")
            {
                int count = EventFrameAnalysis.Properties.Settings.Default.NumberOfRecentEventFrames;
                AFNamedCollectionList<AFEventFrame> recentEventFrames = AFEventFrame.FindEventFrames(database: dataDB,
                                                                                                     searchRoot: null,
                                                                                                     startTime: new AFTime("*"),
                                                                                                     startIndex: 0,
                                                                                                     maxCount: count,
                                                                                                     searchMode: AFEventFrameSearchMode.BackwardFromEndTime,
                                                                                                     nameFilter: EventFrameAnalysis.Properties.Settings.Default.NameFilter,
                                                                                                     referencedElementNameFilter: "",
                                                                                                     elemCategory: null,
                                                                                                     elemTemplate: eventFrameTemplate,
                                                                                                     searchFullHierarchy: true);

                eventFrames.AddRange(recentEventFrames);
            }

            if (setting == "ExtendedProperties" || setting == "Both")
            {
                IList<KeyValuePair<string, AFEventFrame>> searchedEventFrames = AFEventFrame.FindEventFramesByExtendedProperty(database: dataDB,
                                                                                                                               propertyName: extendedPropertiesKey,
                                                                                                                               values: extendedPropertiesValues,
                                                                                                                               maxCount: Int32.MaxValue);

                foreach (KeyValuePair<string, AFEventFrame> pair in searchedEventFrames)
                {
                    AFEventFrame frame = pair.Value;
                    eventFrames.Add(frame);
                }
            }
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
            foreach (AFValues slice in slices)
            {
                statisticsSlices.Add(GetStatistics(slice));
            }
        }

        internal static void Stitch()
        {
            statisticsTrends.Clear();
            foreach (AFSummaryTypes type in types)
            {
                statisticsTrends[type] = new AFValues();
            }

            // Takes all statistics and recreates an trend
            foreach (Dictionary<AFSummaryTypes, AFValue> statisticSlice in statisticsSlices)
            {
                foreach (KeyValuePair<AFSummaryTypes, AFValue> pair in statisticSlice)
                {
                    statisticsTrends[pair.Key].Add(pair.Value);
                }
            }
        }

        internal static void WriteValues(AFTime startTime)
        {
            foreach (AFSummaryTypes type in types)
            {
                AFTime lastTime = timeShift(statisticsTrends[type], startTime);
                attributes[type].Data.UpdateValues(statisticsTrends[type], AFUpdateOption.Insert);
                // Write no data at the end of each trend
                AFValue nodataValue = new AFValue(nodata, lastTime);
            
                attributes[type].PIPoint.UpdateValue(nodataValue, AFUpdateOption.Insert);
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
            changes.AddRange(dataDB.FindChangedItems(true, int.MaxValue, cookie, out cookie));

            // Refresh objects that have been changed.
            AFChangeInfo.Refresh(pisystem, changes);

            // Go over all changes and only continue further if the new object is an newly added event frame of the stated event frame
            foreach (AFChangeInfo info in changes)
            {
                if (info.Identity == AFIdentity.EventFrame)
                {
                    if (info.Action == AFChangeInfoAction.Added)
                    {
                        AFEventFrame lastestEventFrame = (AFEventFrame)info.FindObject(pisystem, true); ;
                        if (lastestEventFrame.Template.Name == EventFrameAnalysis.Properties.Settings.Default.EFTemplate)
                        {
                            WriteValues(lastestEventFrame.StartTime);
                        }
                    }
                    else if (info.Action == AFChangeInfoAction.Updated || info.Action == AFChangeInfoAction.Removed)
                    {
                        GetEventFrames();
                        GetTrends();
                        ComputeSatistics();
                        Stitch();
                    }
                }
            }
        }

        internal static void OnElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Refreshing Database will cause any external changes to be seen which will
            // result in the triggering of the OnChanged event handler
            lock (dataDB)
            {
                dataDB.Refresh();
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