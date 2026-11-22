using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogisticsEngine
{
    public class WeatherCondition : Fact
    {
        private string _terminalName = string.Empty;
        public string TerminalName
        {
            get => _terminalName;
            set => SetField(ref _terminalName, value);
        }

        private string _severity = "Clear";
        public string Severity
        {
            get => _severity;
            set => SetField(ref _severity, value);
        }
    }
}
