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
    /// <summary>
    /// The ReteEngine class represents the core of a Rete-based rule engine, responsible for managing the Rete network, working memory, 
    /// and agenda. It provides methods for asserting, retracting, and refreshing facts, as well as firing rules based on the current 
    /// state of the network. The ReteEngine maintains a root node for the Rete network, an agenda for pending activations, and a working 
    /// memory to store asserted facts. It also allows for dynamic modification of the network, such as adding terminal nodes and removing 
    /// rules. The ReteEngine serves as the main interface for interacting with the Rete-based rule engine, enabling users to define rules,
    /// manage facts, and execute the inference process effectively.
    /// </summary>
    public class ReteEngine
    {
        /// <summary>
        /// The root node of the Rete network. This node serves as the entry point for all facts asserted into the network. It is responsible 
        /// for propagating facts to its successor nodes, which may include ObjectTypeNodes, AlphaCondition Nodes, and ultimately TerminalNodes 
        /// that represent the conditions of rules. The RootNode is a special type of node that does not perform any specific processing on the 
        /// facts but simply serves as a starting point for the flow of facts through the network.
        /// </summary>
        private readonly RootNode _root = new();
        /// <summary>
        /// The agenda manages pending rule activations in the Rete-based rule engine. It maintains a list of activations that have been triggered
        /// but not yet executed. The agenda processes these activations based on their salience (priority) when the FireAll method is called, 
        /// ensuring that higher priority rules are executed before lower priority ones when multiple activations are present. The agenda allows 
        /// for efficient management of rule execution, enabling the engine to prioritize and execute rules based on the current state of the 
        /// network and the facts that have been asserted.
        /// </summary>
        private readonly Agenda _agenda = new();
        /// <summary>
        /// The storage for all facts that have been asserted into the Rete network. This working memory allows the engine to keep track of the 
        /// current state of knowledge and ensures that facts are only asserted once. When a fact is asserted, it is added to this list if it is not 
        /// already present, and when a fact is retracted, it is removed from the list. The working memory serves as a central repository for all 
        /// facts that are currently active in the system, allowing the Rete network to efficiently manage and propagate facts to successor nodes as 
        /// needed. Additionally, if a fact implements the INotifyPropertyChanged interface, the engine subscribes to property change notifications to 
        /// automatically refresh the fact in the network when its properties change, ensuring that the engine remains up-to-date with the latest 
        /// information and can trigger rules based on changes to facts over time.
        /// </summary>
        private readonly List<object> _workingMemory = new();
        /// <summary>
        /// The list of terminal nodes in the Rete network. Terminal nodes represent the conditions of rules and are responsible for creating activations 
        /// when their conditions are met. These activations are then managed by the agenda, which determines the order of rule execution based on salience.
        /// Terminal nodes are stored in this list to allow for easy management and potential removal of rules from the network.
        /// </summary>
        private readonly List<TerminalNode> _terminalNodes = new();
        /// <summary>
        /// The alpha registry is a dictionary that maps a tuple of type and name to an AlphaMemory instance. This allows for efficient retrieval of 
        /// alpha memories based on the type and name of the facts they are associated with.
        /// </summary>
        private readonly Dictionary<(Type Type, string Name), object> _alphaRegistry = new();
        /// <summary>
        /// The accessor for the root node of the Rete network. This property allows external code to access the root node, which serves as the entry point 
        /// for all facts asserted into the network.
        /// </summary>
        public IReteNode Root { get { return _root; } }
        /// <summary>
        /// The accessor for the agenda of the Rete engine. This property allows external code to access the agenda, which manages pending rule activations 
        /// in the rule engine. The agenda maintains a list of activations that have been triggered but not yet executed, and it processes these activations 
        /// based on their salience (priority) when the FireAll method is called.
        /// </summary>
        public Agenda Agenda
        {
            get { return _agenda; }
        }

        /// <summary>
        /// Adds a terminal node to the Rete network. Terminal nodes represent the conditions of rules and are responsible for creating activations when 
        /// their conditions are met. When a terminal node is added, it is stored in the list of terminal nodes for management purposes. 
        /// </summary>
        /// <param name="node"></param>
        public void AddTerminalNode(TerminalNode node)
        {
            _terminalNodes.Add(node);
        }

        /// <summary>
        /// Adds a fact to the working memory and asserts it into the Rete network. If the fact is not already present in the working memory, it is added 
        /// to the list. The PropertyChanged event is subscribed to if the fact implements INotifyPropertyChanged, allowing the engine to automatically 
        /// refresh the fact in the network when its properties change. The fact is asserted into the root node of the Rete network to propagate it through 
        /// the network and potentially trigger rule activations based on the conditions defined in the terminal nodes.
        /// </summary>
        /// <param name="fact"></param>
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

        /// <summary>
        /// Refreshes a fact in the Rete network, which may trigger re-evaluation of the fact and its propagation to successor nodes if necessary. This 
        /// method is typically called when a property of a fact changes, and it allows the Rete network to update its state based on the new information. 
        /// The Refresh method is crucial for ensuring that the Rete network remains accurate and responsive to changes in facts over time.
        /// </summary>
        /// <param name="fact"></param>
        /// <param name="propertyName"></param>
        public void Refresh(object fact, string propertyName = null)
        {
            if (fact == null) { return; }
            _root.Refresh(fact, propertyName);
        }

        /// <summary>
        /// Removes a fact from the working memory and retracts it from the Rete network. If the fact is present in the working memory, it is removed from 
        /// the list, and the fact is retracted from the root node of the Rete network to propagate the retraction through the network. This may trigger the 
        /// removal of the fact from successor nodes and the cancellation of any pending activations that were triggered by that fact, ensuring that the Rete 
        /// network remains consistent with the current state of knowledge. The Retract method is essential for maintaining the integrity of the Rete network 
        /// as facts change over time, allowing it to correctly reflect the current state of knowledge and trigger or deactivate rules as needed.
        /// </summary>
        /// <param name="fact"></param>
        public void Retract(object fact)
        {
            if (_workingMemory.Remove(fact))
            {
                _root.Retract(fact);
            }
        }

        /// <summary>
        /// Fires all pending activations in the agenda. This method processes the list of activations that have been triggered but not yet executed. The 
        /// method continues to fire activations until the agenda is empty, ensuring that all rules that have been triggered are executed. This is a 
        /// crucial part of the inference process in a the rule engine, allowing it to respond to changes in facts and execute the appropriate rules based 
        /// on the current state of the network. Additionally, before firing activations from the agenda, this method processes any pre-loaded facts in the
        /// working memory through the network to ensure that all facts are properly asserted and can trigger any relevant rules before activations are fired.
        /// </summary>
        public void FireAll()
        {
            // Work any pre-loaded facts through the network.
            // This takes care of any facts that were asserted
            // before the network was fully built.
            _workingMemory.ForEach(f => _root.Assert(f));
            while (_agenda.HasActivations) { _agenda.FireAll(); }
        }

        /// <summary>
        /// Updates a fact in the Rete network by first retracting it and then asserting it again. This method is useful when a fact has changed and needs to 
        /// be re-evaluated against the conditions in the network. By retracting the fact, it is removed from the network, and by asserting it again, it 
        /// is re-introduced and re-evaluated against all conditions, potentially triggering new activations based on the updated state of the fact. The 
        /// Update method ensures that the network remains accurate and responsive to changes in facts over time, allowing it to correctly reflect the 
        /// current state of knowledge and trigger or deactivate rules as needed.
        /// </summary>
        /// <param name="fact"></param>
        public void Update(object fact)
        {
            // Remove the stale state
            this.Retract(fact);
            // Re-evaluate it against all conditions
            this.Assert(fact);
        }

        /// <summary>
        /// Starts the definition of a new rule in the Rete engine. This method initializes a new ReteBuilder instance for the specified rule name, allowing 
        /// users to define the conditions and actions of the rule using a fluent interface. The ReteBuilder provides methods for specifying the conditions 
        /// of the rule, as well as the actions to be executed when the rule is triggered. 
        /// By calling the Begin method, users can start building a new rule and ultimately add it to the Rete network, where it will be evaluated against 
        /// asserted facts and can trigger activations based on its conditions. The rule name provided to the Begin method is used for identification and 
        /// management purposes, allowing users to easily reference and potentially remove the rule from the network in the future if needed.
        /// </summary>
        /// <param name="ruleName"></param>
        /// <returns></returns>
        public ReteBuilder<Cell> Begin(string ruleName) => new ReteBuilder<Cell>(this, ruleName);

        /// <summary>
        /// Returns an AlphaMemory for the specified type and optional name. This method retrieves an existing AlphaMemory from the alpha registry if it 
        /// exists, or creates a new one if it does not. The AlphaMemory is associated with a specific type and an optional name, allowing for efficient 
        /// retrieval and reuse of alpha memories based on the type and name of the facts they are associated with. If an initial condition is provided, an 
        /// AlphaConditionNode is created to evaluate the condition against facts of the specified type, and the AlphaMemory is added as a successor to the 
        /// ObjectTypeNode for that type. This method allows for dynamic creation and management of alpha memories in the Rete network, enabling users to 
        /// define conditions for specific types of facts and manage the flow of facts through the network based on those conditions. The use of a registry 
        /// ensures that alpha memories are reused when possible, preventing unnecessary duplication and improving the efficiency of the network.
        /// </summary>
        /// <typeparam name="T">The type of the facts for which the AlphaMemory is being retrieved or created.</typeparam>
        /// <param name="name">An optional name for the AlphaMemory, allowing for differentiation between multiple alpha memories of the same type.</param>
        /// <param name="initialCondition">An optional initial condition to be applied to the AlphaMemory, represented as a function that takes a fact of 
        /// type T and returns a boolean.</param>
        /// <returns>The AlphaMemory associated with the specified type and name.</returns>
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

        /// <summary>
        /// Removes a rule from the Rete network based on its name. This method removes any alpha memories associated with the specified rule name from 
        /// the alpha registry, removes any activations associated with the rule from the agenda, and removes the terminal node for the rule from the 
        /// network. The method also prunes any orphaned nodes that may result from the removal of the terminal node, ensuring that the network remains 
        /// clean and efficient. This allows for dynamic modification of the Rete network by removing rules that are no longer needed or relevant, while 
        /// maintaining the integrity of the network. 
        /// </summary>
        /// <param name="ruleName"></param>
        public void RemoveRule(string ruleName)
        {
            if (string.IsNullOrEmpty(ruleName))
            {
                return;
            }
            if (_alphaRegistry.Keys.Any(k => k.Name == ruleName))
            {
                var keysToRemove = _alphaRegistry.Keys.Where(k => k.Name == ruleName).ToList();
                foreach (var key in keysToRemove)
                {
                    _alphaRegistry.Remove(key);
                    Console.WriteLine($"[ReteEngine] Removed alpha memory for rule: {ruleName} (Type: {key.Type.Name})");
                }
            }
            if (_agenda.RemoveActivationsByRule(ruleName))
            {
                Console.WriteLine($"[ReteEngine] Removed activations for rule: {ruleName}");
            }
            else
            {
                Console.WriteLine($"[ReteEngine] No activations found for rule: {ruleName}");
            }

            var terminalNode = _terminalNodes.FirstOrDefault(t => t.RuleMetadata.Name == ruleName);
            if (terminalNode != null)
            {
                terminalNode.RemoveFromParentNode();
                Console.WriteLine($"[ReteEngine] Removed terminal node for rule: {ruleName}");
                _terminalNodes.Remove(terminalNode);
                PruneOrphanedNodes(terminalNode.Parent);
            }
            else
            {
                Console.WriteLine($"[ReteEngine] No terminal node found for rule: {ruleName}");
            }
        }

        /// <summary>
        /// A convenience method to recursively prune orphaned nodes from the Rete network. This method is called after a terminal node is removed from the 
        /// network to ensure that any nodes that no longer have successors (i.e., orphaned nodes) are also removed from the network. The method checks if 
        /// the given node has any successors, and if it does not and is not the root node, it removes the node from its parent and then recursively calls 
        /// itself on the parent node. This process continues up the network until it reaches a node that has no successors or the root node.
        /// </summary>
        /// <param name="node">The node to start pruning from.</param>
        private void PruneOrphanedNodes(IReteNode? node)
        {
            if (node.Successors.Count() == 0 && node is not RootNode)
            {
                var parent = node.Parent;
                parent?.RemoveSuccessor(node);
                if (parent != null) PruneOrphanedNodes(parent);
            }
        }

        /// <summary>
        /// A debugging method to visualize the flow of a fact through the Rete network. This method prints out the structure of the network and how the 
        /// specified fact is processed at each node.
        /// </summary>
        /// <param name="fact">The fact to print information from</param>
        public void DebugPrintNetwork(object fact)
        {
            Console.WriteLine($"\n--- Rete Trace for Fact {fact} ---");
            _root.DebugPrint(fact, 0);
            Console.WriteLine("--- End Trace ---");
        }

        /// <summary>
        /// The TraceNode class is a special type of node in the Rete network that is used for debugging and tracing the flow of facts through the 
        /// network. It implements the IReteNode interface and provides methods for asserting, retracting, and refreshing facts. When a fact is put 
        /// into the network, it prints out a message indicating the operation being performed and the fact involved, along with the label of the 
        /// TraceNode for context.
        /// </summary>
        public class TraceNode : IReteNode, ILatentMemory
        {
            /// <summary>
            /// A label for the TraceNode, used to identify it in debug output. This label can be set when the TraceNode is created and is included in the 
            /// messages printed when facts are asserted, retracted, or refreshed through this node.
            /// </summary>
            private readonly string _label;

            /// <summary>
            /// Keeps track of successor nodes that will receive facts asserted, retracted, or refreshed through this TraceNode. Each successor is 
            /// an IReteNode that will be affected by operations performed on this node. The collection is initialized as an empty list and can be 
            /// modified by adding new successor nodes using the AddSuccessor method. The order of successors in the list may affect the order in 
            /// which facts are propagated to them, but does not affect the logic of the network.
            /// </summary>
            private readonly List<IReteNode> _successors = new();

            /// <summary>
            /// A dummy token is used to satisfy the requirement of the ILatentMemory interface, which expects a collection of tokens. In this case, 
            /// the TraceNode does not actually manage any tokens, so a single dummy token is created to fulfill this requirement. The token is 
            /// initialized with a name ("Dummy Token") and a null value, indicating that it does not represent any meaningful state or fact in the 
            /// network. This allows the TraceNode to implement the ILatentMemory interface without needing to manage actual tokens, while still 
            /// providing the necessary structure for tracing and debugging the flow of facts through the network. The presence of this dummy token 
            /// allows the TraceNode to be used in the Rete network for tracing purposes without affecting the logic of the network or the flow of 
            /// facts.
            /// </summary>
            private Token token = new Token("Dummy Token", null);

            /// <summary>
            /// The constructor for the TraceNode class takes a label as a parameter, which is used to identify the node in debug output.
            /// </summary>
            /// <param name="label"></param>
            public TraceNode(string label) => _label = label;

            /// <summary>
            /// This object keeps track of the current token state for this TraceNode. It is initialized with a dummy token and can be updated as 
            /// facts are processed through the network. The token can be used to store information about the current state of the node, such as 
            /// the fact being processed or any relevant metadata. This allows for more detailed tracing and debugging of the flow of facts through 
            /// the network, as the token can provide context about what is happening at this node when a fact is asserted, retracted, or refreshed.
            /// </summary>
            public IEnumerable<Token> Tokens { get { return new List<Token>() { token }; } }

            /// <summary>
            /// Returns an enumerable of successor nodes. In this implementation, there is typically one successor node that receives 
            /// facts that satisfy the condition defined by the predicate.
            /// </summary>
            public IEnumerable<IReteNode> Successors => _successors;

            /// <summary>
            /// The parent node in the Rete network. This property allows for navigation back up the network from this node.
            /// </summary>
            public IReteNode? Parent { get; set; }

            /// <summary>
            /// Adds a successor node to this TraceNode. This method is used to build the structure of the Rete network by connecting nodes together.
            /// </summary>
            /// <param name="node">The node to add as a successor</param>
            public void AddSuccessor(IReteNode node) { node.Parent = this; _successors.Add(node); }

            /// <summary>
            /// Asserts a fact into the TraceNode and passes it downstream in the network. A message will print out indicating that the fact has 
            /// been asserted along with the label of the TraceNode.
            /// </summary>
            /// <param name="fact">The fact to pass on through the network</param>
            public void Assert(object fact)
            {
                Console.WriteLine($"[TRACE:{_label}] Fact Asserted: {fact}");
                _successors.ForEach(s => s.Assert(fact));
            }

            /// <summary>
            /// Retracts a fact from the TraceNode, which may trigger the removal of the fact from successor nodes.
            /// </summary>
            /// <param name="fact">The fact to retract</param>
            public void Retract(object fact)
            {
                Console.WriteLine($"[TRACE:{_label}] Fact Retracted: {fact}");
                _successors.ForEach(s => s.Retract(fact));
            }

            /// <summary>
            /// Refreshes a fact in the TraceNode, which may trigger re-evaluation of the fact and its propagation to successor nodes if necessary.
            /// </summary>
            /// <param name="fact">The fact to refresh</param>
            /// <param name="prop">The property of the fact that has changed</param>
            public void Refresh(object fact, string prop) => _successors.ForEach(s => s.Refresh(fact, prop));

            /// <summary>
            /// Removes a successor node from this RootNode. This method is used when a successor node is no longer needed or when the 
            /// structure of the Rete network changes. When a successor is removed, it will no longer receive any tokens asserted, 
            /// retracted, or refreshed through this node. The method simply removes the specified child node from the list of successors, 
            /// ensuring that it is no longer part of the propagation path for tokens. This allows for dynamic modification of the Rete network 
            /// as it evolves over time and ensures that nodes can be added or removed as needed without affecting the overall functionality of 
            /// the system.
            /// </summary>
            /// <param name="successor">The successor node to remove. Cannot be null.</param>
            public void RemoveSuccessor(IReteNode successor)
            {
                _successors.Remove(successor);
            }

            /// <summary>
            /// Writes a formatted debug message to the console that includes the specified fact and the current number of
            /// successors.
            /// </summary>
            /// <param name="fact">The fact object to include in the debug output. Can be any object; its string representation will be
            /// printed.</param>
            /// <param name="level">The indentation level to apply to the debug message. Each level increases indentation by spaces.
            /// Defaults to 0.</param>
            public void DebugPrint(object fact, int level = 0)
            {
                string indent = new string(' ', level * 2);
                Console.WriteLine($"{indent}[TraceNode:{_label} -- {fact}");
            }
        }

    }
}
