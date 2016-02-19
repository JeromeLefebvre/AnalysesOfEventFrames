using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OSIsoft.AF;




namespace EventFrameTest
{
    class Program
    {
        static System.Timers.Timer refreshTimer = new System.Timers.Timer(10 * 1000); // every 10 seconds;
        static AFDatabase monitoredDB = null;
        static object sysCookie, dbCookie;
        static PISystem PISystem = null;
        static System.Timers.ElapsedEventHandler elapsedEH = null;
        static EventHandler<AFChangedEventArgs> changedEH = null;


        static void Main(string[] args)
        {
            PISystems myPISystems = new PISystems();
            PISystem myPISystem = myPISystems[EventFrameTest.Properties.Settings.Default.AFSystemName];
            object sysCookie, dbCookie;

            AFDatabases myDBs = myPISystem.Databases;
            AFDatabase myDB = myDBs[EventFrameTest.Properties.Settings.Default.AFDBName];

            myPISystem.FindChangedItems(false, int.MaxValue, null, out sysCookie);
            myDB.FindChangedItems(false, int.MaxValue, null, out dbCookie);

            // Find changes made while application not running.
            List<AFChangeInfo> list = new List<AFChangeInfo>();
            list.AddRange(myPISystem.FindChangedItems(false, int.MaxValue, sysCookie, out sysCookie));
            list.AddRange(myDB.FindChangedItems(false, int.MaxValue, dbCookie, out dbCookie));

            // Refresh objects that have been changed.
            AFChangeInfo.Refresh(myPISystem, list);
            foreach (AFChangeInfo info in list)
            {
                AFChangeInfoAction ac = info.Action;
                AFObject myObj = info.FindObject(myPISystem, true);
                AFIdentity myID = myObj.Identity;
                if (myID == AFIdentity.EventFrame && ac==AFChangeInfoAction.Added)
                {

                    Console.WriteLine("Found changed object: {0}", myObj);
                }
            }
            Console.ReadLine();
        }
    }
}
