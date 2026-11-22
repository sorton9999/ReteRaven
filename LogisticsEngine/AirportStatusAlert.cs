using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogisticsEngine
{
    public class AirportStatusAlert : Fact
    {
        private string _terminalName = string.Empty;
        public string TerminalName
        {
            get => _terminalName;
            set { _terminalName = value; OnPropertyChanged(); }
        }

        private string _alertLevel = string.Empty;
        public string AlertLevel
        {
            get => _alertLevel;
            set { _alertLevel = value; OnPropertyChanged(); }
        }

        public override Fact Snapshot() => new AirportStatusAlert
        {
            Id = this.Id,
            TerminalName = this.TerminalName,
            AlertLevel = this.AlertLevel
        };
    }
}
