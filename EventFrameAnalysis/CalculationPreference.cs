using OSIsoft.AF.Asset;
using System.Collections.Generic;
using System.Linq;

namespace EventFrameAnalysis
{
    class CalculationPreference_old
    {
        public string sensorPath { get; set; }
        public string eventFrameQuery { get; set; }
        public Dictionary<AFAttributeTrait, string> calculationsToPerform = new Dictionary<AFAttributeTrait, string> { };
        public Dictionary<string, string> calculationsToPerformRaw = new Dictionary<string, string> { };
        public Dictionary<string, AFAttributeTrait> reverse = AFAttributeTrait.AllLimits.ToDictionary(p => p.Name, p => p);

        public CalculationPreference_old(string path, string query)
        {
            // Used in the following way:
            //CalculationPreference calculation = JsonConvert.DeserializeObject<CalculationPreference>(json);
            //calculation.fillDictionary();
            sensorPath = path;
            eventFrameQuery = query;
        }

        public void nameToTrait()
        {
            calculationsToPerform = calculationsToPerformRaw.ToDictionary(p => reverse[p.Key], p => p.Value);
        }

        public void traitToName()
        {
            calculationsToPerformRaw = calculationsToPerform.ToDictionary(p => p.Key.Name, p => p.Value);
        }
    }
}
