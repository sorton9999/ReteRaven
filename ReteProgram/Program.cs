using ReteCore;
using ReteProgram;


var engine = new ReteEngine();

// Define a rule: "If two cells have same ID but different Values"
engine.RegisterConflictRule<Cell>(
    "ValueMismatch",
    (c1, c2) => c1.Get<Cell>("A").Id == c2.Id && c1.Get<Cell>("A").Value != c2.Value,
    (c1, c2) => Console.WriteLine($"Conflict: {c1.Id} has values {c1.Value} and {c2.Value}"),
    salience: 10
);



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

RuleBuilder<int> ruleBuilder = new RuleBuilder<int>(engine, "MyBuilder");
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

var terminal = new TerminalNode("TripleJoinRule", (t) => {
    var fact1 = t.NamedFacts["A"];
    var fact2 = t.NamedFacts["B"];
    var fact3 = t.NamedFacts["C"];
    Console.WriteLine($"3-way match! [1]:{fact1}; [2]:{fact2}; [3]:{fact3}");
}, new Agenda());
joinABC.AddSuccessor(terminal);

engine.Begin("DetectConflict")
    .Match<Cell>("FirstCell", "CheckMatch")
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


var engine2 = new ReteEngine();

// -- LOGIC: (Status: Night) AND (Sensor: Door Open) --
engine2.Begin("NightIntrusion_Door")
    .Match<SystemStatus>("NightMode") // Check global state "Night"
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
    .Match<SystemStatus>("NightMode")
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


var engine3 = new ReteEngine();
CriticalCell critCell = new() { Id="C", Value = 100, Status = "Not Critical" };

// Rule 1: If Cell value is 100, set Status to "Critical"
engine3.Begin("MarkCritical")
    .Match<CriticalCell>("C")
    .And<CriticalCell>("C", (t, c) => c.Value >= 100 && c.Status != "Critical")
    .Then(t => {
        var c = t.Get<CriticalCell>("C");
        c.Status = "Critical";
        // This update triggers an 'Refresh' which puts Rule 2 on the Agenda
        //engine3.Refresh(c, nameof(CriticalCell.Status));
    });

// Rule 2: If Status is "Critical", sound alarm
engine3.Begin("SoundAlarm")
    .Match<CriticalCell>("C")
    .And<CriticalCell>("C", (t, c) => c.Status == "Critical")
    .Then(t => Console.WriteLine("ALARM SOUNDED!"));
engine3.Assert(critCell);
// This call will now run BOTH rules in sequence
engine3.FireAll();


var engine4 = new ReteEngine();

// Rule 1: When a Cell value is high, mark it "Urgent"
engine4.Begin("MarkUrgent")
    .Match<CriticalCell>("M")
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
    .Match<CriticalCell>("A")
    .And<CriticalCell>("A", (t, c) => c.Status == "Urgent")
    .Then(t => Console.WriteLine("ALERT! Urgent!"));

// ASSERT DATA
engine4.Assert(new CriticalCell { Id = "A", Value = 150, Status = "Normal" });

// RECURSIVE FIRE LOOP
engine4.FireAll();

string cellName = "M";
var engine5 = new ReteEngine();
var criticalCell = new CriticalCell { Id = cellName, Status = "Normal", Value = 590 };
engine5.Begin("MatchStatus")
    .Match<CriticalCell>(cellName, "MatchStatus", (c) => { return c.Status == "Normal"; })
    .Or<CriticalCell>(cellName,
    (t, c) => c.Status != "Urgent",
    (t, c) => c.Value >= 100
    )
    //.And<CriticalCell>(cellName, (t, c) => c.Value > 500)
    .Or<CriticalCell>(cellName, 
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

var engine6 = new ReteEngine();
var criticalCell2 = new CriticalCell { Id = "Not Cell", Status = "Normal", Value = 590 };
engine6.Begin("MatchStatusNot")
    .Match<CriticalCell>("C")
    .Not<CriticalCell>("C", (t, c) => c.Status == "Urgent", "MarkNot")
    .And<CriticalCell>("C", (t, c) => c.Value > 500, "MarkAnd")
    .Then(t => {
        Console.WriteLine($">>RESULT:[{t}]: This should be marked URGENT!");
        });

engine6.Assert(criticalCell2);
engine6.FireAll();


var engine7 = new ReteEngine();
engine7.Begin("MatchStatusExists")
    .Match<CriticalCell>("C")
    .Exists<CriticalCell>("C", (t, c) => c.Status == "Normal", "MarkExists")
    .Or<CriticalCell>("C", "Exists-MarkOr", 
        (t, c) => c.Value > 500,
        (t, c) => c.Value < 300
        )
    .Then(t => {
        Console.WriteLine($">>RESULT:[{t}]: This Exists and should be marked URGENT!");
        });

engine7.Assert(criticalCell2);
engine7.FireAll();

var engine8 = new ReteEngine();
engine8.Begin("Alert_Out_of_Stock_Electronics")
    .Match<Product>("P")
    .And<Product>("P", (t, i) => i.Category == "Electronics", "CheckCategory")
    .Not<Inventory>("P", (t, i) => i.ProductId == t.Get<Product>("P").ProductId, "NoStock")
    .Then(terminal =>
    {
        var p = terminal.Get<Product>("P");
        Console.WriteLine($"ALERT: Order emergency stock!! Product '{p.Name}' is out of stock!", 999);
    });

engine8.Begin("Tag_High_Priority_Items")
    .Match<Product>("P")
    .And<Product>("P", (t, i) => i.ProductId == t.Get<Product>("P").ProductId)
    // OR: Join logic splits here. Firing if either branch is true.
    .Or<Product>("P", "PriceCheck",
        (t, c) => c.Price > 1000,
        (t, c) => c.ProductId == 998899)
    .Then(t => {
        Console.WriteLine($"[ACTION] Tagging {t.Get<Product>("P").Name} for Insurance.", 100);
    });

engine8.Begin("Process_Urgent_Batch")
    .Match<Product>("P")
    // EXISTS: We only care IF there is a pending shipment, not HOW MANY.
    .Exists<Shipment>("P", (t, s) => s.ProductId == t.Get<Product>("P").ProductId && s.Status == "Pending")
    .Then(t => {
        Console.WriteLine($"[ACTION] Adding {t.Get<Product>("P").Name} to the morning truck.", 555);
    });

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
    Name = "Luxury Watch",
    Category = "Accessories",
    Price = 5000
};
Inventory inventory = new Inventory()
{
    ProductId = 12347,
    Quantity = 0
};
Inventory inventory2 = new Inventory()
{
    ProductId = 888899,
    Quantity = 1
};
Shipment shipment = new Shipment()
{
    ProductId = 888899,
    Status = "Pending"
};

engine8.Assert(product);
engine8.Assert(product2);
engine8.Assert(inventory);
engine8.Assert(inventory2);
engine8.Assert(shipment);

engine8.FireAll();

