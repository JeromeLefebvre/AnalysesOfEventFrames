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

namespace EventFrameAnalysis
{
    class Program
    {
        static Timer refreshTimer = new Timer(1000);
        static AFDatabase dataDB = null;
        static AFDatabase statisticDB = null;
        static object cookie;
        static PISystem pisystem = null;
        static ElapsedEventHandler elapsedEH = null;
        static EventHandler<AFChangedEventArgs> changedEH = null;

        static AFNamedCollectionList<AFEventFrame> eventFrames = null;

        static string extendedPropertiesKey = EventFrameAnalysis.Properties.Settings.Default.ExtendedPropertyKey;
        static IEnumerable<string> extendedPropertiesValues = new string[] { EventFrameAnalysis.Properties.Settings.Default.ExtendedPropertyValue };

        static AFAttribute sensor;
        static List<AFValues> allValues;

        static List<double> means = new List<double> { };
        static List<double> standardDeviations = new List<double> { };
        static Dictionary<AFSummaryTypes, AFValues> statisticsVal = new Dictionary<AFSummaryTypes, AFValues> { };

        static AFAttribute meanattr;
        static AFAttribute stdattr;
        static Dictionary<AFSummaryTypes, AFAttribute> attributes;

        static AFElementTemplate eventFrameTemplate;

        static AFTimeSpan interval = new AFTimeSpan(seconds: 1);

        List<AFSummaryTypes> types = new List<AFSummaryTypes> { AFSummaryTypes.Maximum, AFSummaryTypes.Minimum, AFSummaryTypes.Average, AFSummaryTypes.StdDev };

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


            AFElement element = statisticDB.Elements["DataStatistic"];

            attributes = new Dictionary<AFSummaryTypes, AFAttribute> {
                { AFSummaryTypes.Average, element.Attributes["Mean"] },
                { AFSummaryTypes.StdDev, element.Attributes["StandardDev"] },
                { AFSummaryTypes.Minimum, element.Attributes["Minimum"] },
                { AFSummaryTypes.Maximum, element.Attributes["Maximum"] }
            };
             
            //meanattr = element.Attributes["Mean"];
            stdattr = element.Attributes["StandardDev"];
            
            /*
            // Force a load of the PIPoint references underlying the attributes
            AFAttributeList attrlist = new AFAttributeList(new[] { meanattr, stdattr });
            attrlist.GetPIPoint();

            if (!meanattr.PIPoint.IsResolved || !stdattr.PIPoint.IsResolved)
            {
                Console.WriteLine("The tags might not have been created, please make sure that they are before continuing.");
                Console.ReadLine();
                return;
            }   */ 

            eventFrameTemplate = dataDB.ElementTemplates[EventFrameAnalysis.Properties.Settings.Default.EFTemplate];

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

            sensor = mostRecent.Attributes[EventFrameAnalysis.Properties.Settings.Default.EFProperty];
            CaptureEventFrames();
            GetAllTrends();
            ComputeSatistics();
            WriteValues(mostRecent.StartTime);
        }

        internal static void CaptureEventFrames()
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
                AFValues values = sensor.Data.InterpolatedValues(EF.TimeRange
                    , new AFTimeSpan(seconds: 1), null, "", true);
                allTrends.Add(values);
            }
            allValues = Transpose(allTrends);
        }

        internal static void ComputeSatistics()
        {
            means.Clear();
            standardDeviations.Clear();
            foreach (AFValues row in allValues)
            {
                IDictionary<AFSummaryTypes, AFValue> meanAndStddev = statistics(row);
                means.Add((double)meanAndStddev[AFSummaryTypes.Average].Value);
                standardDeviations.Add((double)meanAndStddev[AFSummaryTypes.StdDev].Value);
            }
        }
        internal static void ComputeSatistics2()
        {
            statisticsVal.Clear();
            foreach (AFValues row in allValues)
            {
                IDictionary<AFSummaryTypes, AFValue> meanAndStddev = statistics(row);
                means.Add((double)meanAndStddev[AFSummaryTypes.Average].Value);
                standardDeviations.Add((double)meanAndStddev[AFSummaryTypes.StdDev].Value);
            }
        }

        internal static void WriteValues(AFTime startTime, double interval = 1)
        {
            AFValues projectedMean = new AFValues();
            AFValues projectedStandardDeviation = new AFValues();

            for (int i = 0; i < means.Count; i++)
            {
                AFTime time = new AFTime(startTime.UtcSeconds + i* interval);
                
                AFValue mean = new AFValue(means[i], time);
                AFValue standardDeviation = new AFValue(standardDeviations[i], time);
                projectedMean.Add(mean);
                projectedStandardDeviation.Add(standardDeviation);
            }
            meanattr.Data.UpdateValues(projectedMean, AFUpdateOption.Replace);
            stdattr.Data.UpdateValues(projectedStandardDeviation, AFUpdateOption.Replace);
        }

        internal static void WriteValues2(AFTime startTime)
        {
            foreach (AFSummaryTypes type in types)
            {
                timeShift(statisticsVal[type], startTime);
                attributes[type].Data.UpdateValues(statisticsVal[type], AFUpdateOption.Insert);
            }
        }

        internal static void timeShift(AFValues values, AFTime startTime)
        {
            for (int i = 0; i < values.Count; i++)
            {
                values[i].Timestamp = startTime;
                startTime += interval;
            }
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
                        CaptureEventFrames();
                        GetAllTrends();
                        ComputeSatistics();
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

        public static IDictionary<AFSummaryTypes, AFValue> statistics(AFValues values)
        {
            AFTimeRange range = new AFTimeRange(values.Min(value => value.Timestamp), values.Max(value => value.Timestamp));
            IDictionary<AFSummaryTypes, AFValue> summary = values.Summary(range,
                                                                          AFSummaryTypes.Average | AFSummaryTypes.StdDev | AFSummaryTypes.Maximum | AFSummaryTypes.Minimum,
                                                                          AFCalculationBasis.EventWeighted,
                                                                          AFTimestampCalculation.MostRecentTime);

            return summary;
            
        }
    }
}