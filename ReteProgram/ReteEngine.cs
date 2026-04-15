using ReteCore;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteProgram
{
    public class ReteEngine
    {
        private readonly RootNode _root = new();
        private readonly Agenda _agenda = new();
        private readonly List<object> _workingMemory = new();

        // --- Public API ---

        public IReteNode Root { get { return _root; } }

        public void Assert(object fact)
        {
            if (!_workingMemory.Contains(fact))
            {
                _workingMemory.Add(fact);
                if (fact is INotifyPropertyChanged observable)
                {
                    observable.PropertyChanged += (s, e) => { _root.Refresh(s, e.PropertyName); };
                }
                _root.Assert(fact);
            }
        }

        public void Refresh(object fact, string propertyName = null)
        {
            if (fact == null) { return; }
            _root.Refresh(fact, propertyName);
        }

        public void Retract(object fact)
        {
            if (_workingMemory.Remove(fact))
            {
                _root.Retract(fact);
            }
        }

        public void FireAll()
        {
            while (_agenda.HasActivations) { _agenda.FireAll(); }
        }

        // Helper to build a "Conflict" rule easily
        public void RegisterConflictRule<T>(string name, Func<Token, T, bool> condition, Action<T, T> action, int salience = 0)
        {
            var typeNode = new ObjectTypeNode<T>();
            var alphaMem = new AlphaMemory();
            var BetaMem = new BetaMemory();
            var joinNode = new JoinNode(BetaMem, alphaMem, name, (a, b) => condition((Token)a, (T)b));
            var terminal = new TerminalNode(name, t => action((T)t.NamedFacts["A"], (T)t.NamedFacts["B"]), _agenda, salience);

            _root.AddSuccessor(typeNode);
            typeNode.AddSuccessor(alphaMem);
            joinNode.AddSuccessor(terminal);
        }

        public void Update(object fact)
        {
            // Remove the stale state
            this.Retract(fact);
            // Re-evaluate it against all conditions
            this.Assert(fact);
        }

        public Agenda Agenda
        {
            get { return _agenda; }
        }

        public ReteEngine()
        {
            // Initialize the Rete network with a root node
            _root.AddSuccessor(new ObjectTypeNode<object>()); // Start with a generic type node
        }

        public RuleBuilder<Cell> Begin(string ruleName) => new RuleBuilder<Cell>(this, ruleName);

        private readonly Dictionary<Type, object> _alphaRegistry = new();

        public AlphaMemory GetAlphaMemory<T>(string name = null, Func<T, bool> initialCondition = null)
        {
            var type = typeof(T);
            if (!_alphaRegistry.ContainsKey(type))
            {
                AlphaConditionNode<T> alphaConditionNode = null;
                var alpha = new AlphaMemory();
                if (initialCondition != null)
                {
                    alphaConditionNode = new AlphaConditionNode<T>(name, initialCondition, alpha);
                }
                var typeNode = new ObjectTypeNode<T>();
                typeNode.AddSuccessor(alphaConditionNode != null ? alphaConditionNode : alpha);

                _root.AddSuccessor(typeNode);

                _alphaRegistry[type] = alpha;
            }
            return (AlphaMemory)_alphaRegistry[typeof(T)];
        }

        public void DebugPrintNetwork(object fact)
        {
            Console.WriteLine($"\n--- Rete Trace for Fact {fact} ---");
            _root.DebugPrint(fact, 0);
            Console.WriteLine("--- End Trace ---");
        }

        // --- Internal Rete Network Components ---

        private class RootNode : IReteNode
        {
            private readonly List<IReteNode> _children = new();
            public void AddSuccessor(IReteNode node) => _children.Add(node);
            public void Assert(object fact) => _children.ForEach(c => c.Assert(fact));
            public void Retract(object fact) => _children.ForEach(c => c.Retract(fact));
            public void Refresh(object fact, string propertyName)
            {
                foreach (var child in _children)
                {
                    child.Refresh(fact, propertyName);
                }
            }
            public void DebugPrint(object fact, int level = 0)
            {
                Console.WriteLine($"[RootNode] Fact: {fact}");
                foreach (var child in _children) child.DebugPrint(fact, level + 1);
            }
        }

        private class ObjectTypeNode<T> : IReteNode
        {
            private readonly List<IReteNode> _children = new();
            public void AddSuccessor(IReteNode node) => _children.Add(node);
            public void Assert(object fact) { if (fact is T) _children.ForEach(c => c.Assert(fact)); }
            public void Retract(object fact) { if (fact is T) _children.ForEach(c => c.Retract(fact)); }
            public void Refresh(object fact, string propertyName)
            {
                if (fact is T typedFact)
                {
                    foreach (var child in _children)
                    {
                        child.Refresh(fact, propertyName);
                    }
                }
            }
            public void DebugPrint(object fact, int level = 0)
            {
                bool match = fact is T;
                string indent = new string(' ', level * 2);
                Console.WriteLine($"{indent}[TypeNode:{typeof(T).Name}] {(match ? "MATCH" : "SKIP")}");
                if (match) foreach (var child in _children) child.DebugPrint(fact, level + 1);
            }
        }

        public class TraceNode : IReteNode, ILatentMemory
        {
            private readonly string _label;
            private readonly List<IReteNode> _successors = new();
            private Token token = new Token("Dummy Token", null);

            public TraceNode(string label) => _label = label;
            public IEnumerable<Token> Tokens { get { return new List<Token> () { token }; } }
            public void AddSuccessor(IReteNode node) => _successors.Add(node);

            public void Assert(object fact)
            {
                Console.WriteLine($"[TRACE:{_label}] Fact Asserted: {fact}");
                _successors.ForEach(s => s.Assert(fact));
            }

            public void Retract(object fact)
            {
                Console.WriteLine($"[TRACE:{_label}] Fact Retracted: {fact}");
                _successors.ForEach(s => s.Retract(fact));
            }

            public void Refresh(object fact, string prop) => _successors.ForEach(s => s.Refresh(fact, prop));

            public void DebugPrint(object fact, int level = 0)
            {
                string indent = new string(' ', level * 2);
                Console.WriteLine($"{indent}[TraceNode:{_label} -- {fact}");
            }
        }

    }
}
