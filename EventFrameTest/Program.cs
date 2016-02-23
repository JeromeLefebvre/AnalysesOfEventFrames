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
using OSIsoft.AF.PI;
using OSIsoft.AF.Data;

namespace EventFrameTest
{
    class Program
    {
        static Timer refreshTimer = new Timer(1000);
        static AFDatabase monitoredDB = null;
        static object cookie;
        static PISystems pisystems = null;
        static PISystem pisystem = null;
        static ElapsedEventHandler elapsedEH = null;
        static EventHandler<AFChangedEventArgs> changedEH = null;

        public static void WaitForQuit()
        {
            do {
                Console.Write("Enter Q to exit the program: ");
            } while (Console.ReadLine() != "Q");
        }

        static void Main(string[] args)
        {
            pisystems = new PISystems();
            pisystem = pisystems[EventFrameTest.Properties.Settings.Default.AFSystemName];
            monitoredDB = pisystem.Databases[EventFrameTest.Properties.Settings.Default.AFDBName];

            // Initialize the cookie (bookmark)
            monitoredDB.FindChangedItems(false, int.MaxValue, null, out cookie);

            // Initialize the timer, used to refresh the database
            elapsedEH = new System.Timers.ElapsedEventHandler(OnElapsed);
            refreshTimer.Elapsed += elapsedEH;

            // Set the function to be triggered once a change is detected
            changedEH = new EventHandler<AFChangedEventArgs>(OnChanged);
            monitoredDB.Changed += changedEH;

            refreshTimer.Start();
            
            WaitForQuit();
            
            // Clean up
            monitoredDB.Changed -= changedEH;
            refreshTimer.Elapsed -= elapsedEH;
            refreshTimer.Stop();
        }

        internal static void OnChanged(object sender, AFChangedEventArgs e)
        {
            // Find changes since the last refresh
            List<AFChangeInfo> changes = new List<AFChangeInfo>();
            changes.AddRange(monitoredDB.FindChangedItems(true, int.MaxValue, cookie, out cookie));

            // Refresh objects that have been changed.
            AFChangeInfo.Refresh(pisystem, changes);

            // Go over all changes and only continue further if the new object is an newly added event frame of the stated event frame
            foreach (AFChangeInfo info in changes)
            {
                AFObject myObj = info.FindObject(pisystem, true);
                
                if (myObj.Identity == AFIdentity.EventFrame && info.Action == AFChangeInfoAction.Added)
                {
                    AFEventFrame lastestEventFrame = (AFEventFrame)myObj;
                    if (lastestEventFrame.Template.Name == EventFrameTest.Properties.Settings.Default.EFTemplate) {

                        AFNamedCollectionList<AFEventFrame> recentEventFrames = AFEventFrame.FindEventFrames(database: monitoredDB,
                                                                                searchRoot: null,
                                                                                startTime: new AFTime("*"),
                                                                                startIndex: 0,
                                                                                maxCount: 100,
                                                                                searchMode: AFEventFrameSearchMode.BackwardFromEndTime,
                                                                                nameFilter: "",
                                                                                referencedElementNameFilter: "",
                                                                                elemCategory: null,
                                                                                elemTemplate: lastestEventFrame.Template,
                                                                                searchFullHierarchy: true);



                        List<AFValues> allTrends = new List<AFValues>();

                        // Get the various, sensor, mean and tag, attributes directly from the element
                        AFElement element = monitoredDB.Elements["DataGeneration"];
                        AFAttribute meanattr = element.Attributes["Mean"];
                        AFAttribute stdattr = element.Attributes["StandardDev"];
                        AFAttribute sensor = element.Attributes["Sensor"];

                        // Get the data from the sensor tag for each event frames
                        foreach (AFEventFrame EF in recentEventFrames)
                        {
                            AFTimeRange range = EF.TimeRange;
                            AFValues values = sensor.Data.InterpolatedValues(range, new AFTimeSpan(seconds: 1), null, null, true);
                            allTrends.Add(values);
                        }

                        List<AFValues> allValues = Transpose(allTrends);
                        AFTime first = lastestEventFrame.StartTime;

                        // For each seconds, compute the mean and standard deviation and write them to the data archive
                        int i = 0;
                        foreach (AFValues row in allValues)
                        {
                            AFValue mean = Mean(row, new AFTime(lastestEventFrame.StartTime.UtcSeconds + i));
                            meanattr.Data.UpdateValue(mean, AFUpdateOption.Insert);
                            AFValue std = StandardDeviation(row, new AFTime(lastestEventFrame.StartTime.UtcSeconds + i));
                            stdattr.Data.UpdateValue(std, AFUpdateOption.Insert);
                            i++;
                        }
                    }                                                                       
                }
            }
        }

        public static AFValue StandardDeviation(AFValues values, AFTime time)
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
            return new AFValue(S == 0 ? 0 : Math.Sqrt(S / (k - 2)), time);
        }

        public static AFValue Mean(AFValues values, AFTime time)
        {
            // Returns the mean of a collection of AFValues
            // As the timestamp for each value may vary, a timestamp must also be provided to return an AFValue
            double total = 0;
            foreach (AFValue value in values)
            {
                // The data stored in the attribute is of type Float32, thus possible to convert to double 
                total += (double)value.Value;
            }
            int count = values.Count;
            return new AFValue(total / count, time);
        }

        internal static void OnElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Refreshing Database will cause any external changes to be seen which will
            // result in the triggering of the OnChanged event handler
            lock (monitoredDB)
            {
                monitoredDB.Refresh();
            }
            refreshTimer.Start();
        }

        public static List<AFValues> Transpose(List<AFValues> trends)
        {
            // Does a matrix like transpose on a list of AFValues
            var longest = trends.Any() ? trends.Max(l => l.Count) : 0;

            List<AFValues> outer = new List<AFValues>();
            for (int i = 0; i < longest; i ++)
            {
                outer.Add(new AFValues());
            }
            for (int j = 0; j < trends.Count; j++)
            {
                int i = 0;
                foreach (AFValue value in trends[j])
                {
                    outer[i].Add(value);
                    i++;
                }
            }
            return outer;
        }
    }
}