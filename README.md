```
            _______
        ___/       \___           ____      _       ____
       /   \  RETE  /   \        |  _ \ ___| |_ ___|  _ \ __ ___   _____ _ __
      |     \ RAVEN/     |       | |_) / _ \ __/ _ \ |_) / _` \ \ / / _ \ '_ \
      |      \____/      |       |  _ <  __/ |_  __/  _ < (_| |\ V /  __/ | | |
       \___          ___/        |_| \_\___|\__\___|_| \_\__,_| \_/ \___|_| |_|
           \________/
               ||
          [Inference Engine]
```

ReteRaven -- A rule builder and inference engine API that can create a rules-based, pattern matching engine using the Rete algorithm.

From Wikipedia:
The Rete algorithm provides a generalized logical description of an implementation of functionality responsible for matching data tuples ("facts") against productions ("rules") in a pattern-matching production system.
The word 'Rete' is Latin for 'net' or 'comb'. The same word is used in modern Italian to mean 'network'.

This is a full implementation of the algorithm which provides logical AND, OR, and NOT operators (among others) to rules that are input via the API.  The implementation offers the operations in a rule builder that follows the fluent software pattern.

A typical rule can be put together in the following way:

```csharp
ruleEngine.Begin("CustomerStateRule")
          .Match<Customer> ("CustomerNewEngland")
          .Or<Customer> ("CustomerNewEngland", (token, customer) => customer.State == "Rhode Island",
              (token, customer) => customer.State == "Maine")
          .And<Customer> ("CustomerNewEngland", (token, customer) => customer.Balance > 0)
          .Then ((token) => PrintCustomer(token))
```

Items or facts are input using: `ruleEngine.Assert(CustomerList)`

The rule(s) are fired using: `ruleEngine.FireAll()`

Using this rule engine, it is possible to write multiple rules using the logical operators and run it against input and then provide an action to run against the output.  These actions are performed in the `.Then` method.

Full documentation can be accessed via the URL: https://sorton9999.github.io/ReteRaven

