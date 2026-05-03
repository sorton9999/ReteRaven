//-----------------------------------------------------------------------
// <copyright file="ReteEngine.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReteCore;

namespace ReteEngine
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
        }

        public ReteBuilder<Cell> Begin(string ruleName) => new ReteBuilder<Cell>(this, ruleName);

        private readonly Dictionary<(Type Type, string Name), object> _alphaRegistry = new();

        public AlphaMemory GetAlphaMemory<T>(string name = null, Func<T, bool> initialCondition = null)
        {
            var type = typeof(T);

            // Get or create the ObjectTypeNode for type T
            var typeNode = _root.GetSuccessor<T>();
            if (typeNode == null)
            {
                typeNode = new ObjectTypeNode<T>();
                _root.AddSuccessor(typeNode);
            }

            // Use a combination of type and name as the key for the alpha registry.
            // We want to reuse the same alpha memory for the same type and name,
            // but allow different conditions to create separate alpha memories if needed.
            var registryKey = (type, name ?? "default");
            if (!_alphaRegistry.ContainsKey(registryKey))
            {
                AlphaConditionNode<T> alphaConditionNode = null;
                var alpha = new AlphaMemory();
                if (initialCondition != null)
                {
                    alphaConditionNode = new AlphaConditionNode<T>(name, initialCondition, alpha);
                }
                typeNode.AddSuccessor(alphaConditionNode != null ? alphaConditionNode : alpha);

                _alphaRegistry[registryKey] = alpha;
            }
            return (AlphaMemory)_alphaRegistry[registryKey];
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
            public ObjectTypeNode<T> GetSuccessor<T>()
            {
                return _children
                    .OfType<ObjectTypeNode<T>>()
                    .FirstOrDefault();
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
            public IEnumerable<Token> Tokens { get { return new List<Token>() { token }; } }
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
