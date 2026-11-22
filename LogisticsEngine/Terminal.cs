using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace LogisticsEngine
{
    public class Terminal : Fact
    {
        private string _terminalName = String.Empty;
        public string TerminalName 
        {
            get => _terminalName;
            set => SetField(ref _terminalName, value);
        }

        private int _maxCapacity = 0;
        public int MaxCapacity 
        {
            get => _maxCapacity;
            set => SetField(ref _maxCapacity, value);
        }

    }
}
