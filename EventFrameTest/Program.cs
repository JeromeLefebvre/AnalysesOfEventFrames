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

            foreach (AFChangeInfo info in changes)
            {
                AFObject myObj = info.FindObject(pisystem, true);

                if (myObj.Identity == AFIdentity.EventFrame && info.Action == AFChangeInfoAction.Added)
                {
                    AFEventFrame lastestEventFrame = (AFEventFrame)myObj;
                    AFNamedCollectionList<AFEventFrame> recentEventFrames = AFEventFrame.FindEventFrames(monitoredDB,
                                                                                                null, 
                                                                                                new AFTime("*"), 
                                                                                                0, 
                                                                                                25, 
                                                                                                AFEventFrameSearchMode.BackwardFromEndTime, 
                                                                                                "", 
                                                                                                "", 
                                                                                                null, 
                                                                                                lastestEventFrame.Template, 
                                                                                                true);
                    List<AFValues> allTrends = new List<AFValues>();

                    AFElement element = monitoredDB.Elements["DataGeneration"];
                    AFAttribute meanattr = element.Attributes["Mean"];
                    AFAttribute stdattr = element.Attributes["StandardDev"];
                    AFAttribute sensor = element.Attributes["Sensor"];

                    foreach (AFEventFrame EF in recentEventFrames)
                    {
                        AFTimeRange range = EF.TimeRange;
                        AFValues values = sensor.Data.InterpolatedValues(range, new AFTimeSpan(seconds:1), null, null, true);
                        allTrends.Add(values);
                    }

                    List<AFValues> allValues = Transpose(allTrends);
                    AFTime first = lastestEventFrame.StartTime;

                    int i = 0;
                    foreach (AFValues row in allValues)
                    {
                        AFValue mean = Mean(row, new AFTime (lastestEventFrame.StartTime.UtcSeconds + i));   
                        meanattr.Data.UpdateValue(mean, AFUpdateOption.Insert);
                        AFValue std = StandardDeviation(row, new AFTime(lastestEventFrame.StartTime.UtcSeconds + i));
                        stdattr.Data.UpdateValue(std, AFUpdateOption.Insert);
                        i++;
                    }
                }
            }
        }

        public static AFValue StandardDeviation(AFValues values, AFTime time)
        {
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
            double total = 0;
            foreach (AFValue value in values)
            {
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
            var longest = trends.Any() ? trends.Max(l => l.Count) : 0;

            List<AFValues> outer = new List<AFValues>();
            for (int i = 0; i < 60; i ++)
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

        public static List<List<T>> Transpose_gen<T>(List<List<T>> lists)
        {
            var longest = lists.Any() ? lists.Max(l => l.Count) : 0;
            List<List<T>> outer = new List<List<T>>(longest);
            for (int i = 0; i < longest; i++)
                outer.Add(new List<T>(lists.Count));
            for (int j = 0; j < lists.Count; j++)
                for (int i = 0; i < lists[j].Count; i++)
                    outer[i].Add(lists[j][i]);
            return outer;
        }
    }
}