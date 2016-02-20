using System;
using System.Collections.Generic;
using System.Linq;
using OSIsoft.AF;
using OSIsoft.AF.EventFrame;
using OSIsoft.AF.Time;
using OSIsoft.AF.Asset;
using OSIsoft.AF.PI;
using OSIsoft.AF.Data;

/*
When we grab the event frames, do we grab the current one?
*/
namespace EventFrameTest
{
    class Program
    {
        static System.Timers.Timer refreshTimer = new System.Timers.Timer(1000);
        static AFDatabase monitoredDB = null;
        static object sysCookie, dbCookie;
        static PISystems pisystems = null;
        static PISystem pisystem = null;
        static System.Timers.ElapsedEventHandler elapsedEH = null;
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

            // Initialize the cookies (bookmarks)
            object sysCookie, dbCookie;
            //pisystem.FindChangedItems(false, int.MaxValue, null, out sysCookie);
            monitoredDB.FindChangedItems(false, int.MaxValue, null, out dbCookie);

            //Timer
            elapsedEH = new System.Timers.ElapsedEventHandler(OnElapsed);
            refreshTimer.Elapsed += elapsedEH;
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
            //Console.WriteLine(sender);
            //Console.WriteLine(e);
            // Find changes made while application not running.
            List<AFChangeInfo> list = new List<AFChangeInfo>();
            //list.AddRange(pisystem.FindChangedItems(true, int.MaxValue, sysCookie, out sysCookie));
            list.AddRange(monitoredDB.FindChangedItems(true, int.MaxValue, dbCookie, out dbCookie));

            PIServer myPIServer = new PIServers().DefaultPIServer;
            
            // Display information for Default PIServer

            // Refresh objects that have been changed.
            AFChangeInfo.Refresh(pisystem, list);
            foreach (AFChangeInfo info in list)
            {
                AFChangeInfoAction ac = info.Action;
                AFObject myObj = info.FindObject(pisystem, true);
                AFIdentity myID = myObj.Identity;
                //Console.WriteLine(myID);
                if (myID == AFIdentity.EventFrame && ac == AFChangeInfoAction.Added)
                {
                    AFEventFrame myEFinfo = (AFEventFrame)info.FindObject(pisystem, true);
                    AFNamedCollectionList<AFEventFrame> myEFList = AFEventFrame.FindEventFrames(monitoredDB, null, new AFTime("*"), 0, 20, AFEventFrameSearchMode.BackwardFromEndTime, "", "", null, myEFinfo.Template, true);
                    List<AFValues> allTrends = new List<AFValues>();

                    Console.WriteLine();

                    // Find the out the delay into getting started.
                    Console.WriteLine(myEFinfo.StartTime);
                    Console.WriteLine(DateTime.Now.TimeOfDay);


                    AFElement element = monitoredDB.Elements["DataGeneration"];
                    AFAttribute meanattr = element.Attributes["Mean"];
                    AFAttribute stdattr = element.Attributes["StandardDev"];
                    AFAttribute sensor = element.Attributes["Sensor"];

                    foreach (AFEventFrame EF in myEFList)
                    {
                        AFTimeRange range = new AFTimeRange(EF.StartTime, EF.EndTime);
                        //AFAttribute attr = EF.Attributes["Sensor"];
                        //PIPoint point = attr.PIPoint;
                        //AFValues values = sensor.RecordedValues(range, AFBoundaryType.Inside, null, true, 100);
                        AFValues values = sensor.Data.InterpolatedValues(range, new AFTimeSpan(seconds:1), null, null, true);
                        allTrends.Add(values);
                        //Console.WriteLine(EF.Name);
                    }

                    //myEFinfo.ReferencedElements;

                    List<AFValues> allValues = Transpose(allTrends);
                    AFTime first = myEFinfo.StartTime;

                    foreach (AFValues row in allValues)
                    {
                        AFValue mean = Mean(row);
                        mean.Timestamp = new AFTime(mean.Timestamp.UtcSeconds + 60);
                        meanattr.Data.UpdateValue(mean, AFUpdateOption.Insert);
                        AFValue std = StandardDeviation(row);
                        std.Timestamp = new AFTime(std.Timestamp.UtcSeconds + 60);
                        stdattr.Data.UpdateValue(std, AFUpdateOption.Insert);
                    }
                }
            }
        }

        public static AFValue StandardDeviation(AFValues values)
        {
            // Assumes all values have the same timestamp
            AFTime time = values[0].Timestamp;
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

        public static AFValue Mean(AFValues values)
        {
            // Expects all values to have the same timestamp
            AFTime time = values[0].Timestamp;
            double total = 0;
            foreach (AFValue value in values)
            {
                total += (double)value.Value;
            }
            int count = values.Count;
            return new AFValue(total / count, time);
        }

        internal static void print<T>(T str)
        {
            Console.WriteLine(str);
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