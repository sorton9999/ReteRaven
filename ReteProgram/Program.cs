//-----------------------------------------------------------------------
// <copyright file="Program.cs">
//     Copyright (c) Steven Orton. All rights reserved.
//     Licensed under the GNU Lesser General Public License v2.1.
//     See LICENSE file in the ReteRaven project root for full license
//     information.
// </copyright>
//-----------------------------------------------------------------------
using ReteCore;
using ReteEngine;
using ReteProgram;


var engine = new ReteEngine.ReteEngine();

var cell1 = new Cell { Id = "A", Value = 100 };
var cell2 = new Cell { Id = "A", Value = 200 };
cell1.PropertyChanged += Cell_PropertyChanged;
cell2.PropertyChanged += Cell_PropertyChanged;

engine.Assert(cell1);
engine.Assert(cell2); // Activates the rule
engine.Retract(cell2); // Retracts the rule before it fires
engine.FireAll();      // Nothing prints because of the retraction

cell1.Value = 300; // Update cell1's value
cell2.Value = 500; // Update cell2's value to match cell1
var cell3 = new Cell { Id = "A", Value = 1000 };

cell3.PropertyChanged += Cell_PropertyChanged;

void Cell_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
{
    Cell cell = sender as Cell;
    if (cell != null)
    {
        Console.WriteLine($"Cell \'{cell.Id}\' new Value:[{cell.Value}].");
    }
}

BetaMemory initialBetaMemory = new BetaMemory();
initialBetaMemory.AddSuccessor(new JoinNode(initialBetaMemory, new AlphaMemory(), "B", (t, f) => true)); // Dummy join to start the chain
AlphaMemory alphaMemoryA = new AlphaMemory();
alphaMemoryA.Facts.Add(cell1); // Assert cell1 into Alpha Memory A
alphaMemoryA.AddSuccessor(new AlphaToBetaAdapter(initialBetaMemory, "A")); // Connect A to the initial Beta Memory
AlphaMemory alphaMemoryB = new AlphaMemory();
alphaMemoryB.Facts.Add(cell2);
AlphaMemory alphaMemoryC = new AlphaMemory();
alphaMemoryA.Facts.Add(cell3);

ReteBuilder<int> ruleBuilder = new ReteBuilder<int>(engine, "MyBuilder");
ruleBuilder.StartWith(alphaMemoryA, "A")
    .JoinWith<int>(alphaMemoryB, (t, b) => (int)t.Get<int>("A") < b)
    .JoinWith<int>(alphaMemoryC, (t, c) => (int)t.Get<int>("B") < c)
    .Then(new Agenda(), (t) =>
    {
        var fact1 = t.Get<int>("A");
        var fact2 = t.Get<int>("B");
        var fact3 = t.Get<int>("C");
        Console.WriteLine($"3-way match! [1]:{fact1}; [2]:{fact2}; [3]:{fact3}");
    });

engine.FireAll();

// 1. Join Cell A and Cell B
var joinAB = new JoinNode(initialBetaMemory, alphaMemoryB, "B", (t, f) =>
{
    var cell1 = t.Get<Cell>("A");
    var cell2 = (Cell)f;
    return cell1.Id == cell2.Id;
});
var betaMemoryAB = new BetaMemory();
betaMemoryAB.AddSuccessor(joinAB);

// 2. Join (A+B) and Cell C
var joinABC = new JoinNode(betaMemoryAB, alphaMemoryC, "C", (t, f) => {
    var cellA = t.Get<Cell>("A");
    var cellB = t.Get<Cell>("B");
    var cellC = (Cell)f;
    return cellA.Id == cellB.Id && cellB.Id == cellC.Id; // Match if all three have the same ID
});
var metaData = new RuleMetadata()
{ 
    Name = "TripleJoinRule",
    Salience = 10,
    Agenda = new Agenda(),
    Action = (t) =>
    {
        var fact1 = t.Get<Cell>("A");
        var fact2 = t.Get<Cell>("B");
        var fact3 = t.Get<Cell>("C");
        Console.WriteLine($"3-way match! [1]:{fact1}; [2]:{fact2}; [3]:{fact3}");
    }};
var terminal = new TerminalNode(metaData);
joinABC.AddSuccessor(terminal);

engine.Begin("DetectConflict")
    .Where<Cell>("FirstCell", "CheckMatch")
    .And<Cell>("SecondCell", (token, next) =>
        token.Get<Cell>("FirstCell").Id == next.Id &&
        token.Get<Cell>("FirstCell").Value != next.Value,
        "CheckAdd")
    .Then(token =>
    {
        var a = token.Get<Cell>("FirstCell");
        var b = token.Get<Cell>("SecondCell");
        Console.WriteLine($"Conflict found on {a.Id}!");
    }, salience: 10);

Cell cell100 = new Cell() { Id = "FirstCell", Value = 100 };
Cell cell500 = new Cell() { Id = "SecondCell", Value = 500 };

engine.Assert(cell100);
engine.Assert(cell500);

engine.FireAll();


var engine2 = new ReteEngine.ReteEngine();

// -- LOGIC: (Status: Night) AND (Sensor: Door Open) --
engine2.Begin("NightIntrusion_Door")
    .Where<SystemStatus>("NightMode") // Check global state "Night"
    .And<Sensor>("Door", (token, s) =>
        token.Get<SystemStatus>("NightMode").IsActive &&
        s.Type == "Door" && s.IsTriggered)
    .Then(token => {
        var door = token.Get<Sensor>("Door");
        Console.WriteLine($"[ALARM] Intrusion detected! {door.Name} was opened at night.");
    }, salience: 100);

// -- LOGIC: (Status: Night) AND (Sensor: Motion Detected) --
// This creates the "OR" effect by defining a second path to the same outcome
engine2.Begin("NightIntrusion_Motion")
    .Where<SystemStatus>("NightMode")
    .And<Sensor>("Motion", (token, s) =>
        token.Get<SystemStatus>("NightMode").IsActive &&
        s.Type == "Motion" && s.IsTriggered)
    .Then(token => {
        var motion = token.Get<Sensor>("Motion");
        Console.WriteLine($"[ALARM] Movement detected! {motion.Name} triggered at night.");
    }, salience: 100);

// 1. Set the system to Night Mode
var status = new SystemStatus { Name = "NightMode", IsActive = true };
engine2.Assert(status);

// 2. Simulate a Sensor trigger
var frontDoor = new Sensor { Name = "Front Door", Type = "Door", IsTriggered = false };
engine2.Assert(frontDoor);

var frontDoorM = new Sensor { Name = "Front Door", Type = "Motion", IsTriggered = true };
engine2.Assert(frontDoorM);

// 3. Fire the Engine
// This will satisfy the "NightIntrusion_Door" rule.
engine2.FireAll();


var engine3 = new ReteEngine.ReteEngine();
CriticalCell critCell = new() { Id="C", Value = 100, Status = "Not Critical" };

// Rule 1: If Cell value is 100, set Status to "Critical"
engine3.Begin("MarkCritical")
    .Where<CriticalCell>("C")
    .And<CriticalCell>("C", (t, c) => c.Value >= 100 && c.Status != "Critical")
    .Then(t => {
        var c = t.Get<CriticalCell>("C");
        c.Status = "Critical";
        // This update triggers an 'Refresh' which puts Rule 2 on the Agenda
        //engine3.Refresh(c, nameof(CriticalCell.Status));
    });

// Rule 2: If Status is "Critical", sound alarm
engine3.Begin("SoundAlarm")
    .Where<CriticalCell>("C")
    .And<CriticalCell>("C", (t, c) => c.Status == "Critical")
    .Then(t => Console.WriteLine("ALARM SOUNDED!"));
engine3.Assert(critCell);
// This call will now run BOTH rules in sequence
engine3.FireAll();


var engine4 = new ReteEngine.ReteEngine();

// Rule 1: When a Cell value is high, mark it "Urgent"
engine4.Begin("MarkUrgent")
    .Where<CriticalCell>("M")
    .And<CriticalCell>("M", (t, c) => c.Value > 100 && c.Status != "Urgent")
    .Then(t => {
        var c = t.Get<CriticalCell>("M");
        c.Status = "Urgent";
        Console.WriteLine("Rule 1: Marked Cell Urgent.");
        // This Refresh triggers Rule 2 in the NEXT iteration of the while loop
        //engine4.Refresh(c, nameof(CriticalCell.Status));
    });

// Rule 2: When a Cell is "Urgent", log an alert
engine4.Begin("AlertUrgent")
    .Where<CriticalCell>("A")
    .And<CriticalCell>("A", (t, c) => c.Status == "Urgent")
    .Then(t => Console.WriteLine("ALERT! Urgent!"));

// ASSERT DATA
engine4.Assert(new CriticalCell { Id = "A", Value = 150, Status = "Normal" });

// RECURSIVE FIRE LOOP
engine4.FireAll();

string cellName = "M";
var engine5 = new ReteEngine.ReteEngine();
var criticalCell = new CriticalCell { Id = cellName, Status = "Normal", Value = 590 };
engine5.Begin("MatchStatus")
    .Where<CriticalCell>(cellName, "MatchStatus", (c) => { return c.Status == "Normal"; })
    //.And<CriticalCell>(cellName, (t, c) => c.Value > 500)
    .Or<CriticalCell>(cellName, "MatchOr",
    (t, c) => c.Value > 500,
    (t, c) => c.Value < 200)
    .Then(t => {
        Token current = t;
        var f = current.Get<CriticalCell>(cellName);
        if (f.Value > 500)
            f.Status = "Critical";
        else
            f.Status = "Normal";
        Console.WriteLine($"[{t.Fact}]: The Status is [{f.Status}]");
        });

// The original cell should not print the alert because it's normal
// but the value is too high
engine5.Assert(criticalCell);

engine5.FireAll();

// This value change should trigger the rule to mark it Normal and print the alert
criticalCell.Value = 120;

// Fire the rule again to see the affect of the value change
engine5.FireAll();

var engine6 = new ReteEngine.ReteEngine();
var criticalCell2 = new CriticalCell { Id = "Not Cell", Status = "Normal", Value = 590 };
engine6.Begin("MatchStatusNot")
    .Where<CriticalCell>("C")
    .Not<CriticalCell>("C", (t, c) => c.Status == "Urgent", "MarkNot")
    .And<CriticalCell>("C", (t, c) => c.Value > 500, "MarkAnd")
    .Then(t => {
        Console.WriteLine($">>RESULT:[{t}]: This should be marked URGENT!");
        });

engine6.Assert(criticalCell2);
engine6.FireAll();


var engine7 = new ReteEngine.ReteEngine();
engine7.Begin("MatchStatusExists")
    .Where<CriticalCell>("C")
    .Exists<CriticalCell>("C", (t, c) => c.Status == "Normal", "MarkExists")
    //.Or<CriticalCell>("C", "Exists-MarkOr", 
    //    (t, c) => c.Value > 500,
    //    (t, c) => c.Value < 300
    //    )
    .Then(t => {
        Console.WriteLine($">>RESULT:[{t}]: This Exists and should be marked URGENT!");
        });

engine7.Assert(criticalCell2);
engine7.FireAll();

var engine8 = new ReteEngine.ReteEngine();

engine8.Begin("Alert_Out_of_Stock_Electronics")
    .Where<Product>("P", "MatchElec", (c) => c.Category == "Electronics")
    .Not<Inventory>("P", (t, i) => i.ProductId == t.Get<Product>("P").ProductId, "NoStock")
    .Then(terminal =>
    {
        var p = terminal.Get<Product>("P");
        Console.WriteLine($"ALERT: Order emergency stock!! Product '{p.Name}' is out of stock!");
    }, 999);

engine8.Begin("Tag_High_Priority_Items")
    .Where<Product>("R")
    .And<Product>("R", (t, i) => i.ProductId == t.Get<Product>("R").ProductId)
    // OR: Join logic splits here. Firing if either branch is true.
    .Or<Product>("R", "PriceCheck",
        (t, c) => c.Price > 1000,
        (t, c) => c.ProductId == 998899)
    .Then(t => {
        Console.WriteLine($"[ACTION] Tagging {t.Get<Product>("R").Name} for Insurance.");
    }, 100);

engine8.Begin("Process_Urgent_Batch")
    .Where<Inventory>("I", null, (i) => i.WarehouseLocation == "Aisle 3")
    .Where<Product>("Q", null, (p) => p.Name != String.Empty)
    //.Where<TestEval>("Eval", "MatchEval")
     //EXISTS: We only care IF there is a pending shipment, not HOW MANY.
    .Exists<Shipment>("S", (t, s) => s.ProductId == t.Get<Product>("Q").ProductId && s.Status == "Pending")
    //.And<TestEval>("Eval", (t, e) => !e.IsEvaluated) // Prevents infinite loop by only allowing this to run once per Eval fact
    .Then(t => {
        //TestEval eval = t.Get<TestEval>("Eval");
        //eval.IsEvaluated = true;
        Console.WriteLine($"[ACTION] Adding {t.Get<Product>("Q").Name} to the morning truck.");
    }, 555);

Product product = new Product()
{
    ProductId = 12345,
    Name = "4K TV",
    Category = "Electronics",
    Price = 1500
};
Product product2 = new Product()
{
    ProductId = 888899,
    Name = "Luxury Yacht",
    Category = "Accessories",
    Price = 15000000
};
Product product3 = new Product()
{
    ProductId = 12347,
    Name = "Frisbee",
    Category = "Sports",
    Price = 25
};
Inventory inventory = new Inventory()
{
    ProductId = 12347,
    Quantity = 1,
    WarehouseLocation = "Aisle 3"
};
Inventory inventory2 = new Inventory()
{
    ProductId = 888899,
    Quantity = 1,
    WarehouseLocation = "Aisle 5"
};
Inventory inventory3 = new Inventory()
{
    ProductId = 12349,
    Quantity = 0,
    WarehouseLocation = "Aisle 1"
};
Shipment shipment = new Shipment()
{
    ProductId = 12347,
    Status = "No Status"
};
Shipment shipment2 = new Shipment()
{
    ProductId = 888899,
    Status = "Pending"
};

engine8.Assert(product);
engine8.Assert(product2);
engine8.Assert(product3);
engine8.Assert(inventory);
engine8.Assert(inventory2);
engine8.Assert(inventory3);
engine8.Assert(shipment);
engine8.Assert(shipment2);

engine8.FireAll();
Console.WriteLine("Changing shipment status to Pending...");
shipment.Status = "Pending";
engine8.FireAll();

// Test case for recursive rule firing and propagation through multiple levels
// of related facts.
Console.WriteLine("\n--- Testing Recursive Rule ---");
var engine9 = new ReteEngine.ReteEngine();
engine9.Begin("TestOrderPropagation")
    .Next(ReteBuilder<Order>.FIRST_RULE_PRIORITY_VALUE)
    //.Trace($"OrderPropagation")
    .Where<Order>("O", null, o => !o.IsProcessed)
    .And<Officer>("F", (t, off) => off.Rank == t.Get<Order>("O").TargetRank)
    .Then(t => {
        var order = t.Get<Order>("O");
        var officer = t.Get<Officer>("F");
        Console.WriteLine($"[ACTION] {officer.Name} is handling the order.");

        if (officer.Rank == order.TargetRank)
        {
            // This is our base case: the order has reached the correct rank and is executed.
            Console.WriteLine($"[ACTION]----> {officer.Name} (Rank: {officer.Rank}) is executing the order [{order.Text}]");
        }
        else if (!string.IsNullOrEmpty(officer.ReportsToRank))
        {
            var subOrder = new Order() { 
                TargetRank = officer.Underling,
                Text = order.Text,
                IsProcessed = false
            };
            // This assertion will trigger the same rule for the underling officer,
            // creating a recursive effect.
            engine9.Assert(subOrder);
        }
        // Mark the original order as processed
        order.IsProcessed = true;
    });

var order1 = new Order { Id = "1", Text = "Charge!", TargetRank = "Lieutenant", GivenBy = "General", IsProcessed = false };
var officer1 = new Officer { Id = "1", Name = "Smith", Rank = "Lieutenant", Underling = "" };
var officer2 = new Officer { Id = "2", Name = "Johnson", Rank = "Captain", Underling = "Lieutenant" };
var officer3 = new Officer { Id = "3", Name = "Brown", Rank = "Major", Underling = "Captain" };
var officer4 = new Officer { Id = "4", Name = "Davis", Rank = "Colonel", Underling = "Major" };
var officer5 = new Officer { Id = "5", Name = "Williams", Rank = "General", Underling = "Colonel" };
var officer6 = new Officer { Id = "6", Name = "Anderson", Rank = "Captain", Underling = "Lieutenant" };
engine9.Assert(officer6);
engine9.Assert(officer5);
engine9.Assert(officer4);
engine9.Assert(officer3);
engine9.Assert(officer2);
engine9.Assert(officer1);
engine9.Assert(order1);

engine9.FireAll();

Console.WriteLine("\n--- Testing another order, but there are 2 of that rank ---");
var order2 = new Order { Id = "2", Text = "Retreat!", TargetRank = "Captain", GivenBy = "General", IsProcessed = false };
engine9.Assert(order2);
engine9.FireAll();

// Adding new rules to check if the officers are on duty before executing orders.
// If not on duty, the order is not handled by that officer.
Console.WriteLine("\n--- Testing Duty Status ---");
engine9.Begin("ExecuteOnDuty")
    .First()
    //.Trace($"OnDutyTrace")
    .Where<Order>("OnDO", null, o => !o.IsProcessed)
    .And<Officer>("DF", (t, off) => off.Rank == t.Get<Order>("OnDO").TargetRank)
    .And<DutyStatus>("D", (t, d) => d.Name == t.Get<Officer>("DF").Name && d.OnDuty)
    .Then(t =>
    {
        Console.WriteLine($"[ACTION] {t.Get<Officer>("DF").Name} is on duty and executing the order [{t.Get<Order>("OnDO").Text}].");
        t.Get<Order>("OnDO").IsProcessed = true;
    });
// Need a rule to handle the off duty case.
engine9.Begin("HandleOffDuty")
    .Next()
    //.Trace($"OffDutyTrace")
    .Where<Order>("OffDO", null, o => !o.IsProcessed)
    .And<Officer>("DF", (t, off) => off.Rank == t.Get<Order>("OffDO").TargetRank)
    .And<DutyStatus>("DS", (t, d) => d.Name == t.Get<Officer>("DF").Name && !d.OnDuty)
    .Then(t =>
    {
        Console.WriteLine($"[ACTION] Officer {t.Get<Officer>("DF").Name} is off duty and not handling the order.");
    });

var duty1 = new DutyStatus { Id = "1", Name = "Smith", OnDuty = true };
var duty2 = new DutyStatus { Id = "2", Name = "Johnson", OnDuty = false };
var duty3 = new DutyStatus { Id = "3", Name = "Brown", OnDuty = true };
var duty4 = new DutyStatus { Id = "4", Name = "Davis", OnDuty = true };
var duty5 = new DutyStatus { Id = "5", Name = "Williams", OnDuty = true };
var duty6 = new DutyStatus { Id = "6", Name = "Anderson", OnDuty = true };
engine9.Assert(duty1);
engine9.Assert(duty2);
engine9.Assert(duty3);
engine9.Assert(duty4);
engine9.Assert(duty5);
engine9.Assert(duty6);

// Update the order to trigger the new rules.
// This will cause the "HandleOffDuty" rule to fire for Johnson, who is off duty.
order2.IsProcessed = false;
engine9.Update(order2);
engine9.FireAll();

// Let's create a scenario where there is only one officer on duty of duplicate ranks,
// and see how the rules handle that.  We will set the officer on duty in a duty status
// update and fire again.
Console.WriteLine("\n--- Testing Lieutenant Off Duty ---");
var order3 = new Order { Id = "3", Text = "Shuffle Papers", TargetRank = "Lieutenant", GivenBy = "Colonel", IsProcessed = false };
engine9.Assert(order3);
duty1.OnDuty = false;
engine9.Update(duty1);
engine9.FireAll();
Console.WriteLine("\n--- Testing Lieutenant Back On Duty ---");
duty1.OnDuty = true;
order3.IsProcessed = false;
engine9.Update(duty1);
engine9.Update(order3);
engine9.FireAll();

var engineA = new ReteEngine.ReteEngine();
int fireCount = 0;
var order = new Order { Text = "Test Order", TargetRank = "Captain", IsProcessed = false };

// High Priority - Retracts the order
engineA.Begin("HighPriority_Retract")
    .Priority(100)
    .Where<Order>("O", "HighPri", o => !o.IsProcessed)
    .Then(t => {
        fireCount++;
        var fact = t.Get<Order>("O");
        // This should invalidate the next rule
        engineA.Retract(fact);
    });

// Low Priority - Should be cancelled by TM
engineA.Begin("LowPriority_ShouldNotFire")
    .Priority(50)
    .Where<Order>("O", "LowPri", o => !o.IsProcessed)
    .Then(t => {
        // If this runs, TM failed
        fireCount++;
    });

// Act
engineA.Assert(order);
engineA.FireAll();

// If TM is working, fireCount should be 1.
Console.WriteLine($"Fire count: {fireCount}");


