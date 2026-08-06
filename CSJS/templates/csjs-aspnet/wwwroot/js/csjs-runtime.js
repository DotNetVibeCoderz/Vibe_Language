// CSJS runtime helpers.
//
// Only the behaviours that have no faithful one-liner in JavaScript live here. Everything else
// is mapped inline by the transpiler (List<T> is a real Array, Dictionary<K,V> is a real Map),
// which is what keeps generated output readable.
//
// Every helper is a separate named export so bundlers tree-shake the ones a program never calls.
//
// Gravicode Studios — led by Kang Fadhil.

/** Console.Write: no trailing newline, which console.log cannot express. */
export function csjsWrite(value) {
  if (typeof process !== 'undefined' && process.stdout) process.stdout.write(String(value));
  else console.log(value);
}

/** ToString() honouring a user-defined toString, and matching C#'s "" for null. */
export function csjsToString(value) {
  if (value === null || value === undefined) return '';
  return String(value);
}

/** Value equality for the cases where C# == would compare structurally. */
export function csjsEquals(a, b) {
  if (a === b) return true;
  if (a === null || b === null || a === undefined || b === undefined) return false;
  if (a instanceof Date && b instanceof Date) return a.getTime() === b.getTime();
  if (typeof a.equals === 'function') return a.equals(b);
  return false;
}

/** A stable string hash, standing in for GetHashCode(). */
export function csjsHash(value) {
  const s = csjsToString(value);
  let h = 0;
  for (let i = 0; i < s.length; i++) {
    h = ((h << 5) - h + s.charCodeAt(i)) | 0;
  }
  return h;
}

/** String.Split, dropping empty entries the way the common C# overloads do not. */
export function csjsSplit(text, separator, options) {
  let parts;

  if (Array.isArray(separator)) {
    // An empty separator array means "split on whitespace" in C#.
    if (separator.length === 0) parts = text.split(/\s+/);
    else {
      // One regex keeps a multi-separator split to a single pass.
      const escaped = separator.map((s) => String(s).replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
      parts = text.split(new RegExp(escaped.join('|')));
    }
  } else {
    parts = text.split(separator);
  }

  // StringSplitOptions is a flags enum: RemoveEmptyEntries is 1 and TrimEntries is 2.
  // Ignoring it would drop the caller's intent silently, which is worse than not mapping it.
  if ((options & 2) !== 0) parts = parts.map((p) => p.trim());
  if ((options & 1) !== 0) parts = parts.filter((p) => p.length > 0);

  return parts;
}

/** string.Format with {0}-style placeholders. */
export function csjsFormat(format, ...args) {
  return String(format).replace(/\{(\d+)(?::([^}]*))?\}/g, (match, index, spec) => {
    const value = args[Number(index)];
    if (value === undefined) return match;
    if (spec && typeof value === 'number') return csjsFormatNumber(value, spec);
    return csjsToString(value);
  });
}

/** Applies a C# format specifier to one value. Used by interpolated strings such as `{x:F2}`. */
export function csjsFormatValue(value, spec) {
  if (!spec) return csjsToString(value);
  if (typeof value === 'number') return csjsFormatNumber(value, spec);
  if (value instanceof Date) return csjsFormatDate(value, spec);
  return csjsToString(value);
}

function csjsFormatDate(value, spec) {
  const pad = (n, width = 2) => String(n).padStart(width, '0');
  switch (spec) {
    case 'd': return `${pad(value.getDate())}/${pad(value.getMonth() + 1)}/${value.getFullYear()}`;
    case 'D': return value.toDateString();
    case 't': return `${pad(value.getHours())}:${pad(value.getMinutes())}`;
    case 'T': return value.toTimeString().slice(0, 8);
    case 'o':
    case 'O': return value.toISOString();
    default: return value.toISOString();
  }
}

function csjsFormatNumber(value, spec) {
  const kind = spec[0].toLowerCase();
  const digits = spec.length > 1 ? parseInt(spec.slice(1), 10) : undefined;
  if (kind === 'f') return value.toFixed(digits ?? 2);
  if (kind === 'n') return value.toLocaleString(undefined, { minimumFractionDigits: digits ?? 2 });
  if (kind === 'p') return (value * 100).toFixed(digits ?? 2) + '%';
  if (kind === 'x') return Math.trunc(value).toString(16);
  if (kind === 'd') return String(Math.trunc(value)).padStart(digits ?? 0, '0');
  return String(value);
}

/** int.Parse: C# throws on garbage rather than yielding NaN. */
export function csjsParseInt(text) {
  const n = parseInt(text, 10);
  if (Number.isNaN(n)) throw new Error(`Input string was not in a correct format: '${text}'`);
  return n;
}

/** double.Parse with the same throw-on-garbage contract. */
export function csjsParseFloat(text) {
  const n = parseFloat(text);
  if (Number.isNaN(n)) throw new Error(`Input string was not in a correct format: '${text}'`);
  return n;
}

/** int.TryParse. The transpiler rewrites the out parameter into a destructured result. */
export function csjsTryParseInt(text) {
  const n = parseInt(text, 10);
  return Number.isNaN(n) ? { ok: false, value: 0 } : { ok: true, value: n };
}

/** double.TryParse. */
export function csjsTryParseFloat(text) {
  const n = parseFloat(text);
  return Number.isNaN(n) ? { ok: false, value: 0 } : { ok: true, value: n };
}

/** Math.Round with a digit count, which JS Math.round lacks. */
export function csjsRound(value, digits) {
  const factor = Math.pow(10, digits);
  return Math.round(value * factor) / factor;
}

/** List<T>.Remove: removes the first match and reports whether anything went. */
export function csjsRemove(list, item) {
  const i = list.indexOf(item);
  if (i < 0) return false;
  list.splice(i, 1);
  return true;
}

/** List<T>.Sort() with no comparer: C# sorts numbers numerically, JS sorts lexically. */
export function csjsSort(list) {
  return list.sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
}

/** HashSet<T>.Add returns whether the item was new. */
export function csjsSetAdd(set, item) {
  if (set.has(item)) return false;
  set.add(item);
  return true;
}

/** Dictionary<K,V>.TryAdd. */
export function csjsTryAdd(map, key, value) {
  if (map.has(key)) return false;
  map.set(key, value);
  return true;
}

// ---- LINQ ---------------------------------------------------------------------------------

export function csjsFirst(source, predicate) {
  const array = [...source];
  const found = predicate ? array.find(predicate) : array[0];
  if (found === undefined) throw new Error('Sequence contains no matching element');
  return found;
}

export function csjsLast(source) {
  const array = [...source];
  if (array.length === 0) throw new Error('Sequence contains no elements');
  return array[array.length - 1];
}

export function csjsSingle(source) {
  const array = [...source];
  if (array.length !== 1) throw new Error(`Sequence contains ${array.length} elements, expected exactly 1`);
  return array[0];
}

export function csjsSum(source, selector) {
  let total = 0;
  for (const item of source) total += selector ? selector(item) : item;
  return total;
}

export function csjsAverage(source, selector) {
  const array = [...source];
  if (array.length === 0) throw new Error('Sequence contains no elements');
  return csjsSum(array, selector) / array.length;
}

/** OrderBy / OrderByDescending. Stable, matching LINQ's documented guarantee. */
export function csjsOrderBy(source, keySelector, descending) {
  return [...source]
    .map((item, index) => ({ item, index }))
    .sort((a, b) => {
      const ka = keySelector(a.item);
      const kb = keySelector(b.item);
      const cmp = ka < kb ? -1 : ka > kb ? 1 : 0;
      return cmp !== 0 ? (descending ? -cmp : cmp) : a.index - b.index;
    })
    .map((entry) => entry.item);
}

/**
 * GroupBy. Each group is a real Array carrying its key as a property, which is exactly how
 * IGrouping<K,T> behaves in C#: it is the sequence *and* it has a Key. Returning {key, items}
 * instead would break iteration, spreading, and every downstream LINQ call.
 */
export function csjsGroupBy(source, keySelector) {
  const groups = new Map();
  for (const item of source) {
    const key = keySelector(item);
    const bucket = groups.get(key);
    if (bucket) bucket.push(item);
    else groups.set(key, [item]);
  }

  return [...groups.entries()].map(([key, items]) => {
    Object.defineProperty(items, 'key', { value: key, enumerable: false });
    return items;
  });
}

export function csjsToDictionary(source, keySelector, valueSelector) {
  const map = new Map();
  for (const item of source) map.set(keySelector(item), valueSelector(item));
  return map;
}

export function csjsRange(start, count) {
  const result = new Array(count);
  for (let i = 0; i < count; i++) result[i] = start + i;
  return result;
}

// ---- Async / time -------------------------------------------------------------------------

export function csjsDelay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

export function csjsToday() {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

export function csjsAddMs(date, ms) {
  return new Date(date.getTime() + ms);
}

// ---- StringBuilder ------------------------------------------------------------------------

/** A mutable string buffer. Array-of-parts beats string concatenation for large builds. */
export function csjsStringBuilder() {
  const parts = [];
  return {
    append(value) {
      parts.push(csjsToString(value));
      return this;
    },
    appendLine(value) {
      parts.push(csjsToString(value), '\n');
      return this;
    },
    clear() {
      parts.length = 0;
      return this;
    },
    length() {
      return parts.reduce((n, p) => n + p.length, 0);
    },
    toString() {
      return parts.join('');
    },
  };
}

// ---- Exceptions ---------------------------------------------------------------------------
//
// Every BCL exception collapses onto Error in JavaScript, which would make `catch (FormatException)`
// indistinguishable from `catch (Exception)`. Carrying the C# type hierarchy on the error itself is
// what keeps typed catch clauses meaning what they mean in C#.

/** Builds an Error tagged with the C# exception hierarchy it stands for. */
export function csjsError(name, hierarchy, message) {
  const error = new Error(message === undefined || message === null ? name : message);
  error.name = name;
  error.$csjs = hierarchy;
  return error;
}

/** True when a caught value matches a C# catch clause for `typeName`. */
export function csjsCatches(error, typeName) {
  // `catch (Exception)` is the catch-all, including errors JavaScript itself threw.
  if (typeName === 'Exception') return true;
  if (error && Array.isArray(error.$csjs)) return error.$csjs.includes(typeName);

  // Errors originating in JavaScript map onto their closest C# counterpart.
  if (error instanceof TypeError) return typeName === 'NullReferenceException' || typeName === 'InvalidCastException';
  if (error instanceof RangeError) return typeName === 'IndexOutOfRangeException' || typeName === 'ArgumentOutOfRangeException';
  return false;
}

// ---- Semantics that differ between C# and JavaScript --------------------------------------

/** Integer division: C# truncates toward zero, JavaScript does not divide integers at all. */
export function csjsIntDiv(a, b) {
  if (b === 0) throw new Error('Attempted to divide by zero.');
  return Math.trunc(a / b);
}

/** Checked integer overflow, wrapping to 32 bits the way C# unchecked arithmetic does. */
export function csjsInt32(value) {
  return value | 0;
}

/** C# `as` / safe cast: yields null instead of throwing. */
export function csjsAs(value, ctor) {
  return value instanceof ctor ? value : null;
}

/** C# `is` with a type pattern. */
export function csjsIs(value, ctor) {
  return value instanceof ctor;
}

/** A hard cast that still validates, mirroring InvalidCastException. */
export function csjsCast(value, ctor) {
  if (value === null || value === undefined) return value;
  if (!(value instanceof ctor)) throw new Error(`Unable to cast object to ${ctor.name}`);
  return value;
}

/** IComparable.CompareTo: negative, zero, or positive, matching C#'s contract. */
export function csjsCompare(a, b) {
  if (a === b) return 0;
  if (a === null || a === undefined) return -1;
  if (b === null || b === undefined) return 1;
  return a < b ? -1 : a > b ? 1 : 0;
}
