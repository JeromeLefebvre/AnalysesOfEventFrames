using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OSIsoft.AF;
using OSIsoft.AF.EventFrame;
using OSIsoft.AF.Time;


namespace EventFrameTest
{
    class Program
    {
        static System.Timers.Timer refreshTimer = new System.Timers.Timer(10 * 1000); // every 10 seconds;
        static AFDatabase monitoredDB = null;
        static object sysCookie, dbCookie;
        static PISystems myPISystems = null;
        static PISystem myPISystem = null;
        static AFDatabases monitoredDBs = null;
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
            myPISystems = new PISystems();
            myPISystem = myPISystems[EventFrameTest.Properties.Settings.Default.AFSystemName];
            object sysCookie, dbCookie;

            monitoredDBs = myPISystem.Databases;
            monitoredDB = monitoredDBs[EventFrameTest.Properties.Settings.Default.AFDBName];

            myPISystem.FindChangedItems(false, int.MaxValue, null, out sysCookie);
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
            list.AddRange(myPISystem.FindChangedItems(true, int.MaxValue, sysCookie, out sysCookie));
            list.AddRange(monitoredDB.FindChangedItems(true, int.MaxValue, dbCookie, out dbCookie));

            // Refresh objects that have been changed.
            AFChangeInfo.Refresh(myPISystem, list);
            foreach (AFChangeInfo info in list)
            {
                AFChangeInfoAction ac = info.Action;
                AFObject myObj = info.FindObject(myPISystem, true);
                AFIdentity myID = myObj.Identity;
                if (myID == AFIdentity.EventFrame && ac == AFChangeInfoAction.Added)
                {
                    AFEventFrame myEFinfo = (AFEventFrame)info.FindObject(myPISystem, true);
                    AFNamedCollectionList<AFEventFrame> myEFList = AFEventFrame.FindEventFrames(monitoredDB, null, new AFTime("*"), 0, 5, AFEventFrameSearchMode.BackwardFromEndTime, "", "", null, myEFinfo.Template, true);
                    foreach (AFEventFrame EF in myEFList)
                    {
                        Console.WriteLine(EF.Name);
                    }
                }
            }
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

    }
}