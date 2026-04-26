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

## 🧠 ReteRaven
A high-performance, fluent Rete algorithm implementation for C# simulations.
ReteRaven is a pattern-matching engine designed to decouple complex logic from your simulation loop. It trades memory for speed by maintaining a stateful graph of partial matches, ensuring that your simulation only "thinks" about the data that actually changes.
------------------------------
## ✨ Key Features

* ⚡ O(1) to O(n) Pattern Matching: Avoid massive foreach loops. Rule evaluation cost is relative to the number of changes, not the size of your world.
* 🔗 Fluent API: Define complex rules using a readable, LINQ-style syntax.
* ♻️ Automatic Retraction: Simplifies memory management. When you Update a fact, the network automatically handles the retraction of the old state and the assertion of the new one.
* 🚫 Advanced Logic Nodes: Full support for Negation (Not) and Existential (Exists) patterns, allowing for complex "Wait for all" and "At least one" logic.
* 📂 Command Queue Ready: Designed to work as an inference layer that outputs executable commands, keeping your simulation state-safe.

------------------------------
## 🚀 Quick Start
## 1. Define Your Facts
Facts are simple POCOs or Records representing your world state.

`public record Task(int Id, int? ParentId, string Status);`

## 2. Configure the Engine
Use the Fluent Builder to define your domain logic.

```csharp
var engine = new ReteEngine();

engine.CreateRule("BubbleUpCompletion")
    .Match<Task>("BubbleTask", parent => parent.Status == "Incomplete")
    // Ensure the parent has children
    .Exists<Task>("BubbleTask", (child, parent) => child.ParentId == parent.Id)
    // Only fire when NO children are still incomplete (Recursive Return)
    .Not<Task>("BubbleTask", (child, parent) => child.ParentId == parent.Id && child.Status == "Incomplete")
    .Then(match => {
        var parent = match.Get<Task>("BubbleTask");
        // Automatic Retraction handles the state swap
        engine.Update(parent with { Status = "Complete" });
    });
```
You can define multiple rules in the engine with different chains of conditions and assign separate actions for each.

## 3. Pulse the Simulation
Integrate the engine into your tick loop.

```csharp
public void OnTick() {
    // 1. Sync world changes
    engine.Assert(changedEntity);
    
    // 2. Fire rules
    engine.FireAll();
    
    // 3. Process resulting commands
    commandQueue.Process();
}
```
------------------------------
## 🔄 Recursive Patterns
ReteRaven excels at hierarchical logic. Because it supports Automatic Retraction and Negation, you can implement:

   1. Top-Down Breakdown: Rules that split "Complex Tasks" into "Sub-Tasks."
   2. Bottom-Up Bubble: Rules that use .Not() to wait for all children to complete before marking a parent as finished.
   3. Recursive Chains: Propagating states through deep trees (like chain-of-command or crafting trees) without manual stack management.

------------------------------
## 🛠 Installation & Usage

   1. Clone the repository.
   2. Add the ReteRaven project references to your C# solution.
   3. Implement your CommandQueue and start coding your rules.

------------------------------
## 📜 License
This project is licensed under the LGPL v2.1 License — feel free to use it in your games, simulations, or personal projects.
------------------------------




