```
            _______
        ___/ |    | \___          ____      _       ____
       /   \        /   \        |  _ \ ___| |_ ___|  _ \ __ ___   _____ _ __
      |   RETE   RAVEN   |       | |_) / _ \ __/ _ \ |_) / _` \ \ / / _ \ '_ \
      |      \____/      |       |  _ <  __/ |_  __/  _ < (_| |\ V /  __/ | | |
       \___          ___/        |_| \_\___|\__\___|_| \_\__,_| \_/ \___|_| |_|
           \________/
               ||
          [AI Decision Engine]
```

## *** ReteRaven ***

A high-performance production rule system and inference engine for .NET, built on a custom implementation of the Rete algorithm. Designed for speed and developer productivity. It features a modern fluent rule builder that allows you to define complex logic using a type-safe, readable DSL.

Whether you are building an expert system, a business logic layer, or a reactive data pipeline, this C# rule engine provides the tools to handle sophisticated fact matching and pattern binding with ease.

This project is open-source and distributed under the LGPL license.
------------------------------
## ==> Key Features

* = Efficient Pattern Matching: The Rete network avoids repeatedly evaluating every rule against every fact. Rule evaluation focuses on changes as facts enter, leave, or update in the network.
* = Fluent API: Define complex rules using a readable, LINQ-style syntax.
* = In-Place Fact Updates: Update existing facts and refresh their matching state in the network without treating every update as a new fact.
* = Reactive Fact Refresh: Facts that notify property changes can be refreshed automatically by the engine.
* = Advanced Logic Nodes: Support for `Or`, `Not`, `AndNot`, and `Exists` conditions, including an indexed `Exists` implementation.
* = Beta-Node Sharing: Compatible beta-memory nodes can be cached and reused across rules to reduce duplicated network structures.
* = Grouping and Aggregation: Group matched facts and calculate common aggregates such as `Sum`, `Count`, and `Average`.
* = Activation Refraction: Avoids repeatedly firing the same match without a relevant fact-state change.
* = Rule Prioritization: Control firing order with rule priority and activation salience.
* = Rule Removal: Remove rules along with their connected nodes.
* = Command Queue Ready: Use the engine as an inference layer that produces effects for a separate command-processing layer.

------------------------------
## ==> Quick Start

This API targets .NET 8 and assumes the use of Visual Studio 2022.

## 1. Define Your Facts

Facts can be composed of `Fact` objects. For automatic refresh when properties change, facts should raise `INotifyPropertyChanged` notifications.

~~~csharp
public class Task : Fact
{
    public int ParentId { get; set; }
    public string Status { get; set; }
}
~~~

## 2. Configure the Engine

Use the fluent builder to define your domain logic.

~~~csharp
var engine = new ReteEngine();

engine.Begin("BubbleUpCompletion")
    .Where<Task>("BubbleTask", parent => parent.Status == "Incomplete")
    // Ensure the parent has children.
    .Exists<Task>("BubbleTask", (child, parent) => child.ParentId == parent.Id)
    // Only fire when no children are still incomplete.
    .Not<Task>("BubbleTask", (child, parent) =>
        child.ParentId == parent.Id && child.Status == "Incomplete")
    .Then(match =>
    {
        var parent = match.Get<Task>("BubbleTask");
        parent.Status = "Complete";
        engine.Update(parent);
    });
~~~

You can define multiple rules in the engine, each with its own chain of conditions and action.

## 3. Pulse the Simulation

Integrate the engine into your tick loop.

~~~csharp
public void OnTick()
{
    // 1. Synchronize world changes.
    engine.Assert(changedEntity);

    // 2. Fire pending rules.
    engine.FireAll();

    // 3. Process resulting commands.
    commandQueue.Process();
}
~~~

------------------------------
## ==> Recursive Patterns

ReteRaven supports hierarchical logic patterns such as:

1. Top-Down Breakdown: Rules that split complex tasks into sub-tasks.
2. Bottom-Up Bubble: Rules that use `.Not()` to wait for all children to complete before marking a parent as finished.
3. Recursive Chains: Propagate states through deep trees, such as a chain of command or crafting tree, without manual stack management.

------------------------------
## ==> Rule Prioritization

ReteRaven uses a depth-first conflict-resolution strategy. When multiple rules are satisfied, the engine prioritizes:

1. Rule Priority: Highest value first; controls rule-level prioritization.
2. Salience: Highest value first; controls activation-level prioritization.
3. Recency (LIFO): The most recently matched activation fires first, allowing new facts to take precedence over older logic.

------------------------------
## ==> Installation & Usage

1. Clone the repository.
2. Add the ReteRaven project references to your C# solution.
3. Implement your command queue and start defining rules.

------------------------------
## ==> Future Features

The following capabilities are planned and are not part of the current released functionality.

### Performance & Scaling
* = `Publish-and-Resume Agenda Coordination`: Agenda groups will be able to publish fact changes through the engine and resume dependent groups once their prerequisites are satisfied.
* = `Parallel Agenda Processing`: Independent agenda groups may process concurrently. Shared working-memory and Rete-network mutations will require explicit coordination and are expected to remain serialized initially.
### Developer Tooling
* = `Rule Templates`: Allow users to define a rule "shape" once and instantiate it with different parameters at runtime.
* = `Visualizer`: A tool that exports the Rete Graph to a DOT file or Mermaid diagram so developers can "see" their logic tree.
* = `Serialization`: Save and load RuleMetadata to JSON/XML so rules can be managed in a database instead of just C# code.
### Far Future v2.0
* = `The "Self-Optimizing" Rete`
* = `Temporal Logic (Time-Aware Rules)`
* = `Explanatory AI (The "Why" Feature)`

------------------------------
## ==> License

This project is licensed under the LGPL v2.1 License — feel free to use it in your games, simulations, or personal projects.
------------------------------
