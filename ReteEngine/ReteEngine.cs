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
using System.IO;
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
    public class ReteEngine : IDisposable
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
        /// Retains property-change handlers so they can be detached when facts leave working memory or when the engine is disposed.
        /// </summary>
        private readonly Dictionary<object, (INotifyPropertyChanged Observable, PropertyChangedEventHandler Handler)> _propertyChangedHandlers =
            new(ReferenceEqualityComparer.Instance);
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
        /// The beta node registry manages caching and sharing of beta memory nodes and join nodes
        /// across multiple rules. This enables significant memory savings when multiple rules share
        /// common prefixes in their condition chains.
        /// </summary>
        private readonly BetaNodeRegistry _betaNodeRegistry = new();
        /// <summary>
        /// Flag to enable or disable beta-node sharing at runtime (for debugging or legacy behavior).
        /// Default is true (sharing enabled).
        /// </summary>
        private bool _enableBetaNodeSharing = true;
        /// <summary>
        /// A private flag to track whether the ReteEngine has been disposed.
        /// </summary>
        private bool _disposed;

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
        /// Initializes a new instance of the ReteEngine with optional beta-node sharing.
        /// </summary>
        /// <param name="enableBetaNodeSharing">If true (default), rules with identical prefixes share nodes.</param>
        public ReteEngine(bool enableBetaNodeSharing = true)
        {
            _enableBetaNodeSharing = enableBetaNodeSharing;
        }

        /// <summary>
        /// Gets the beta node registry. Returns null if sharing is disabled.
        /// </summary>
        public BetaNodeRegistry? BetaNodeRegistry => _enableBetaNodeSharing ? _betaNodeRegistry : null;

        /// <summary>
        /// Gets diagnostic statistics about beta-node cache performance.
        /// </summary>
        public string GetBetaNodeRegistryStatistics()
        {
            return _betaNodeRegistry.GetStatistics();
        }

        /// <summary>
        /// Clears the beta-node cache (useful for testing or resetting engine state).
        /// </summary>
        public void ClearBetaNodeCache()
        {
            _betaNodeRegistry.Clear();
        }

        /// <summary>
        /// Adds a terminal node to the Rete network. Terminal nodes represent the conditions of rules and are responsible for creating activations when 
        /// their conditions are met. When a terminal node is added, it is stored in the list of terminal nodes for management purposes. 
        /// </summary>
        /// <param name="node">The terminal node to add to the Rete network.</param>
        public void AddTerminalNode(TerminalNode node)
        {
            _terminalNodes.Add(node);
        }

        /// <summary>
        /// The public fact insertion method that introduces facts as a batch Assert.  The private working Assert method is called for each fact in the array.
        /// </summary>
        /// <param name="facts">The array of facts to assert.</param>
        /// <returns>The current instance of the ReteEngine.</returns>
        public ReteEngine Assert(params object[] facts)
        {
            foreach (var fact in facts)
            {
                Assert(fact);
            }
            return this;
        }

        /// <summary>
        /// Adds a fact once, retains its property-change handler, and asserts it into the network.
        /// </summary>
        /// <param name="fact">The fact to assert into the Rete network.</param>
        private void Assert(object fact)
        {
            bool alreadyExists = _workingMemory.Any((f) => ReferenceEquals(f, fact));
            if (!alreadyExists)
            {
                _workingMemory.Add(fact);
                if (!_disposed && fact is INotifyPropertyChanged observable)
                {
                    PropertyChangedEventHandler handler = (sender, args) =>
                    {
                        if (sender != null)
                        {
                            _agenda.AdvanceFactRevision(sender);
                            _root.Refresh(sender, args.PropertyName ?? string.Empty);
                        }
                    };
                    _propertyChangedHandlers[fact] = (observable, handler);
                    observable.PropertyChanged += handler;
                }
                _root.Assert(fact);
            }
        }

        /// <summary>
        /// Refreshes a fact in the Rete network, which may trigger re-evaluation of the fact and its propagation to successor nodes if necessary. This 
        /// method is typically called when a property of a fact changes, and it allows the Rete network to update its state based on the new information. 
        /// The Refresh method is crucial for ensuring that the Rete network remains accurate and responsive to changes in facts over time.
        /// </summary>
        /// <param name="fact">The fact to refresh in the Rete network.</param>
        /// <param name="propertyName">The name of the property that changed, if applicable.</param>
        public void Refresh(object fact, string propertyName = null)
        {
            if (fact == null) { return; }
            _root.Refresh(fact, propertyName);
        }

        /// <summary>
        /// The public fact retraction method that removes facts as a batch Retract.  The private working Retract method is called for each fact in the array.
        /// </summary>
        /// <param name="facts">The array of facts to retract.</param>
        /// <returns>The current instance of the ReteEngine.</returns>
        public ReteEngine Retract(params object[] facts)
        {
            foreach (var fact in facts)
            {
                Retract(fact);
            }
            return this;
        }

        /// <summary>
        /// Removes the exact fact instance, detaches its change handler, and retracts it from the network.
        /// </summary>
        /// <param name="fact">The fact to retract from the Rete network.</param>
        private void Retract(object fact)
        {
            int index = _workingMemory.FindIndex(item => ReferenceEquals(item, fact));
            if (index >= 0)
            {
                _workingMemory.RemoveAt(index);
                if (_propertyChangedHandlers.Remove(fact, out var subscription))
                {
                    subscription.Observable.PropertyChanged -= subscription.Handler;
                }
                _agenda.CancelActivationsByFact(fact);
                _agenda.RemoveByFact(fact);
                _root.Retract(fact);
                _agenda.ReleaseFactRevision(fact);
            }
        }

        /// <summary>
        /// Detaches all retained property-change handlers; repeated disposal is harmless.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            foreach (var subscription in _propertyChangedHandlers.Values)
            {
                subscription.Observable.PropertyChanged -= subscription.Handler;
            }

            _propertyChangedHandlers.Clear();
            _disposed = true;
        }

        /// <summary>
        /// Fires all pending activations in the agenda. This method processes the list of activations that have been triggered but not yet executed. The 
        /// method continues to fire activations until the agenda is empty, ensuring that all rules that have been triggered are executed. This is a 
        /// crucial part of the inference process in a the rule engine, allowing it to respond to changes in facts and execute the appropriate rules based 
        /// on the current state of the network. Additionally, before firing activations from the agenda, this method processes any pre-loaded facts in the
        /// working memory through the network to ensure that all facts are properly asserted and can trigger any relevant rules before activations are fired.
        /// </summary>
        /// <returns>The current instance of the ReteEngine.</returns>
        public ReteEngine FireAll()
        {
            // Work any pre-loaded facts through the network.
            // This takes care of any facts that were asserted
            // before the network was fully built.
            _workingMemory.ForEach(f => _root.Assert(f));
            _agenda.FireAll();
            return this;
        }

        /// <summary>
        /// Updates each fact independently and returns this engine for fluent chaining.
        /// A still-valid match may fire again once for a new fact revision; updates coalesce while its activation is pending.
        /// </summary>
        /// <remarks>A rule action may therefore run more than once during a fact's lifetime, once per fired revision.</remarks>
        /// <param name="facts">Facts to update; null entries are ignored.</param>
        /// <returns>The current engine.</returns>
        public ReteEngine Update(params object[] facts)
        {
            if (facts == null) return this;

            foreach (var fact in facts)
            {
                UpdateFact(fact, null);
            }
            return this;
        }

        /// <summary>
        /// Updates one fact, optionally providing the changed property name for dependency-aware reevaluation.
        /// A still-valid match may fire again once for this new revision; repeated updates while pending keep one activation.
        /// </summary>
        /// <remarks>A rule action may run again for a still-valid tuple after this update, even when the fact identity is unchanged.</remarks>
        /// <param name="fact">The fact to update; null is ignored.</param>
        /// <param name="propertyName">The changed property name, if known.</param>
        /// <returns>The current engine.</returns>
        public ReteEngine Update(object fact, string? propertyName)
        {
            UpdateFact(fact, propertyName);
            return this;
        }

        /// <summary>
        /// Update the fact in the network, asserting it if it is not already present. If the fact is already in working memory,
        /// it will be refreshed with the provided property name (or an empty string if none is provided). If the fact is not in
        /// working memory, it will be asserted into the network. This method ensures that the Rete network remains up to date
        /// with the latest information and can trigger rules based on changes to facts over time.
        /// </summary>
        /// <param name="fact">The fact to update; null is ignored.</param>
        /// <param name="propertyName">The changed property name, if known.</param>
        private void UpdateFact(object fact, string? propertyName)
        {
            if (fact == null) return;

            bool isInWorkingMemory = _workingMemory.Any(existing => ReferenceEquals(existing, fact));
            if (isInWorkingMemory)
            {
                _agenda.AdvanceFactRevision(fact);
                _root.Refresh(fact, propertyName ?? string.Empty);
            }
            else
            {
                Assert(fact);
            }
        }

        /// <summary>
        /// Starts the definition of a new rule in the Rete engine. This method initializes a new ReteBuilder instance for the specified rule name, allowing 
        /// users to define the conditions and actions of the rule using a fluent interface. The ReteBuilder provides methods for specifying the conditions 
        /// of the rule, as well as the actions to be executed when the rule is triggered. 
        /// By calling the Begin method, users can start building a new rule and ultimately add it to the Rete network, where it will be evaluated against 
        /// asserted facts and can trigger activations based on its conditions. The rule name provided to the Begin method is used for identification and 
        /// management purposes, allowing users to easily reference and potentially remove the rule from the network in the future if needed.
        /// </summary>
        /// <param name="ruleName">The name of the rule to begin defining.</param>
        /// <returns>The fluent interface for defining the start of a rule.</returns>
        public IRuleSetup Begin(string ruleName) => new FluentConverter<Fact>(this, ruleName);

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
        public AlphaMemory GetAlphaMemory<T>(string? name = null, Func<T, bool>? initialCondition = null)
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
                AlphaConditionNode<T>? alphaConditionNode = null;
                var alpha = new AlphaMemory();
                if (initialCondition != null)
                {
                    alphaConditionNode = new AlphaConditionNode<T>(name ?? "", initialCondition, alpha);
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
        /// <param name="ruleName">The name of the rule to remove.</param>
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
        /// A static convenience method to recursively prune orphaned nodes from the Rete network. This method is called after a terminal node is removed 
        /// from the network to ensure that any nodes that no longer have successors (i.e., orphaned nodes) are also removed from the network. The method 
        /// checks if the given node has any successors, and if it does not and is not the root node, it removes the node from its parent and then 
        /// recursively calls itself on the parent node. This process continues up the network until it reaches a node that has no successors or the root 
        /// node.
        /// </summary>
        /// <param name="node">The node to start pruning from.</param>
        private static void PruneOrphanedNodes(IReteNode? node)
        {
            if (node != null && node.Successors.Count() == 0 && node is not RootNode)
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
        /// A transparent tracing node that records active tokens, reports propagation events, and forwards them to successors.
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
            /// Tracks the tokens currently passing through this transparent node.
            /// </summary>
            private readonly List<Token> _tokens = new();
            /// <summary>
            /// A TextWriter used for outputting trace information. This writer can be set when the TraceNode is created, allowing for flexibility in where
            /// trace information is written. By default, it writes to the console.
            /// </summary>
            private readonly TextWriter _output;

            /// <summary>
            /// Initializes a labeled trace checkpoint with an optional output writer.
            /// </summary>
            /// <param name="label">The label included in trace reports.</param>
            /// <param name="output">Optional destination for reports; defaults to the current console writer.</param>
            public TraceNode(string label, TextWriter? output = null)
            {
                _label = string.IsNullOrWhiteSpace(label)
                    ? throw new ArgumentException("A trace label is required.", nameof(label))
                    : label;
                _output = output ?? Console.Out;
            }

            /// <summary>
            /// Returns the live token memory used by downstream joins.
            /// </summary>
            public IEnumerable<Token> Tokens => _tokens.AsReadOnly();

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
            /// Connects a successor and seeds it with tokens already held by this trace node.
            /// </summary>
            /// <param name="node">The node to add as a successor</param>
            public void AddSuccessor(IReteNode node)
            {
                if (_successors.Contains(node)) return;
                node.Parent = this;
                _successors.Add(node);
                foreach (var token in _tokens.ToList()) node.Assert(token);
            }

            /// <summary>
            /// Initializes the trace node with a set of tokens, ensuring that each token is unique and forwarding them to successors.
            /// </summary>
            /// <param name="tokens">The set of tokens to initialize the trace node with.</param>
            internal void InitializeTokens(IEnumerable<Token> tokens)
            {
                foreach (var token in tokens)
                {
                    if (_tokens.Any(existing => existing.Equals(token))) continue;
                    _tokens.Add(token);
                    WriteTrace("SNAPSHOT", token);
                }
            }

            /// <summary>
            /// Tracks and reports an assertion, then forwards a new token to successors once.
            /// </summary>
            /// <param name="fact">The fact to pass on through the network</param>
            public void Assert(object fact)
            {
                bool isNewToken = true;
                if (fact is Token token)
                {
                    int existingIndex = _tokens.FindIndex(existing => existing.Equals(token));
                    if (existingIndex >= 0)
                    {
                        _tokens[existingIndex] = token;
                        isNewToken = false;
                    }
                    else
                    {
                        _tokens.Add(token);
                    }
                }

                WriteTrace("ASSERT", fact);
                if (isNewToken)
                {
                    foreach (var successor in _successors.ToList()) successor.Assert(fact);
                }
            }

            /// <summary>
            /// Removes and forwards the retracted token, or tokens containing a retracted fact.
            /// </summary>
            /// <param name="fact">The fact to retract</param>
            public void Retract(object fact)
            {
                WriteTrace("RETRACT", fact);
                if (fact is Token token)
                {
                    _tokens.RemoveAll(existing => existing.Equals(token));
                    foreach (var successor in _successors.ToList()) successor.Retract(token);
                    return;
                }

                var affectedTokens = _tokens
                    .Where(existing => existing.NamedFacts.Values.Any(value => ReferenceEquals(value, fact)))
                    .ToList();
                foreach (var affectedToken in affectedTokens)
                {
                    _tokens.Remove(affectedToken);
                    foreach (var successor in _successors.ToList()) successor.Retract(affectedToken);
                }
                if (affectedTokens.Count == 0)
                {
                    foreach (var successor in _successors.ToList()) successor.Retract(fact);
                }
            }

            /// <summary>
            /// Reports a refresh and forwards it with the property name to affected successors.
            /// </summary>
            /// <param name="fact">The fact to refresh</param>
            /// <param name="prop">The property of the fact that has changed</param>
            public void Refresh(object fact, string propertyName)
            {
                WriteTrace("REFRESH", fact, propertyName);
                if (fact is Token token)
                {
                    int tokenIndex = _tokens.FindIndex(existing => existing.Equals(token));
                    if (tokenIndex >= 0) _tokens[tokenIndex] = token;
                    foreach (var successor in _successors.ToList()) successor.Refresh(token, propertyName);
                    return;
                }

                var affectedTokens = _tokens
                    .Where(existing => existing.NamedFacts.Values.Any(value => ReferenceEquals(value, fact)))
                    .ToList();
                if (affectedTokens.Count == 0)
                {
                    foreach (var successor in _successors.ToList()) successor.Refresh(fact, propertyName);
                    return;
                }

                foreach (var affectedToken in affectedTokens)
                {
                    foreach (var successor in _successors.ToList()) successor.Refresh(affectedToken, propertyName);
                }
            }

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
                if (ReferenceEquals(successor.Parent, this)) successor.Parent = null;
            }

            /// <summary>
            /// Reports the current token memory and recursively prints downstream node diagnostics.
            /// </summary>
            /// <param name="fact">The fact object to include in the debug output. Can be any object; its string representation will be
            /// printed.</param>
            /// <param name="level">The indentation level to apply to the debug message. Each level increases indentation by spaces.
            /// Defaults to 0.</param>
            public void DebugPrint(object fact, int level = 0)
            {
                string indent = new string(' ', level * 2);
                _output.WriteLine($"{indent}[TraceNode:{_label}] {_tokens.Count} active token(s)");
                foreach (var token in _tokens)
                {
                    _output.WriteLine($"{indent}  {FormatTraceValue(token)}");
                }
                foreach (var successor in _successors)
                {
                    successor.DebugPrint(fact, level + 1);
                }
            }

            /// <summary>
            /// Outputs a trace message to the configured output, including the operation type, fact information, and optional property name.
            /// </summary>
            /// <param name="operation">The type of operation being traced (e.g., "ASSERT", "RETRACT").</param>
            /// <param name="fact">The fact object involved in the operation.</param>
            /// <param name="propertyName">An optional property name associated with the fact.</param>
            private void WriteTrace(string operation, object fact, string? propertyName = null)
            {
                string propertySuffix = propertyName == null ? string.Empty : $" Property='{propertyName}'";
                _output.WriteLine($"[TRACE:{_label}] {operation}{propertySuffix}: {FormatTraceValue(fact)}");
            }

            /// <summary>
            /// Formats a fact or token for trace output. This method generates a string representation of the provided value, which can be a fact,\
            /// token, or any other object.
            /// </summary>
            /// <param name="value">The value to format for trace output.</param>
            /// <returns>A string representation of the value suitable for trace output.</returns>
            private static string FormatTraceValue(object? value)
            {
                if (value == null) return "null";
                if (value is Token token)
                {
                    var namedFacts = token.NamedFacts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => $"{pair.Key}={FormatTraceValue(pair.Value)}");
                    return $"Token({string.Join(", ", namedFacts)})";
                }
                if (value is IFact fact)
                {
                    return $"{value.GetType().Name}#{fact.Id}:{fact.UnderlyingObject}";
                }
                return $"{value.GetType().Name}:{value}";
            }
        }

    }
}
