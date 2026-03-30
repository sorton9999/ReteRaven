using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteCore
{

    public class JoinNode : IReteNode
    {
        private readonly string _nextName; // The name for the new fact being joined
        private readonly IReteNode? _leftInput;   // Tokens from previous joins
        private readonly AlphaMemory? _rightInput; // Individual facts
        private readonly Func<Token, object, bool> _condition;
        private readonly List<IReteNode> _successors = new();

        public JoinNode(IReteNode? left, AlphaMemory? right, string name, Func<Token, object, bool> cond)
        {
            _nextName = name;
            _leftInput = left;
            _rightInput = right;
            _condition = cond;
            if (_leftInput is BetaMemory beta)
            {
                beta.AddSuccessor(this);
            }
            else if (_leftInput is CompositeBetaMemory composite)
            {
                composite.AddSuccessor(this);
            }
            right?.AddSuccessor(this);
        }

        public void AddSuccessor(IReteNode node) => _successors.Add(node);

        // Right Activation: New fact arrives
        public void Assert(object fact)
        {
            if (fact is Token leftToken)
            {
                foreach (var rightFact in _rightInput.Facts)
                {
                    EvaluateAndPropagate(leftToken, _nextName, rightFact);
                }
            }
            else
            {
                foreach (var token in ((ILatentMemory)_leftInput).Tokens)
                {
                    EvaluateAndPropagate(token, _nextName, fact);
                }
            }
        }

        public void RightRetract(object fact)
        {
            // Any match involving this specific fact must be retracted
            foreach (var succ in _successors) succ.Retract(fact);
        }


        // Left Activation: New partial match arrives
        public void LeftAssert(Token token)
        {
            if (_rightInput == null) { return; }
            foreach (var fact in _rightInput.Facts)
            {
                if (_condition(token, fact))
                {
                    var newToken = new Token(token, _nextName, fact);
                    foreach (var s in _successors) s.Assert(newToken);
                }
            }
        }

        public void Retract(object fact) => _successors.ForEach(s => s.Retract(fact));

        public void Refresh(object factOrToken, string propertyName)
        {
            if (factOrToken is Token token)
            {
                if (_rightInput == null) { return; }
                foreach (var rightFact in _rightInput.Facts)
                {
                    this.EvaluateAndPropagate(token, propertyName, rightFact);
                }
            }
            else
            {
                if (_leftInput == null) { return; }
                foreach (var leftToken in ((ILatentMemory)_leftInput).Tokens)
                {
                    this.EvaluateAndPropagate(leftToken, propertyName, factOrToken);
                }
            }
        }

        private void EvaluateAndPropagate(Token left, string newName, object right)
        {
            if (_condition(left, right))
            {
                var newToken = new Token(left, newName, right);
                foreach (var s in _successors) { s.Assert(newToken); }
            }
            else
            {
                foreach (var s in _successors) { s.Retract(right); }
            }
        }
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 2);
            // Joins are "Right Activations" in this context
            Console.WriteLine($"{indent}[JoinNode:{_nextName}] Reached - Checking against Left Memory...");
        }
    }

}
