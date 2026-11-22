using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogisticsEngine
{
    namespace AirportLogistics.Engine
    {
        public class GroundCrew : Fact
        {
            public string CrewId { get; set; } = string.Empty;
            public string AssignedTerminal { get; set; } = string.Empty;

            private bool _isAvailable = true;
            public bool IsAvailable
            {
                get => _isAvailable;
                set => SetField(ref _isAvailable, value);
            }
        }
    }
}
