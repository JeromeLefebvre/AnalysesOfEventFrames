﻿#region Copyright
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

namespace EventFrameTest
{
    class Program
    {
        static Timer refreshTimer = new Timer(1000);
        static AFDatabase dataDB = null;
        static AFDatabase statisticDB = null;
        static object cookie;
        static PISystems pisystems = null;
        static PISystem pisystem = null;
        static ElapsedEventHandler elapsedEH = null;
        static EventHandler<AFChangedEventArgs> changedEH = null;

        static AFNamedCollectionList<AFEventFrame> eventFrames = null;

        static string extendedPropertiesKey = EventFrameTest.Properties.Settings.Default.ExtendedPropertyKey;
        static IEnumerable<string> extendedPropertiesValues = new string[] { EventFrameTest.Properties.Settings.Default.ExtendedPropertyValue };

        static AFAttribute sensor;
        static List<AFValues> allValues;

        static List<double> means = new List<double> { };
        static List<double> standardDeviations = new List<double> { };

        static AFAttribute meanattr;
        static AFAttribute stdattr;

        static AFElementTemplate eventFrameTemplate;

        public static void WaitForQuit()
        {
            do
            {
                Console.Write("Enter Q to exit the program: ");
            } while (Console.ReadLine() != "Q");
        }

        static void Main(string[] args)
        {
            pisystems = new PISystems();
            pisystem = pisystems[EventFrameTest.Properties.Settings.Default.AFSystemName];
            dataDB = pisystem.Databases[EventFrameTest.Properties.Settings.Default.AFDataDB];
            statisticDB = pisystem.Databases[EventFrameTest.Properties.Settings.Default.AFStatisticsDB];

            
            AFElement element = statisticDB.Elements["DataStatistic"];
            meanattr = element.Attributes["Mean"];
            stdattr = element.Attributes["StandardDev"];

            eventFrameTemplate = dataDB.ElementTemplates[EventFrameTest.Properties.Settings.Default.EFTemplate];

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
                                                                                                 nameFilter: "",
                                                                                                 referencedElementNameFilter: "",
                                                                                                 elemCategory: null,
                                                                                                 elemTemplate: eventFrameTemplate,
                                                                                                 searchFullHierarchy: true);
            AFEventFrame mostRecent = recentEventFrames[0];

            sensor = mostRecent.Attributes[EventFrameTest.Properties.Settings.Default.EFProperty];
            CaptureEventFrames();
            GetAllTrends();
            ComputeSatistics();
            WriteValues(mostRecent.StartTime);
        }

        internal static void CaptureEventFrames()
        {
            // Captures all strings with the correct extended property
            eventFrames = new AFNamedCollectionList<AFEventFrame>();
            string setting = EventFrameTest.Properties.Settings.Default.WhichEventFramesToUse;

            if (setting == "Recent" || setting == "Both")
            {
                int count = EventFrameTest.Properties.Settings.Default.NumberOfRecentEventFrames;
                AFNamedCollectionList<AFEventFrame> recentEventFrames = AFEventFrame.FindEventFrames(database: dataDB,
                                                                                                     searchRoot: null,
                                                                                                     startTime: new AFTime("*"),
                                                                                                     startIndex: 0,
                                                                                                     maxCount: count,
                                                                                                     searchMode: AFEventFrameSearchMode.BackwardFromEndTime,
                                                                                                     nameFilter: "",
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

        internal static void GetAllTrends()
        {
            // Gets all the data for the sensor attribute for the various event frames
            List<AFValues> allTrends = new List<AFValues>();

            foreach (AFEventFrame EF in eventFrames)
            {
                AFTimeRange range = EF.TimeRange;
                // need to change the following...
                AFValues values = sensor.Data.InterpolatedValues(range, new AFTimeSpan(seconds: 1), null, "not BadVal('sinusoid')", false);
                allTrends.Add(values);
            }
            allValues = Transpose(allTrends);
        }

        internal static void ComputeSatistics()
        {
            means = new List<double> { };
            standardDeviations = new List<double> { };
            foreach (AFValues row in allValues)
            {
                means.Add(Mean(row));
                standardDeviations.Add(StandardDeviation(row));
            }
        }

        internal static void WriteValues(AFTime startTime)
        {
            AFValues projectedMean = new AFValues();
            AFValues projectedStandardDeviation = new AFValues();
            
            for (int i = 0; i < means.Count; i++)
            {
                AFTime time = new AFTime(startTime.UtcSeconds + i);
                AFValue mean = new AFValue(means[i], time);
                AFValue standardDeviation = new AFValue(standardDeviations[i], time);
                projectedMean.Add(mean);
                projectedStandardDeviation.Add(standardDeviation);
            }
            meanattr.Data.UpdateValues(projectedMean, AFUpdateOption.Replace);
            stdattr.Data.UpdateValues(projectedStandardDeviation, AFUpdateOption.Replace);            
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
                        if (lastestEventFrame.Template.Name == EventFrameTest.Properties.Settings.Default.EFTemplate)
                        {
                            WriteValues(lastestEventFrame.StartTime);
                        }
                    }
                    else if (info.Action == AFChangeInfoAction.Updated || info.Action == AFChangeInfoAction.Removed)
                    {
                        CaptureEventFrames();
                        CaptureEventFrames();
                        ComputeSatistics();
                    }
                }
            }
        }

        public static double StandardDeviation(AFValues values)
        {
            // Returns the standard deviation of a collection of AFValues
            // As the timestamp for each value may vary, a time stamp must also be provided to return an AFValue
            double M = 0.0;
            double S = 0.0;
            int k = 1;
            foreach (AFValue value in values)
            {
                double rawValue = value.ValueAsDouble();
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

        public static List<AFValues> Transpose(List<AFValues> trends)
        {
            // Does a matrix like transpose on a list of trends
            var longest = trends.Any() ? trends.Max(l => l.Count) : 0;

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