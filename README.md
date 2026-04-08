RuleEngine -- An API that can create a rules-based, pattern matching engine using the Rete algorithm.

From Wikipedia:
The Rete algorithm provides a generalized logical description of an implementation of functionality responsible for matching data tuples ("facts") against productions ("rules") in a pattern-matching production system.
The word 'Rete' is Latin for 'net' or 'comb'. The same word is used in modern Italian to mean 'network'.

This is a full implementation of the algorithm which provides logical AND and OR operators to rules that are input via the API.  The implementation offers the operations up in a rule builder that follows the fluent software pattern.

A typical rule can be put together in the following way:

ruleEngine.Begin("CustomerStateRule")
          .Match<Customer> ("CustomerNewEngland")
          .Or<Customer> ((token, customer) => customer.State == "Rhode Island",
              (token, customer) => customer.State == "Maine")
          .And<Customer> ((token, customer) => customer.Balance > 0)
          .Then ((token) => PrintCustomer(token))

Using this rule engine, it is possible to write multiple rules using the logical operators and run it against input and then provide an action to run against the output.  These actions are performed in the .Then method.

Full documentation can be accessed via the URL: https://sorton9999.github.io/RuleEngine

