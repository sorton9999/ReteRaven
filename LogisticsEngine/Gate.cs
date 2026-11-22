using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReteCore;

namespace LogisticsEngine
{
    public class Gate : Fact
    {
        public string GateId { get; set; } = string.Empty;
        public string TerminalName { get; set; } = string.Empty;

        private bool _isOccupied = false;
        public bool IsOccupied
        {
            get => _isOccupied;
            set => SetField(ref _isOccupied, value);
        }
    }
}
