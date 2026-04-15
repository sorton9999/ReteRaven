using ReteCore;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteProgram
{
    /// <summary>
    /// Provides a fluent interface for constructing and configuring rules in a Rete-based rule engine.
    /// </summary>
    /// <remarks>Use the RuleBuilder to define the sequence of conditions and actions that make up a rule. The
    /// builder supports chaining methods to specify matching patterns, logical combinations (AND/OR), and actions to
    /// execute when the rule is triggered. Each method call adds a new node or condition to the rule's execution graph.
    /// The builder is not thread-safe and should be used from a single thread during rule construction.</remarks>
    /// <typeparam name="TInitial">The type of the initial fact or object that the rule operates on.</typeparam>
    public class RuleBuilder<TInitial>
    {
        /// <summary>
        /// The ReteEngine instance that this builder will configure. 
        /// The builder will add nodes and conditions to the engine's 
        /// network as the rule is constructed.
        /// </summary>
        private readonly ReteEngine _engine;
        /// <summary>
        /// The name of the rule being built. This is used for identification in the 
        /// engine and for debugging purposes.
        /// </summary>
        private readonly string _ruleName;
        /// <summary>
        /// The last node in the current rule's chain of conditions. This is used to keep track of 
        /// where to add the next condition or action.
        /// </summary>
        private IReteNode? _lastNode;

        /// <summary>
        /// Initializes a new instance of the RuleBuilder class with the 
        /// specified Rete engine and rule name.
        /// </summary>
        /// <param name="engine">The ReteEngine instance that will be used to build and manage the rule. Cannot be null.</param>
        /// <param name="name">The name to assign to the rule being built. Cannot be null or empty.</param>
        public RuleBuilder(ReteEngine engine, string name)
        {
            _engine = engine;
            _ruleName = name;
        }

        /// <summary>
        /// Gets the name of the rule associated with this instance.
        /// </summary>
        public string RuleName { get { return _ruleName; } }

        /// <summary>
        /// Adds a match condition for facts of the specified type to the rule being built.
        /// </summary>
        /// <remarks>Use this method to specify that the rule should match facts of type T. Multiple calls
        /// to Match can be chained to build more complex rules. The name parameter is used to reference this match in
        /// subsequent rule conditions or actions.</remarks>
        /// <typeparam name="T">The type of fact to match in the rule.</typeparam>
        /// <param name="name">The name used to identify this match condition within the rule. Cannot be null or empty.</param>
        /// <param name="debugLabel">An optional label used for debugging purposes. If specified, diagnostic output will be written when the
        /// match is added.</param>
        /// <returns>The current RuleBuilder instance, enabling further configuration of the rule.</returns>
        public RuleBuilder<TInitial> Match<T>(string name, string? debugLabel = null, Func<T, bool> initialCondition = null)
        {
            if (debugLabel != null)
            {
                Console.WriteLine($"===> Start {name}");
            }
            var alpha = _engine.GetAlphaMemory<T>(name, initialCondition);
            var beta = new BetaMemory();
            var adapter = new AlphaToBetaAdapter(beta, name);

            alpha.AddSuccessor(adapter);
            _lastNode = beta;
            return this;
        }

        /// <summary>
        /// Adds a join condition to the rule, requiring that both the previous conditions and the specified join
        /// condition are satisfied for a fact to match.
        /// </summary>
        /// <remarks>Use this method to combine multiple conditions in a rule using logical AND semantics.
        /// The join condition is evaluated for each fact of type T, and only facts that satisfy the condition are
        /// considered matches. If a debug label is provided, diagnostic output is written to the console each time the
        /// join condition is evaluated.</remarks>
        /// <typeparam name="T">The type of fact to join with the current rule conditions.</typeparam>
        /// <param name="name">The name used to identify the join node within the rule network.</param>
        /// <param name="joinCondition">A function that determines whether a given fact of type T should be joined with the current token. Returns
        /// <see langword="true"/> if the fact matches the join condition; otherwise, <see langword="false"/>.</param>
        /// <param name="debugLabel">An optional label used for debugging output. If specified, debug information about the join evaluation is
        /// written to the console.</param>
        /// <returns>The current <see cref="RuleBuilder{TInitial}"/> instance, allowing for method chaining.</returns>
        public RuleBuilder<TInitial> And<T>(string name, Func<Token, T, bool> joinCondition, string? debugLabel = null)
        {
            Func<Token, T, bool> wrapCondition = (token, fact) =>
            {
                bool result = joinCondition(token, fact);
                if (debugLabel != null)
                {
                    Console.WriteLine($"[DEBUG:{debugLabel}] Result: {result} for fact {fact}");
                }
                return result;
            };
            var alpha = _engine.GetAlphaMemory<T>();
            JoinNode join = new JoinNode(_lastNode, alpha, name, (token, fact) => wrapCondition(token, (T)fact));
            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(join);
            }
            else if (_lastNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(join);
            }

            var betaMemory = new BetaMemory();
            join.AddSuccessor(betaMemory);

            _lastNode = betaMemory;

            return this;
        }

        /// <summary>
        /// Adds a logical OR branch to the rule, allowing the rule to match if any of the specified conditions are
        /// satisfied for facts of the given type.
        /// </summary>
        /// <remarks>Each condition in <paramref name="orConditions"/> is evaluated independently against
        /// facts of type <typeparamref name="T"/>. The rule continues if at least one condition is met. This method
        /// enables branching logic within a rule, similar to a logical OR operation.</remarks>
        /// <typeparam name="T">The type of fact to which the conditions apply.</typeparam>
        /// <param name="name">The name used to identify this branch in the rule for debugging or tracing purposes.</param>
        /// <param name="debugLabel">An optional label used for debugging output. If specified, debug information about the evaluation of each 
        /// OR condition is written to the console.</param>
        /// <param name="orConditions">One or more predicate functions that define the alternative conditions. The rule matches if any of these
        /// predicates return <see langword="true"/> for a given fact.</param>
        /// <returns>The current <see cref="RuleBuilder{TInitial}"/> instance, allowing further rule configuration.</returns>
        public RuleBuilder<TInitial> Or<T>(string name, string? debugLabel = null, params Func<Token, T, bool>[] orConditions)
        {
            // Save the starting point so all branches begin from the same prefix
            var branchStartNode = _lastNode; // Previous node in the chain

            // The collector node that merges all paths
            var orNode = new CompositeBetaMemory();

            foreach (var condition in orConditions)
            {
                var wrapCondition = (Token token, T fact) =>
                {
                    bool result = condition(token, fact);
                    if (debugLabel != null)
                    {
                        Console.WriteLine($"[DEBUG:{debugLabel}] Result: {result} for fact {fact}");
                    }
                    return result;
                };

                var alpha = _engine.GetAlphaMemory<T>();

                // Create a JoinNode for this specific condition
                var join = new JoinNode(branchStartNode, alpha, name,
                    (token, fact) => wrapCondition(token, (T)fact));

                if (branchStartNode is BetaMemory beta)
                {
                    beta.AddSuccessor(join);
                }
                else if (branchStartNode is CompositeBetaMemory compositeBeta)
                {
                    compositeBeta.AddSuccessor(join);
                }

                alpha.AddSuccessor(join);

                // Point this branch to the OrNode
                join.AddSuccessor(orNode);
                //_lastNode = orNode;
            }
            // Update the builder state: the rest of the rule now follows the orNode
            _lastNode = orNode;

            return this;
        }

        /// <summary>
        /// Adds a negated join condition to the rule, specifying that the rule should only match if there are no facts of 
        /// type T that satisfy the given join condition.
        /// </summary>
        /// <typeparam name="T">The type of fact to which the conditions apply.</typeparam>
        /// <param name="name">The name used to identify this branch in the rule for debugging or tracing purposes.</param>
        /// <param name="joinCondition">A function that determines whether a given fact of type T should be joined with the current token.</param>
        /// <param name="debugLabel">An optional label used for debugging output. If specified, debug information about the evaluation of the 
        /// condition is written to the console.</param>
        /// <returns>The current <see cref="RuleBuilder{TInitial}"/> instance, allowing further rule configuration.</returns>
        public RuleBuilder<TInitial> AndNot<T>(string name, Func<Token, T, bool> joinCondition, string? debugLabel = null)
        {
            Func<Token, T, bool> wrapCondition = (token, fact) =>
            {
                bool result = joinCondition(token, fact);
                if (debugLabel != null)
                {
                    Console.WriteLine($"[DEBUG:{debugLabel}] Result: {result} for fact {fact}");
                }
                return !result; // Negate the condition for AND NOT semantics
            };
            var alpha = _engine.GetAlphaMemory<T>();
            JoinNode join = new JoinNode(_lastNode, alpha, name, (token, fact) => wrapCondition(token, (T)fact));
            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(join);
            }
            else if (_lastNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(join);
            }
            var betaMemory = new BetaMemory();
            join.AddSuccessor(betaMemory);
            _lastNode = betaMemory;
            return this;
        }

        /// <summary>
        /// Adds a "not" condition to the rule, specifying that there must be no facts of type T that satisfy the given 
        /// join condition for the rule to match.
        /// </summary>
        /// <typeparam name="T">The type of fact to which the conditions apply.</typeparam>
        /// <param name="name">The name used to identify this branch in the rule for debugging or tracing purposes.</param>
        /// <param name="joinCondition">A function that determines whether a given fact of type T should be joined with the current token.</param>
        /// <param name="debugLabel">An optional label used for debugging output. If specified, debug information about the evaluation of the 
        /// condition is written to the console.</param>
        /// <returns>The current <see cref="RuleBuilder{TInitial}"/> instance, allowing further rule configuration.</returns>
        public RuleBuilder<TInitial> Not<T>(string name, Func<Token, T, bool> joinCondition, string? debugLabel = null)
        {
            Func<Token, T, bool> wrapCondition = (token, fact) =>
            {
                bool result = joinCondition(token, fact);
                if (debugLabel != null)
                {
                    Console.WriteLine($"[DEBUG:{debugLabel}] Result: {result} for fact {fact}");
                }
                return result;
            };
            var alpha = _engine.GetAlphaMemory<T>();
            var notNode = new NotNode(name, (token, fact) => wrapCondition(token, (T)fact));
            alpha.AddSuccessor(notNode);

            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(notNode);
            }
            else if (_lastNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(notNode);
            }
            var betaMemory = new BetaMemory();
            notNode.AddSuccessor(betaMemory);
            _lastNode = betaMemory;
            return this;
        }

        /// <summary>
        /// Adds an "exists" condition to the rule, specifying that there must be at least one fact of type T that satisfies
        /// the given join condition for the rule to match.
        /// </summary>
        /// <typeparam name="T">The type of fact to which the conditions apply.</typeparam>
        /// <param name="name">The name used to identify this branch in the rule for debugging or tracing purposes.</param>
        /// <param name="joinCondition">A function that determines whether a given fact of type T should be joined with the current token.</param>
        /// <param name="debugLabel">An optional label used for debugging output. If specified, debug information about the evaluation of the 
        /// condition is written to the console.</param>
        /// <returns>The current <see cref="RuleBuilder{TInitial}"/> instance, allowing further rule configuration.</returns>
        public RuleBuilder<TInitial> Exists<T>(string name, Func<Token, T, bool> joinCondition, string? debugLabel = null)
        {

            Func<Token, T, bool> wrapCondition = (token, fact) =>
            {
                bool result = joinCondition(token, fact);
                if (debugLabel != null)
                {
                    Console.WriteLine($"[DEBUG:{debugLabel}] Result: {result} for fact {fact}");
                }
                return result;
            };

            var alphaMem = _engine.GetAlphaMemory<T>();

            JoinKeyExtractor joinKeyExtractor = new JoinKeyExtractor();
            ExistsNode existsNode = null;
            IndexedExistsNode indexedExistsNode = null;
            try
            {
                var (leftKey, rightKey) = joinKeyExtractor.Extract((Token t, object f) => wrapCondition(t, (T)f));
                indexedExistsNode = new IndexedExistsNode(name, leftKey, rightKey);
            }
            catch
            {
                existsNode = new ExistsNode(name, (token, fact) => wrapCondition(token, (T)fact));
            }
            alphaMem.AddSuccessor(existsNode != null ? existsNode : indexedExistsNode);

            //var existsNode = new ExistsNode((token, fact) => wrapCondition(token, fact));
            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(existsNode != null ? existsNode : indexedExistsNode);
            }
            else if (_lastNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(existsNode != null ? existsNode : indexedExistsNode);
            }
            var betaMemory = new BetaMemory();
            //existsNode.AddSuccessor(betaMemory);
            if (existsNode != null)
            {
                existsNode.AddSuccessor(betaMemory);
            }
            else if (indexedExistsNode != null)
            {
                indexedExistsNode.AddSuccessor(betaMemory);
            }
            _lastNode = betaMemory;
            return this;
        }

        /// <summary>
        /// Adds a terminal action to the rule that will be executed when the rule is triggered.  This method finalizes the rule definition
        /// by specifying the consequence of the rule. The action will be invoked each time the rule's conditions are satisfied. Salience 
        /// can be used to control the order in which rules are executed when multiple rules are eligible to fire. Higher salience
        /// values indicate higher priority, meaning that rules with greater salience will be executed before those with lower salience when 
        /// they are both eligible to fire. The default salience is 0, and rules with the same salience will be executed in the order they 
        /// were added to the agenda. Use this method to specify the action that should be taken when the rule's conditions are met, and 
        /// optionally assign a salience to influence the execution order of the rule relative to others.
        /// </summary>
        /// <remarks>Use this method to specify the consequence of the rule. The action will be invoked
        /// each time the rule's conditions are satisfied. Salience can be used to control the order in which rules are
        /// executed when multiple rules are eligible.</remarks>
        /// <param name="action">The action to execute when the rule fires. The action receives the matched token as its parameter. Cannot be
        /// null.</param>
        /// <param name="salience">The priority of the rule when multiple rules are eligible to fire. Higher values indicate higher priority.
        /// The default is 0.</param>
        /// <returns>The current <see cref="RuleBuilder{TInitial}"/> instance, enabling further rule configuration.</returns>
        public RuleBuilder<TInitial> Then(Action<Token> action, int salience = 0)
        {

            var terminal = new TerminalNode(_ruleName, action, _engine.Agenda, salience);
            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(terminal);
            }
            else if (_lastNode is CompositeBetaMemory compositeBeta)
            {
                compositeBeta.AddSuccessor(terminal);
            }
            return this;
        }

        /// <summary>
        /// Adds a trace node to the rule builder pipeline with the specified label, enabling inspection of rule
        /// evaluation at this point.
        /// </summary>
        /// <remarks>Use trace nodes to monitor or debug the flow of facts through the rule network.
        /// Tracing can help diagnose rule behavior or performance issues by providing labeled checkpoints in the
        /// evaluation process.</remarks>
        /// <param name="label">The label to associate with the trace node. Used to identify the trace point during rule evaluation.</param>
        /// <returns>The current <see cref="RuleBuilder{TInitial}"/> instance, allowing for method chaining.</returns>
        public RuleBuilder<TInitial> Trace(string label)
        {
            var tracer = new ReteEngine.TraceNode(label);
            if (_lastNode is BetaMemory beta)
            {
                beta.AddSuccessor(tracer);
            }
            _lastNode = tracer;
            return this;
        }

        /// <summary>
        /// Asserts the specified fact into the current context or knowledge base.
        /// </summary>
        /// <param name="fact">The fact to assert. Cannot be null.</param>
        public void Assert(object fact)
        {
            _lastNode?.Assert(fact);
        }

        /// <summary>
        /// Begins the rule definition by specifying the initial AlphaMemory node and the fact name to match against.
        /// </summary>
        /// <remarks>Call this method as the first step when constructing a rule. Subsequent calls to
        /// JoinWith or other builder methods will extend the rule from this starting point.</remarks>
        /// <param name="alpha">The AlphaMemory node that serves as the starting point for the rule's pattern matching.</param>
        /// <param name="factName">The name of the fact to be matched by the initial AlphaMemory node. Cannot be null or empty.</param>
        /// <returns>A RuleBuilder<TInitial> instance configured to continue building the rule from the specified AlphaMemory
        /// node.</returns>
        public RuleBuilder<TInitial> StartWith(AlphaMemory alpha, string factName)
        {
            // Create the very first BetaMemory for this rule's chain
            var firstBeta = new BetaMemory();

            // Use the Adapter to convert single facts from Alpha into Tokens for Beta
            var adapter = new AlphaToBetaAdapter(firstBeta, factName);

            // Link the AlphaMemory to the Adapter
            alpha.AddSuccessor(adapter);

            // Update the tracker so the next 'JoinWith' knows where to connect
            _lastNode = firstBeta;

            return this;
        }

        /// <summary>
        /// Adds a join node to the rule network that combines the current beta memory with the specified alpha memory
        /// using the given join condition.
        /// </summary>
        /// <remarks>Use this method to extend the rule network by specifying additional join conditions
        /// between working memory elements. The join condition is evaluated for each combination of tokens and facts
        /// from the respective memories.</remarks>
        /// <typeparam name="TNext">The type of the facts stored in the alpha memory to be joined.</typeparam>
        /// <param name="nextAlpha">The alpha memory node to join with the current beta memory. Cannot be null.</param>
        /// <param name="condition">A function that determines whether a token from the beta memory and a fact from the alpha memory should be
        /// joined. Returns <see langword="true"/> to join the pair; otherwise, <see langword="false"/>.</param>
        /// <returns>The current <see cref="RuleBuilder{TInitial}"/> instance, enabling method chaining.</returns>
        public RuleBuilder<TInitial> JoinWith<TNext>(ReteCore.AlphaMemory nextAlpha, Func<Token, TNext, bool> condition)
        {
            BetaMemory? beta = _lastNode as BetaMemory;
            var join = new ReteCore.JoinNode(beta!, nextAlpha, "dummy", (t, f) => condition(t, (TNext)f));
            var nextBeta = new ReteCore.BetaMemory();
            join.AddSuccessor(nextBeta);
            _lastNode = nextBeta;
            return this;
        }

        /// <summary>
        /// Defines the terminal action to execute when the rule is triggered, and adds the rule to the specified agenda
        /// with an optional salience.
        /// </summary>
        /// <remarks>This method finalizes the rule definition by specifying the action to perform when
        /// the rule conditions are met. The rule is then registered with the provided agenda. If multiple rules are
        /// eligible to fire, those with higher salience values are prioritized.</remarks>
        /// <param name="agenda">The agenda to which the rule will be added. Cannot be null.</param>
        /// <param name="action">The action to execute when the rule fires. Receives the token that caused the rule to trigger. Cannot be
        /// null.</param>
        /// <param name="salience">The priority of the rule within the agenda. Higher values indicate higher priority. The default is 0.</param>
        public void Then(Agenda agenda, Action<Token> action, int salience = 0)
        {
            var terminal = new TerminalNode(_ruleName, action, agenda, salience);
            _lastNode = terminal;
            _lastNode.Assert(new Token("end", 250));
        }
    }

    /// <summary>
    /// Adapts an Alpha Memory output (single fact) to a Beta Memory input (Token).
    /// This allows the first JoinNode in a chain to receive a Token on its left.
    /// </summary>
    public class AlphaToBetaAdapter : IReteNode
    {
        /// <summary>
        /// The BetaMemory instance that this adapter will feed tokens into. When a fact is asserted into the adapter, it will be 
        /// wrapped into a Token and asserted into this BetaMemory.
        /// </summary>
        private readonly BetaMemory _betaMemory;
        /// <summary>
        /// The name of the fact being adapted. This is used to create a Token with a consistent identifier for the fact when it is 
        /// wrapped and asserted into the BetaMemory. The fact name helps maintain clarity in the rule network and can be used for 
        /// debugging or tracing purposes to identify which facts are being processed through this adapter.
        /// </summary>
        private readonly string _factName;

        /// <summary>
        /// This constructor initializes a new instance of the AlphaToBetaAdapter class with the specified BetaMemory and fact name. 
        /// The adapter will take facts asserted into it, wrap them into Tokens with the given fact name, and assert those tokens into 
        /// the provided BetaMemory. This allows for seamless integration of Alpha Memory outputs into the Beta Memory processing 
        /// pipeline, enabling the first JoinNode in a rule to receive tokens that represent individual facts from the Alpha Memory.
        /// </summary>
        /// <param name="betaMemory">The BetaMemory to add the token and associated fact to</param>
        /// <param name="factName">The name of the fact</param>
        /// <exception cref="ArgumentNullException">Thrown on a null BetaMemory argument</exception>
        public AlphaToBetaAdapter(BetaMemory betaMemory, string factName)
        {
            _betaMemory = betaMemory ?? throw new ArgumentNullException(nameof(betaMemory));
            _factName = factName;
        }

        /// <summary>
        /// Adds a successor node to this adapter. In the context of the Rete network, this method is used to connect the output of 
        /// the adapter (which produces Tokens) to the next node in the network, typically a JoinNode or another BetaMemory. When a 
        /// fact is asserted into this adapter, it will be wrapped into a Token and then passed to all successor nodes that have been 
        /// added through this method. This allows the adapter to serve as a bridge between Alpha Memory outputs and the Beta Memory 
        /// processing that follows in the rule evaluation process.
        /// </summary>
        /// <param name="node"></param>
        public void AddSuccessor(IReteNode node)
        {
            Console.WriteLine("[AlphaToBetaAdapter] -- This node currently does not implement AddSuccessor.\n" +
            "Operations are done on the added BetaMemory explicitly to start the chain.\n" +
            "In the future this may be used for BetaMemory to BetaMemory connections.");
        }

        /// <summary>
        /// Asserts a fact into the adapter. The fact is wrapped into a Token with the specified fact name and then asserted into 
        /// the connected BetaMemory. This allows the first JoinNode in the rule network to receive a Token representing the fact 
        /// from the Alpha Memory, enabling it to participate in the rule evaluation process as if it were a standard token from a 
        /// BetaMemory. The fact name is used to maintain clarity and consistency in the tokens being processed through the network.
        /// </summary>
        /// <param name="fact">The fact object to be asserted and passed to successor nodes. Cannot be null.</param>
        public void Assert(object fact)
        {
            // Wrap the single fact into the first Token of a potential chain
            var initialToken = new Token(_factName, fact);
            _betaMemory?.Assert(initialToken);
        }

        /// <summary>
        /// Retracts a fact from the adapter. This method will remove any tokens from the connected BetaMemory that contain the 
        /// specified fact. Since the adapter wraps facts into tokens, it relies on the BetaMemory's Retract method to identify 
        /// and remove any tokens that include the retracted fact. This ensures that when a fact is retracted, all relevant tokens 
        /// in the BetaMemory are updated accordingly, allowing successor nodes to adjust their state based on the change in facts.
        /// </summary>
        /// <param name="fact">The fact object to retract. Cannot be null.</param>
        public void Retract(object fact)
        {
            // BetaMemory.Retract is already designed to find and remove 
            // any Tokens containing this specific fact.
            _betaMemory?.Retract(fact);
        }

        /// <summary>
        /// Refreshes a fact in the adapter. This method will trigger a re-evaluation of any tokens in the connected BetaMemory 
        /// that contain the specified fact, based on the property that has changed. The adapter relies on the BetaMemory's 
        /// Refresh method to identify relevant tokens and propagate the refresh to successor nodes, allowing them to re-evaluate 
        /// their conditions based on the updated information. This is crucial for maintaining the accuracy and responsiveness of 
        /// the Rete network as facts evolve over time.
        /// </summary>
        /// <param name="fact">The fact object whose property is being refreshed. Cannot be null.</param>
        /// <param name="propertyName">The name of the property to refresh. Cannot be null or empty.</param>
        public void Refresh(object fact, string propertyName)
        {
            _betaMemory?.Refresh(fact, propertyName);
        }

        /// <summary>
        /// A visual debugging method that prints the fact being processed and the internal state of the connected BetaMemory. 
        /// This method can be used to trace the flow of facts through the adapter and into the BetaMemory, providing insight 
        /// into how facts are being wrapped into tokens and how they are being processed by successor nodes. The level 
        /// parameter can be used to control the indentation of the output for better readability when visualizing complex 
        /// rule networks.
        /// </summary>
        /// <param name="fact">The fact object to include in the debug output. Can be any object; its string representation will be
        /// printed.</param>
        /// <param name="level">The indentation level to apply to the debug message. Each level increases indentation by spaces.
        /// Defaults to 0.</param>
        public void DebugPrint(object fact, int level = 0)
        {
            string indent = new string(' ', level * 4);
            Console.WriteLine($"{indent}[AlphaToBetaAdapter] Wrapping Fact: {fact}");
            _betaMemory?.DebugPrint(fact, level + 1);
        }
    }

    // Test classes
    class SystemStatus
    {
        public string Name { get; set; }
        public bool IsActive { get; set; }
    };

    class Sensor
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsTriggered { get; set; }
    };

    class CriticalCell : Cell
    {
        string _status = String.Empty;
        public string Status
        {
            get { return _status; }
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        public override bool Equals(object? obj)
        {
            return obj is CriticalCell cell && Id == cell.Id && Value == cell.Value && Status == cell.Status;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value, Status);
        }
    }

    public class Product : Cell
    {
        private string _name;
        private string _category;
        private int _productId;
        private int _price;

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }
        public string Category
        {
            get { return _category; }
            set
            {
                if (_category != value)
                {
                    _category = value;
                    OnPropertyChanged(nameof(Category));
                }
            }
        }

        public int ProductId
        {
            get { return _productId; }
            set
            {
                if (_productId != value)
                {
                    _productId = value;
                    OnPropertyChanged(nameof(ProductId));
                }
            }
        }

        public int Price
        {
            get { return _price; }
            set
            {
                if (_price != value)
                {
                    _price = value;
                    OnPropertyChanged(nameof(Price));
                }
            }
        }

        public override bool Equals(object? obj)
        {
            return obj is Product product && Id == product.Id && ProductId == product.ProductId && Price == product.Price && Value == product.Value && Name == product.Name && Category == product.Category;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value, Name, Category, Price, ProductId);
        }
    }

    public class Inventory : Cell
    {
        private int _quantity;
        private int _productId;
        private string _location;
        public int Quantity
        {
            get { return _quantity; }
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged(nameof(Quantity));
                }
            }
        }
        public int ProductId
        {
            get { return _productId; }
            set
            {
                if (_productId != value)
                {
                    _productId = value;
                    OnPropertyChanged(nameof(ProductId));
                }
            }
        }
        public string WarehouseLocation
        {
            get { return _location; }
            set
            {
                if (_location != value)
                {
                    _location = value;
                    OnPropertyChanged(nameof(WarehouseLocation));
                }
            }
        }

        public override bool Equals(object? obj)
        {
            return obj is Inventory inventory && Id == inventory.Id && Value == inventory.Value && ProductId == inventory.ProductId && Quantity == inventory.Quantity && WarehouseLocation == inventory.WarehouseLocation;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value, Quantity, ProductId, WarehouseLocation);
        }
    }

    public class Shipment : Cell
    {
        private int _productId;
        private string _status;
        public int ProductId
        {
            get { return _productId; }
            set
            {
                if (_productId != value)
                {
                    _productId = value;
                    OnPropertyChanged(nameof(ProductId));
                }
            }
        }
        public string Status
        {
            get { return _status; }
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                }
            }
        }
        public override bool Equals(object? obj)
        {
            return obj is Shipment shipment && Id == shipment.Id && Value == shipment.Value && ProductId == shipment.ProductId && Status == shipment.Status;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Id, Value, ProductId, Status);
        }
    }
}
