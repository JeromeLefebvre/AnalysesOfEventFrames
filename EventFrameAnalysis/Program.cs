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
using OSIsoft.AF;
using OSIsoft.AF.EventFrame;
using OSIsoft.AF.Asset;
using System.IO;
using Newtonsoft.Json;
using LimitCalculatorSDK;

namespace EventFrameAnalysis
{
    class Program
    {
        static Timer refreshTimer = new Timer(1000);
        static object cookie;
        static ElapsedEventHandler elapsedEH;
        static EventHandler<AFChangedEventArgs> changedEH;

        static PISystem pisystem ;
        static AFDatabase afdatabse;
        static AFAttribute sensor;
        static List<LimitCalculation> calculations;

        public static void WaitForQuit()
        {
            do
            {
                Console.Write("Enter Q to exit the program: ");
            } while (Console.ReadLine() != "Q");
        }

        static void Main(string[] args)
        {
            sensor = AFAttribute.FindAttribute(Properties.Settings.Default.SensorPath, null);
            //pisystem = sensor.PISystem;
            afdatabse = sensor.Database;
            List<CalculationPreference> calculationPreferences;

            string path = @"C:\Users\jlefebvre\Desktop\setting.json";

            string preferenceText = File.ReadAllText(path);
            calculationPreferences = JsonConvert.DeserializeObject<List<CalculationPreference>>(preferenceText);

            calculations = new List<LimitCalculation> { };

            foreach (CalculationPreference pref in calculationPreferences)
            {
                pref.nameToTrait();
                calculations.Add(new LimitCalculation(pref));
            } 

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

        internal static void OnChanged(object sender, AFChangedEventArgs e)
        {
            // Find changes since the last refresh
            List<AFChangeInfo> changes = new List<AFChangeInfo>();
            changes.AddRange(afdatabse.FindChangedItems(true, int.MaxValue, cookie, out cookie));
            AFChangeInfo.Refresh(afdatabse.PISystem, changes);

            foreach (AFChangeInfo info in changes.FindAll(i => i.Identity == AFIdentity.EventFrame))
            {
                AFEventFrame lastestEventFrame = (AFEventFrame)info.FindObject(afdatabse.PISystem, true);
                
                foreach (LimitCalculation calculation in calculations)
                {
                    calculation.performAction(lastestEventFrame, info.Action);
                }
            }
        }

        internal static void OnElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Refreshing Database will cause any external changes to be seen which will result in the triggering of the OnChanged event handler
            lock (afdatabse)
            {
                afdatabse.Refresh();
            }
            refreshTimer.Start();
        }
    }
}