using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteCore
{
    public class CompositeBetaMemory : IReteNode, ILatentMemory
    {
        // Tracks specific tokens and how many branches support them
        private readonly Dictionary<Token, int> _supportedMatches = new();
        private readonly List<IReteNode> _successors = new();


        public IEnumerable<Token> Tokens
        {
            get
            {
                return _supportedMatches.Keys;
            }
        }

        public void AddSuccessor(IReteNode node) => _successors.Add(node);

        public void Assert(object fact)
        {
            if (fact is Token token)
            {
                if (!_supportedMatches.ContainsKey(token))
                {
                    _supportedMatches[token] = 1;
                    // First time seeing this match: Propagate to next node (And/Then)
                    foreach (var successor in _successors)
                    {
                        successor.Assert(fact);
                    }
                }
                else
                {
                    // Already active via another branch: Just increment support count
                    _supportedMatches[token]++;
                }
            }
        }

        public void Retract(object fact)
        {
            if (fact is Token token && _supportedMatches.TryGetValue(token, out int count))
            {
                if (count <= 1)
                {
                    // Last branch supporting this match is gone: Remove and propagate
                    _supportedMatches.Remove(token);
                    foreach (var successor in _successors)
                    {
                        successor.Retract(fact);
                    }
                }
                else
                {
                    // Match is still supported by other branches: Just decrement
                    _supportedMatches[token] = count - 1;
                }
            }
        }


        public void Refresh(object fact, string propertyName)
        {
            // Propagate the refresh to all active branches
            foreach (var successor in _successors)
            {
                successor.Refresh(fact, propertyName);
            }
        }

        public void DebugPrint(object fact, int level = 0)
        {
            if (fact is Token token)
            {
                string indent = new string(' ', level * 2);
                bool isActive = _supportedMatches.ContainsKey(token);
                Console.WriteLine($"{indent}[OR Node] Fact: {fact}, Active: {isActive}");

                foreach (var successor in _successors)
                {
                    successor.DebugPrint(fact, level + 1);
                }
            }
            else
            {
                Console.WriteLine("A token didn't come in to provide a memory.");
            }
        }
    }
}
