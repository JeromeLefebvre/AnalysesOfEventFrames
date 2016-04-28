namespace EventFrameAnalysis
{
    class CalculationPreference
    {
        public string sensorPath { get; set; }
        public string eventFrameQuery { get; set; }
        public CalculationPreference(string path, string query)
        {
            sensorPath = path;
            eventFrameQuery = query;
        }
    }
}
