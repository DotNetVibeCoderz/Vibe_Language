// Runs the transpiled library on Node. `npm test` builds first.
import assert from 'node:assert/strict';
import { Money } from './dist/Money.js';
import { Text } from './dist/Text.js';

const total = new Money(10, 'IDR').Add(new Money(5.5, 'IDR'));
assert.equal(total.ToString(), '15.50 IDR');
assert.ok(total.Equals(new Money(15.5, 'IDR')), 'records compare by value');

const shares = new Money(10, 'USD').Split(3).map((m) => m.Amount);
assert.deepEqual(shares, [3.34, 3.33, 3.33]);

assert.equal(Text.Slugify('  Hello, World! '), 'hello-world');
assert.equal(Text.WordCount('one  two\tthree'), 3);
assert.equal(Text.Truncate('transpiled from C#', 10), 'transpile…');

assert.throws(() => new Money(1, 'USD').Add(new Money(1, 'EUR')), /Cannot add EUR to USD/);

console.log('All checks passed.');
