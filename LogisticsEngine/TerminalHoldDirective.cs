using ReteCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogisticsEngine
{
    public class TerminalHoldDirective : Fact
    {
        private string _teminalHoldDirective = string.Empty;
        public string TerminalName
        {
            get { return _teminalHoldDirective; }
            set { SetField(ref _teminalHoldDirective, value); }
        }

        private string _reason = string.Empty;
        public string Reason
        {
            get { return _reason; }
            set { SetField(ref _reason, value); }
        }

        public override Fact Snapshot() => new TerminalHoldDirective
        {
            Id = this.Id,
            TerminalName = this.TerminalName,
            Reason = this.Reason
        };

        /// <summary>
        /// Creates a directive with a deterministic FactId per TerminalName so multiple
        /// independent rules can logically support the exact same directive instance.
        /// </summary>
        public static TerminalHoldDirective ForTerminal(string terminalName, string reason)
        {
            // Deterministic UUIDv5-style hash from TerminalName so both rules target the same FactId
            byte[] hash = System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes($"TerminalHoldDirective:{terminalName}"));
            return new TerminalHoldDirective
            {
                Id = new Guid(hash),
                TerminalName = terminalName,
                Reason = reason
            };
        }
    }
}
