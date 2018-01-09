using System.Collections.Generic;

namespace IdleMaster
{
    public class EnhancedSteamHelper
    {
        public List<Avg> AverageValues { get; set; }
    }

    public class Avg
    {
        public int AppId { get; set; }
        
        public double AveragePrice { get; set; }
    }
}