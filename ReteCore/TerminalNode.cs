using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteCore
{
    public class TerminalNode : IReteNode
    {
        private readonly string _ruleName;
        private readonly Action<Token> _action;
        private readonly Agenda _agenda;
        private readonly int _salience;

        public TerminalNode(string name, Action<Token> action, Agenda agenda, int salience = 0)
        {
            _ruleName = name;
            _action = action;
            _agenda = agenda;
            _salience = salience;
        }

        public void AddSuccessor(IReteNode node) { Console.WriteLine("[TerminalNode]: Nothing should come after this node."); }

        public void Assert(object fact)
        {
            if (fact is Token finalMatch)
            {
                _agenda.Add(new Activation(_ruleName, _action, finalMatch, _salience));
            }
        }

        public void Retract(object fact)
        {
            // Find and remove any activations in the agenda that contain this fact
            _agenda.RemoveByFact(fact);
            Console.WriteLine($"[RETRACT] Pending activation for rule '{_ruleName}' cancelled.");
        }

        public void Refresh(object fact, string propertyName)
        {
            // Remove existing activations for this fact from the Agenda
            _agenda.RemoveByFact(fact);
        }
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 2);
            Console.WriteLine($"{indent}TerminalNode:{_ruleName}:{fact}");
        }
    }
}
