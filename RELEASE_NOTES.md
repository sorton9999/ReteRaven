# ReteRaven Release Notes

This file summarizes notable features and improvements.

## v1.4.1 (Oct 30, 2026)

### Update-in-place and fact refresh improvements

- Added update-in-place behavior for facts already in working memory.
- Improved activation handling when facts are updated, including cases where a pending match remains valid.
- Avoided duplicate activations when a match has not changed, while allowing a match to activate again after it leaves and re-enters the network.
- Improved refresh handling across alpha, join, `Not`, and `Exists` nodes.
- Rebuilt indexed `Exists` indexes during refresh to avoid stale keys.
- Added property-change handler tracking, with handlers detached when facts are retracted or the engine is disposed.

## v1.4 (Sept 12, 2026)

### Beta-node sharing

- Added caching and reuse of compatible beta-memory nodes across rules.
- Extended node sharing to rules using `Or`, `Not`, and `AndNot` operators.
- Added tests and documentation for shared-node behavior.

## v1.3.1 (Sept 5, 2026)

### Fact grouping

- Added support for grouping facts and performing `Sum`, `Count`, and `Average` operations.
- Added grouping support to the fluent rule builder.

## v1.3 (May 22, 2026)

### Aggregation and rule-builder improvements

- Added the `.From` and `.All` aggregation clauses.
- Introduced a fluent rule-definition state machine to guide rule authoring and improve editor completion.
- Updated builder methods to use caller argument expressions for cleaner rule definitions.
- Made `Assert`, `Update`, and `FireAll` chainable and added single-fact and batch processing support.

## v1.2 (May 15, 2026)

### Rule and fact management

- Added the ability to remove a rule along with its connected nodes.
- Expanded `Cell` to accept arbitrary values and provide a unique ID.

## v1.1 (May 3, 2026)

### Conditions, priorities, and rule metadata

- Added global `If` conditions and typed `If<T>` conditions.
- Added rule priority support.
- Added `RuleMetadata` and an incrementing rule index.
- Added the `Fact` class in preparation for evolving the fact model.

## Earlier development (Mar 29–Apr 15, 2026)

- Added the `Or` rule-builder operation and activation refraction to prevent repeated firing of the same match.
- Added `Exists`, `Not`, and `AndNot` operations, including an indexed `Exists` implementation.
- Added predicate-based matching.
- Added simulated recursion support and tests.
