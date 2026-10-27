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
  // C# capitalises booleans when it turns them into text.
  if (typeof value === 'boolean') return value ? 'True' : 'False';
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

const csjsBound = new WeakMap();

/**
 * An instance method used as a delegate: `requestAnimationFrame(Tick)`. JavaScript would call
 * the bare function with no `this`; C# delegates carry their target. The bound function is cached
 * per target and method, so the same method group yields the same function every time — which is
 * what lets `-=` and removeEventListener find it again, as delegate equality does in C#.
 */
export function csjsBind(target, method) {
  let methods = csjsBound.get(target);
  if (!methods) csjsBound.set(target, (methods = new Map()));
  let bound = methods.get(method);
  if (!bound) methods.set(method, (bound = method.bind(target)));
  return bound;
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

const csjsDayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const csjsMonthNames = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August',
  'September', 'October', 'November', 'December'];

function csjsFormatDate(value, spec) {
  switch (spec) {
    case 'd': return csjsFormatDate(value, 'MM/dd/yyyy');
    case 'D': return csjsFormatDate(value, 'dddd, MMMM d, yyyy');
    case 't': return csjsFormatDate(value, 'h:mm tt');
    case 'T': return csjsFormatDate(value, 'h:mm:ss tt');
    case 'g': return csjsFormatDate(value, 'MM/dd/yyyy h:mm tt');
    case 'G': return csjsFormatDate(value, 'MM/dd/yyyy h:mm:ss tt');
    case 's': return csjsFormatDate(value, "yyyy-MM-dd'T'HH:mm:ss");
    case 'o':
    case 'O': return value.toISOString();
  }

  const pad = (n, width) => String(n).padStart(width, '0');
  // Custom specifiers, longest first. Quoted text and backslash escapes are literals.
  return spec.replace(/'([^']*)'|"([^"]*)"|\\(.)|yyyy|yy|MMMM|MMM|MM|M|dddd|ddd|dd|d|HH|H|hh|h|mm|m|ss|s|fff|ff|f|tt/g,
    (token, single, double, escaped) => {
      if (single !== undefined) return single;
      if (double !== undefined) return double;
      if (escaped !== undefined) return escaped;
      const hours12 = value.getHours() % 12 === 0 ? 12 : value.getHours() % 12;
      switch (token) {
        case 'yyyy': return pad(value.getFullYear(), 4);
        case 'yy': return pad(value.getFullYear() % 100, 2);
        case 'MMMM': return csjsMonthNames[value.getMonth()];
        case 'MMM': return csjsMonthNames[value.getMonth()].slice(0, 3);
        case 'MM': return pad(value.getMonth() + 1, 2);
        case 'M': return String(value.getMonth() + 1);
        case 'dddd': return csjsDayNames[value.getDay()];
        case 'ddd': return csjsDayNames[value.getDay()].slice(0, 3);
        case 'dd': return pad(value.getDate(), 2);
        case 'd': return String(value.getDate());
        case 'HH': return pad(value.getHours(), 2);
        case 'H': return String(value.getHours());
        case 'hh': return pad(hours12, 2);
        case 'h': return String(hours12);
        case 'mm': return pad(value.getMinutes(), 2);
        case 'm': return String(value.getMinutes());
        case 'ss': return pad(value.getSeconds(), 2);
        case 's': return String(value.getSeconds());
        case 'fff': return pad(value.getMilliseconds(), 3);
        case 'ff': return pad(value.getMilliseconds(), 3).slice(0, 2);
        case 'f': return pad(value.getMilliseconds(), 3).slice(0, 1);
        case 'tt': return value.getHours() < 12 ? 'AM' : 'PM';
        default: return token;
      }
    });
}

/** Groups the integer part of a non-negative decimal string with commas. */
function csjsGroupDigits(text) {
  const [whole, fraction] = text.split('.');
  const grouped = whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return fraction === undefined ? grouped : `${grouped}.${fraction}`;
}

function csjsFormatNumber(value, spec) {
  const kind = spec[0].toLowerCase();
  const standard = /^[a-zA-Z]\d*$/.test(spec);

  if (standard) {
    const digits = spec.length > 1 ? parseInt(spec.slice(1), 10) : undefined;
    const sign = value < 0 ? '-' : '';
    const magnitude = Math.abs(value);
    switch (kind) {
      case 'f': return sign + csjsRound(magnitude, digits ?? 2, 1).toFixed(digits ?? 2);
      case 'n': return sign + csjsGroupDigits(csjsRound(magnitude, digits ?? 2, 1).toFixed(digits ?? 2));
      case 'c': return sign + '$' + csjsGroupDigits(csjsRound(magnitude, digits ?? 2, 1).toFixed(digits ?? 2));
      case 'p': return sign + csjsGroupDigits(csjsRound(magnitude * 100, digits ?? 2, 1).toFixed(digits ?? 2)) + ' %';
      case 'e': {
        const [mantissa, exponent] = magnitude.toExponential(digits ?? 6).split('e');
        const e = parseInt(exponent, 10);
        const letter = spec[0] === 'E' ? 'E' : 'e';
        return `${sign}${mantissa}${letter}${e < 0 ? '-' : '+'}${String(Math.abs(e)).padStart(3, '0')}`;
      }
      case 'x': {
        const hex = (Math.trunc(value) >>> 0).toString(16).padStart(digits ?? 0, '0');
        return spec[0] === 'X' ? hex.toUpperCase() : hex;
      }
      case 'd': return sign + String(Math.trunc(magnitude)).padStart(digits ?? 0, '0');
      case 'g': return String(value);
      case 'r': return String(value);
    }
  }

  // A custom pattern such as 0.00, #,##0, or 000: the digits after the point fix the precision,
  // zeros before it set the minimum width, and a comma asks for grouping.
  const section = spec.split(';')[value < 0 && spec.includes(';') ? 1 : 0];
  const match = /^([^0#,.]*)([0#,]*)(?:\.([0#]*))?(.*)$/.exec(section);
  if (!match) return String(value);
  const [, prefix, integerPart, fractionPart = '', suffix] = match;
  const percent = suffix.includes('%') || prefix.includes('%');
  const scaled = Math.abs(percent ? value * 100 : value);
  const minFraction = (fractionPart.match(/0/g) ?? []).length;
  const maxFraction = fractionPart.length;
  let text = csjsRound(scaled, maxFraction, 1).toFixed(maxFraction);
  if (maxFraction > minFraction) {
    // Optional (#) fraction digits are dropped when they are trailing zeros.
    const [w, f] = text.split('.');
    let trimmed = f;
    while (trimmed.length > minFraction && trimmed.endsWith('0')) trimmed = trimmed.slice(0, -1);
    text = trimmed.length > 0 ? `${w}.${trimmed}` : w;
  }
  const minWhole = (integerPart.match(/0/g) ?? []).length;
  let [whole, fraction] = text.split('.');
  if (minWhole === 0 && whole === '0' && fraction !== undefined) whole = '';
  whole = whole.padStart(minWhole, '0');
  if (integerPart.includes(',')) whole = csjsGroupDigits(whole);
  const body = fraction === undefined ? whole : `${whole}.${fraction}`;
  const negative = value < 0 && !spec.includes(';') && Number(body.replace(/,/g, '')) !== 0 ? '-' : '';
  return `${negative}${prefix}${body}${suffix}`;
}

/** int.Parse: C# throws on garbage rather than yielding NaN, and rejects trailing junk. */
export function csjsParseInt(text, radix = 10) {
  const trimmed = String(text).trim();
  const valid = radix === 16 ? /^[+-]?(0x)?[0-9a-f]+$/i : radix === 2 ? /^[+-]?[01]+$/ : radix === 8 ? /^[+-]?[0-7]+$/ : /^[+-]?\d+$/;
  if (!valid.test(trimmed)) throw csjsError('FormatException', ['FormatException', 'SystemException', 'Exception'], `The input string '${text}' was not in a correct format.`);
  return parseInt(trimmed, radix);
}

/** double.Parse with the same throw-on-garbage contract. */
export function csjsParseFloat(text) {
  const trimmed = String(text).trim();
  const n = Number(trimmed);
  if (trimmed.length === 0 || Number.isNaN(n)) {
    throw csjsError('FormatException', ['FormatException', 'SystemException', 'Exception'], `The input string '${text}' was not in a correct format.`);
  }
  return n;
}

/** bool.Parse accepts True/False in any case, with surrounding whitespace. */
export function csjsParseBool(text) {
  const t = String(text).trim().toLowerCase();
  if (t === 'true') return true;
  if (t === 'false') return false;
  throw csjsError('FormatException', ['FormatException', 'SystemException', 'Exception'], `String '${text}' was not recognized as a valid Boolean.`);
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

/**
 * Math.Round(x): half rounds to the even neighbour ("banker's rounding"), which is C#'s default.
 * JavaScript's Math.round always rounds half up, so Math.Round(2.5) would be 3 instead of 2.
 */
export function csjsRoundEven(value) {
  const floor = Math.floor(value);
  const diff = value - floor;
  if (diff < 0.5) return floor;
  if (diff > 0.5) return floor + 1;
  return floor % 2 === 0 ? floor : floor + 1;
}

/**
 * Math.Round with a digit count and an optional MidpointRounding mode
 * (ToEven = 0, AwayFromZero = 1, ToZero = 2, ToNegativeInfinity = 3, ToPositiveInfinity = 4).
 */
export function csjsRound(value, digits = 0, mode = 0) {
  const factor = Math.pow(10, digits);
  // .NET scales and rounds the double as it is, so a value the scaling pushes off the midpoint
  // (2.345 * 100 is 234.50000000000003) rounds by where it actually landed. Doing the same keeps
  // the results identical to C#, including the ones that surprise people.
  const scaled = value * factor;
  let rounded;
  switch (mode) {
    case 1: rounded = Math.sign(scaled) * Math.round(Math.abs(scaled)); break;
    case 2: rounded = Math.trunc(scaled); break;
    case 3: rounded = Math.floor(scaled); break;
    case 4: rounded = Math.ceil(scaled); break;
    default: rounded = csjsRoundEven(scaled); break;
  }
  return rounded / factor;
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

/** Dictionary<K,V>.Add throws on a duplicate key; Map.set would silently overwrite. */
export function csjsMapAdd(map, key, value) {
  if (map.has(key)) {
    throw csjsError('ArgumentException', ['ArgumentException', 'SystemException', 'Exception'],
      `An item with the same key has already been added. Key: ${csjsToString(key)}`);
  }
  map.set(key, value);
}

/** Count on an interface-typed collection, which may be an Array, a Map, or a Set. */
export function csjsCount(collection) {
  return collection instanceof Map || collection instanceof Set ? collection.size : collection.length;
}

/** List<T>.RemoveAll: removes every match in place and returns how many went. */
export function csjsRemoveAll(list, predicate) {
  let kept = 0;
  const before = list.length;
  for (let i = 0; i < before; i++) {
    if (!predicate(list[i])) list[kept++] = list[i];
  }
  list.length = kept;
  return before - kept;
}

/** List<T>.Sort(IComparer<T>): the comparer is an object with a Compare method. */
export function csjsSortWith(list, comparer) {
  return list.sort((a, b) => comparer.Compare(a, b));
}

/** Array.BinarySearch / List<T>.BinarySearch: the complement of the insertion point when absent. */
export function csjsBinarySearch(array, value) {
  let lo = 0;
  let hi = array.length - 1;
  while (lo <= hi) {
    const mid = (lo + hi) >>> 1;
    const cmp = csjsCompare(array[mid], value);
    if (cmp === 0) return mid;
    if (cmp < 0) lo = mid + 1;
    else hi = mid - 1;
  }
  return ~lo;
}

/** Array.Copy. */
export function csjsArrayCopy(source, sourceIndex, target, targetIndex, length) {
  for (let i = 0; i < length; i++) target[targetIndex + i] = source[sourceIndex + i];
}

/** Random.Shuffle: Fisher–Yates, in place. */
export function csjsShuffle(array) {
  for (let i = array.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [array[i], array[j]] = [array[j], array[i]];
  }
}

/** Queue<T>.Dequeue / Stack<T>.Pop. Both keep the next item at index 0. */
export function csjsTake(array, emptyMessage) {
  if (array.length === 0) {
    throw csjsError('InvalidOperationException', ['InvalidOperationException', 'SystemException', 'Exception'], emptyMessage);
  }
  return array.shift();
}

/** Queue<T>.Peek / Stack<T>.Peek. */
export function csjsPeek(array, emptyMessage) {
  if (array.length === 0) {
    throw csjsError('InvalidOperationException', ['InvalidOperationException', 'SystemException', 'Exception'], emptyMessage);
  }
  return array[0];
}

// ---- HashSet ------------------------------------------------------------------------------

export function csjsUnionWith(set, other) {
  for (const item of other) set.add(item);
}

export function csjsIntersectWith(set, other) {
  const keep = new Set(other);
  for (const item of [...set]) if (!keep.has(item)) set.delete(item);
}

export function csjsExceptWith(set, other) {
  for (const item of other) set.delete(item);
}

export function csjsIsSubsetOf(set, other) {
  const all = other instanceof Set ? other : new Set(other);
  for (const item of set) if (!all.has(item)) return false;
  return true;
}

export function csjsSetEquals(set, other) {
  const all = new Set(other);
  return all.size === set.size && csjsIsSubsetOf(set, all);
}

export function csjsRemoveWhere(set, predicate) {
  let removed = 0;
  for (const item of [...set]) {
    if (predicate(item)) {
      set.delete(item);
      removed++;
    }
  }
  return removed;
}

// ---- Strings ------------------------------------------------------------------------------

// StringComparison: CurrentCulture = 0, CurrentCultureIgnoreCase = 1, InvariantCulture = 2,
// InvariantCultureIgnoreCase = 3, Ordinal = 4, OrdinalIgnoreCase = 5.
const csjsIgnoresCase = (comparison) => comparison === 1 || comparison === 3 || comparison === 5;
const csjsFold = (text, comparison) => (csjsIgnoresCase(comparison) ? String(text).toLowerCase() : String(text));

export function csjsIndexOf(text, value, comparison) {
  return csjsFold(text, comparison).indexOf(csjsFold(value, comparison));
}

export function csjsIndexOfAny(text, chars) {
  let best = -1;
  for (const c of chars) {
    const i = text.indexOf(c);
    if (i >= 0 && (best < 0 || i < best)) best = i;
  }
  return best;
}

export function csjsStartsWith(text, value, comparison) {
  return csjsFold(text, comparison).startsWith(csjsFold(value, comparison));
}

export function csjsEndsWith(text, value, comparison) {
  return csjsFold(text, comparison).endsWith(csjsFold(value, comparison));
}

export function csjsStringEquals(a, b, comparison) {
  if (a === null || a === undefined || b === null || b === undefined) return a === b;
  return csjsFold(a, comparison) === csjsFold(b, comparison);
}

/** string.Compare: ordinal comparisons compare code units, cultural ones use localeCompare. */
export function csjsStringCompare(a, b, comparison) {
  if (a === b) return 0;
  if (a === null || a === undefined) return -1;
  if (b === null || b === undefined) return 1;
  if (comparison === 4) return csjsCompare(a, b);
  if (comparison === 5) return csjsCompare(a.toLowerCase(), b.toLowerCase());
  const result = a.localeCompare(b, undefined, { sensitivity: csjsIgnoresCase(comparison) ? 'accent' : 'variant' });
  return Math.sign(result);
}

/** Trim / TrimStart / TrimEnd with characters: mode 1 trims the start, 2 the end, 3 both. */
export function csjsTrim(text, chars, mode) {
  const set = new Set(chars.flat().flatMap((c) => [...String(c)]));
  let start = 0;
  let end = text.length;
  if (mode & 1) while (start < end && set.has(text[start])) start++;
  if (mode & 2) while (end > start && set.has(text[end - 1])) end--;
  return text.slice(start, end);
}

export function csjsInsert(text, index, value) {
  return text.slice(0, index) + value + text.slice(index);
}

export function csjsRemoveAt(text, index, count) {
  return text.slice(0, index) + text.slice(index + count);
}

/** string.Join over arbitrary values, converting each the way C# would. */
export function csjsJoin(separator, values) {
  return [...values].map(csjsToString).join(separator);
}

/** char.GetNumericValue: the digit's value, or -1 for a character that is not one. */
export function csjsNumericValue(c) {
  return /^[0-9]$/.test(c) ? c.charCodeAt(0) - 48 : -1;
}

/** Convert.ToBase64String: bytes arrive as an array of numbers. */
export function csjsToBase64(bytes) {
  let binary = '';
  for (const b of bytes) binary += String.fromCharCode(b & 0xff);
  return btoa(binary);
}

export function csjsFromBase64(text) {
  return Array.from(atob(text), (c) => c.charCodeAt(0));
}

// ---- LINQ ---------------------------------------------------------------------------------

const csjsNoElements = () =>
  csjsError('InvalidOperationException', ['InvalidOperationException', 'SystemException', 'Exception'], 'Sequence contains no elements');
const csjsNoMatch = () =>
  csjsError('InvalidOperationException', ['InvalidOperationException', 'SystemException', 'Exception'], 'Sequence contains no matching element');
const csjsMoreThanOne = () =>
  csjsError('InvalidOperationException', ['InvalidOperationException', 'SystemException', 'Exception'], 'Sequence contains more than one element');

export function csjsFirst(source, predicate) {
  for (const item of source) {
    if (!predicate || predicate(item)) return item;
  }
  throw predicate ? csjsNoMatch() : csjsNoElements();
}

export function csjsLast(source, predicate) {
  const array = [...source];
  for (let i = array.length - 1; i >= 0; i--) {
    if (!predicate || predicate(array[i])) return array[i];
  }
  throw predicate ? csjsNoMatch() : csjsNoElements();
}

export function csjsSingle(source, predicate) {
  const matches = [...source].filter((x) => !predicate || predicate(x));
  if (matches.length === 0) throw predicate ? csjsNoMatch() : csjsNoElements();
  if (matches.length > 1) throw csjsMoreThanOne();
  return matches[0];
}

/** SingleOrDefault: null for none, the element for one, and still a throw for several. */
export function csjsSingleOrNull(source, predicate) {
  const matches = [...source].filter((x) => !predicate || predicate(x));
  if (matches.length > 1) throw csjsMoreThanOne();
  return matches.length === 0 ? null : matches[0];
}

export function csjsElementAt(source, index) {
  const array = [...source];
  if (index < 0 || index >= array.length) {
    throw csjsError('ArgumentOutOfRangeException',
      ['ArgumentOutOfRangeException', 'ArgumentException', 'SystemException', 'Exception'],
      'Index was out of range. Must be non-negative and less than the size of the collection.');
  }
  return array[index];
}

export function csjsSum(source, selector) {
  let total = 0;
  for (const item of source) total += selector ? selector(item) : item;
  return total;
}

export function csjsAverage(source, selector) {
  const array = [...source];
  if (array.length === 0) throw csjsNoElements();
  return csjsSum(array, selector) / array.length;
}

function csjsExtreme(source, selector, sign) {
  let best;
  let found = false;
  for (const item of source) {
    const value = selector ? selector(item) : item;
    if (!found || csjsCompare(value, best) * sign > 0) {
      best = value;
      found = true;
    }
  }
  if (!found) throw csjsNoElements();
  return best;
}

export function csjsMin(source, selector) {
  return csjsExtreme(source, selector, -1);
}

export function csjsMax(source, selector) {
  return csjsExtreme(source, selector, 1);
}

function csjsExtremeBy(source, keySelector, sign) {
  let best;
  let bestKey;
  let found = false;
  for (const item of source) {
    const key = keySelector(item);
    if (!found || csjsCompare(key, bestKey) * sign > 0) {
      best = item;
      bestKey = key;
      found = true;
    }
  }
  return found ? best : null;
}

export function csjsMinBy(source, keySelector) {
  return csjsExtremeBy(source, keySelector, -1);
}

export function csjsMaxBy(source, keySelector) {
  return csjsExtremeBy(source, keySelector, 1);
}

function csjsSortByKeys(source, orderings) {
  return [...source]
    .map((item, index) => ({ item, index, keys: orderings.map((o) => o.key(item)) }))
    .sort((a, b) => {
      for (let k = 0; k < orderings.length; k++) {
        const cmp = csjsCompare(a.keys[k], b.keys[k]);
        if (cmp !== 0) return orderings[k].descending ? -cmp : cmp;
      }
      return a.index - b.index;
    })
    .map((entry) => entry.item);
}

/**
 * OrderBy / OrderByDescending. Stable, matching LINQ's documented guarantee. The result remembers
 * its source and keys, so a following ThenBy can refine the ordering rather than replace it.
 */
export function csjsOrderBy(source, keySelector, descending) {
  const orderings = [{ key: keySelector, descending }];
  const sorted = csjsSortByKeys(source, orderings);
  Object.defineProperty(sorted, '$csjsOrder', { value: { source: [...source], orderings }, enumerable: false });
  return sorted;
}

/** ThenBy / ThenByDescending: a secondary key, consulted only where the earlier keys tie. */
export function csjsThenBy(ordered, keySelector, descending) {
  const previous = ordered.$csjsOrder;
  if (!previous) return csjsOrderBy(ordered, keySelector, descending);
  const orderings = [...previous.orderings, { key: keySelector, descending }];
  const sorted = csjsSortByKeys(previous.source, orderings);
  Object.defineProperty(sorted, '$csjsOrder', { value: { source: previous.source, orderings }, enumerable: false });
  return sorted;
}

/**
 * A key usable in a Map with C# equality: primitives as they are, Dates by time, and anything
 * structured (anonymous types, tuples, records) by its contents — which is how C# compares keys
 * built from `new { a, b }` in a join or a GroupBy.
 */
function csjsKey(key) {
  if (key === null || key === undefined || typeof key !== 'object') return key;
  if (key instanceof Date) return `$date:${key.getTime()}`;
  return '$obj:' + JSON.stringify(key, (_, v) => (typeof v === 'bigint' ? `${v}n` : v));
}

/**
 * GroupBy. Each group is a real Array carrying its key as a property, which is exactly how
 * IGrouping<K,T> behaves in C#: it is the sequence *and* it has a Key. Returning {key, items}
 * instead would break iteration, spreading, and every downstream LINQ call.
 */
export function csjsGroupBy(source, keySelector, elementSelector) {
  const groups = new Map();
  for (const item of source) {
    const key = keySelector(item);
    const id = csjsKey(key);
    let bucket = groups.get(id);
    if (!bucket) {
      bucket = [];
      Object.defineProperty(bucket, 'key', { value: key, enumerable: false });
      groups.set(id, bucket);
    }
    bucket.push(elementSelector ? elementSelector(item) : item);
  }
  return [...groups.values()];
}

/** CountBy: KeyValuePair<K, int> is a [key, count] pair, like every other KeyValuePair here. */
export function csjsCountBy(source, keySelector) {
  return csjsGroupBy(source, keySelector).map((g) => [g.key, g.length]);
}

export function csjsToDictionary(source, keySelector, valueSelector) {
  const map = new Map();
  for (const item of source) {
    const key = keySelector(item);
    if (map.has(key)) {
      throw csjsError('ArgumentException', ['ArgumentException', 'SystemException', 'Exception'],
        `An item with the same key has already been added. Key: ${csjsToString(key)}`);
    }
    map.set(key, valueSelector ? valueSelector(item) : item);
  }
  return map;
}

export function csjsRange(start, count) {
  const result = new Array(count);
  for (let i = 0; i < count; i++) result[i] = start + i;
  return result;
}

export function csjsTakeLast(source, count) {
  const array = [...source];
  return count <= 0 ? [] : array.slice(Math.max(0, array.length - count));
}

export function csjsSkipLast(source, count) {
  const array = [...source];
  return array.slice(0, Math.max(0, array.length - Math.max(0, count)));
}

export function csjsTakeWhile(source, predicate) {
  const result = [];
  let index = 0;
  for (const item of source) {
    if (!predicate(item, index++)) break;
    result.push(item);
  }
  return result;
}

export function csjsSkipWhile(source, predicate) {
  const result = [];
  let skipping = true;
  let index = 0;
  for (const item of source) {
    if (skipping && predicate(item, index++)) continue;
    skipping = false;
    result.push(item);
  }
  return result;
}

export function csjsDistinctBy(source, keySelector) {
  const seen = new Set();
  const result = [];
  for (const item of source) {
    const key = csjsKey(keySelector(item));
    if (seen.has(key)) continue;
    seen.add(key);
    result.push(item);
  }
  return result;
}

/** Intersect yields distinct elements of the first sequence that also occur in the second. */
export function csjsIntersect(first, second) {
  const other = new Set(second);
  return [...new Set(first)].filter((x) => other.has(x));
}

/** Except yields distinct elements of the first sequence that do not occur in the second. */
export function csjsExcept(first, second) {
  const other = new Set(second);
  return [...new Set(first)].filter((x) => !other.has(x));
}

/**
 * SelectMany. flatMap would only flatten a selector that returns an Array; a C# selector may
 * return any sequence — a Set, a string, a generator — so each result is spread.
 */
export function csjsSelectMany(source, collectionSelector, resultSelector) {
  const result = [];
  let index = 0;
  for (const item of source) {
    for (const inner of collectionSelector(item, index++)) {
      result.push(resultSelector ? resultSelector(item, inner) : inner);
    }
  }
  return result;
}

/** Join: an equi-join on keys compared with C# equality. Output keeps the outer order. */
export function csjsInnerJoin(outer, inner, outerKey, innerKey, resultSelector) {
  const lookup = new Map();
  for (const item of inner) {
    const key = csjsKey(innerKey(item));
    if (key === null || key === undefined) continue;
    const bucket = lookup.get(key);
    if (bucket) bucket.push(item);
    else lookup.set(key, [item]);
  }
  const result = [];
  for (const item of outer) {
    for (const match of lookup.get(csjsKey(outerKey(item))) ?? []) result.push(resultSelector(item, match));
  }
  return result;
}

/** GroupJoin: every outer element paired with the (possibly empty) array of its matches. */
export function csjsGroupJoin(outer, inner, outerKey, innerKey, resultSelector) {
  const lookup = new Map();
  for (const item of inner) {
    const key = csjsKey(innerKey(item));
    const bucket = lookup.get(key);
    if (bucket) bucket.push(item);
    else lookup.set(key, [item]);
  }
  return [...outer].map((item) => resultSelector(item, lookup.get(csjsKey(outerKey(item))) ?? []));
}

/** Zip: stops at the shorter sequence. Without a selector each pair is a tuple, i.e. an array. */
export function csjsZip(first, second, resultSelector) {
  const a = [...first];
  const b = [...second];
  const length = Math.min(a.length, b.length);
  const result = new Array(length);
  for (let i = 0; i < length; i++) result[i] = resultSelector ? resultSelector(a[i], b[i]) : [a[i], b[i]];
  return result;
}

/** Aggregate without a seed: the first element seeds, and an empty sequence throws. */
export function csjsAggregate(source, func) {
  const array = [...source];
  if (array.length === 0) throw csjsNoElements();
  return array.reduce(func);
}

export function csjsSequenceEqual(first, second) {
  const a = [...first];
  const b = [...second];
  return a.length === b.length && a.every((x, i) => csjsEquals(x, b[i]));
}

/** MemoryExtensions.StartsWith over a span: the first elements equal the given sequence. */
export function csjsSpanStartsWith(span, prefix) {
  const p = [...prefix];
  return p.length <= span.length && p.every((x, i) => csjsEquals(span[i], x));
}

export function csjsSpanEndsWith(span, suffix) {
  const s = [...suffix];
  const offset = span.length - s.length;
  return offset >= 0 && s.every((x, i) => csjsEquals(span[offset + i], x));
}

export function csjsChunk(source, size) {
  const array = [...source];
  const chunks = [];
  for (let i = 0; i < array.length; i += size) chunks.push(array.slice(i, i + size));
  return chunks;
}

export function csjsDefaultIfEmpty(source, fallback) {
  const array = [...source];
  return array.length === 0 ? [fallback] : array;
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

/** AddMonths clamps to the end of a shorter month, as C# does: Jan 31 + 1 month is Feb 28/29. */
export function csjsAddMonths(date, months) {
  const result = new Date(date.getTime());
  const day = result.getDate();
  result.setDate(1);
  result.setMonth(result.getMonth() + months);
  const last = new Date(result.getFullYear(), result.getMonth() + 1, 0).getDate();
  result.setDate(Math.min(day, last));
  return result;
}

export function csjsDateOnly(date) {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

export function csjsDayOfYear(date) {
  return Math.round((csjsDateOnly(date) - new Date(date.getFullYear(), 0, 1)) / 86400000) + 1;
}

export function csjsIsLeapYear(year) {
  return (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
}

export function csjsParseDate(text) {
  const date = new Date(text);
  if (Number.isNaN(date.getTime())) {
    throw csjsError('FormatException', ['FormatException', 'SystemException', 'Exception'],
      `String '${text}' was not recognized as a valid DateTime.`);
  }
  return date;
}

/**
 * TimeSpan.ToString. A TimeSpan is a number of milliseconds; the default format is
 * [-][d.]hh:mm:ss[.fffffff], and the custom specifiers cover d, h, m, s, and f.
 */
export function csjsTimeSpanToString(ms, format) {
  const negative = ms < 0;
  const total = Math.abs(ms);
  const days = Math.floor(total / 86400000);
  const hours = Math.floor(total / 3600000) % 24;
  const minutes = Math.floor(total / 60000) % 60;
  const seconds = Math.floor(total / 1000) % 60;
  const fraction = Math.round((total % 1000) * 10000);
  const pad = (n, w = 2) => String(n).padStart(w, '0');

  if (!format || format === 'c' || format === 'g' || format === 'G') {
    let text = `${days > 0 ? days + '.' : ''}${pad(hours)}:${pad(minutes)}:${pad(seconds)}`;
    if (fraction > 0) text += '.' + pad(fraction, 7);
    return (negative ? '-' : '') + text;
  }

  // Backslashes escape literals in a custom TimeSpan format, e.g. hh\:mm.
  return format.replace(/\\(.)|(d+|h+|m+|s+|f+)/g, (match, literal, token) => {
    if (literal !== undefined) return literal;
    const width = token.length;
    switch (token[0]) {
      case 'd': return pad(days, width);
      case 'h': return pad(hours, width);
      case 'm': return pad(minutes, width);
      case 's': return pad(seconds, width);
      case 'f': return pad(fraction, 7).slice(0, width);
      default: return match;
    }
  });
}

// ---- Regular expressions ------------------------------------------------------------------

// RegexOptions: IgnoreCase = 1, Multiline = 2, Singleline = 16, IgnorePatternWhitespace = 32.
export function csjsRegex(pattern, options = 0) {
  let flags = 'u';
  if (options & 1) flags += 'i';
  if (options & 2) flags += 'm';
  if (options & 16) flags += 's';
  let source = String(pattern);
  // .NET's inline (?i) at the very start is common; JavaScript spells it as a flag.
  if (source.startsWith('(?i)')) {
    source = source.slice(4);
    if (!flags.includes('i')) flags += 'i';
  }
  if (options & 32) source = source.replace(/\\#/g, '\u0000').replace(/#.*$/gm, '').replace(/\s+/g, '').replace(/\u0000/g, '\\#');
  return new RegExp(source, flags);
}

function csjsGlobal(regex) {
  return new RegExp(regex.source, regex.flags.includes('g') ? regex.flags : regex.flags + 'g');
}

/**
 * The .NET group number of each JavaScript capture. JavaScript numbers groups strictly left to
 * right; .NET numbers every unnamed group first and the named ones after them, so in
 * `(?<word>\w+)@(\d+)` the digits are group 1 in C# but group 2 in JavaScript.
 */
function csjsGroupOrder(source) {
  const captures = [];
  let inClass = false;
  for (let i = 0; i < source.length; i++) {
    const c = source[i];
    if (c === '\\') { i++; continue; }
    if (inClass) { if (c === ']') inClass = false; continue; }
    if (c === '[') { inClass = true; continue; }
    if (c !== '(') continue;
    if (source[i + 1] !== '?') { captures.push(null); continue; }
    const named = /^\(\?(?:<([A-Za-z_]\w*)>|'([A-Za-z_]\w*)')/.exec(source.slice(i));
    if (named) captures.push(named[1] ?? named[2]);
  }
  const unnamed = captures.map((name, i) => (name === null ? i + 1 : -1)).filter((i) => i > 0);
  const named = captures.map((name, i) => (name !== null ? i + 1 : -1)).filter((i) => i > 0);
  return [0, ...unnamed, ...named];
}

/** Builds a C#-shaped Match from a RegExp exec result. */
function csjsMatchFrom(regex, input, exec) {
  if (!exec) {
    const empty = { success: false, value: '', index: 0, length: 0, name: '0' };
    empty.groups = Object.assign([empty], {});
    empty.nextMatch = () => empty;
    empty.result = () => '';
    return empty;
  }
  const order = csjsGroupOrder(regex.source);
  const groups = order.map((js, i) => {
    const value = exec[js];
    return {
      success: value !== undefined,
      value: value ?? '',
      index: value !== undefined && exec.indices?.[js] ? exec.indices[js][0] : exec.index,
      length: value?.length ?? 0,
      name: String(i),
    };
  });
  // Named groups are reachable by name as well as by number, like GroupCollection's indexers.
  for (const [name, value] of Object.entries(exec.groups ?? {})) {
    const position = exec.indices?.groups?.[name]?.[0] ?? exec.index;
    groups[name] = { success: value !== undefined, value: value ?? '', index: position, length: value?.length ?? 0, name };
  }
  const match = {
    success: true,
    value: exec[0],
    index: exec.index,
    length: exec[0].length,
    name: '0',
    groups,
    nextMatch() {
      const g = csjsGlobal(regex);
      g.lastIndex = exec.index + Math.max(1, exec[0].length);
      return csjsMatchFrom(regex, input, g.exec(input));
    },
    result(replacement) {
      return csjsExpandReplacement(replacement, exec);
    },
    toString() {
      return exec[0];
    },
  };
  return match;
}

function csjsWithIndices(regex) {
  return regex.flags.includes('d') ? regex : new RegExp(regex.source, regex.flags + 'd');
}

export function csjsMatch(regex, input) {
  const r = csjsWithIndices(regex);
  return csjsMatchFrom(regex, input, r.exec(input));
}

export function csjsMatches(regex, input) {
  const g = csjsGlobal(csjsWithIndices(regex));
  return [...input.matchAll(g)].map((exec) => csjsMatchFrom(regex, input, exec));
}

/** .NET substitutions: $1, ${name}, $0 for the whole match, $$ for a literal dollar. */
function csjsExpandReplacement(replacement, exec) {
  return replacement.replace(/\$(\$|\{(\w+)\}|(\d+)|&)/g, (token, all, name, number) => {
    if (all === '$') return '$';
    if (all === '&') return exec[0];
    if (name !== undefined) return /^\d+$/.test(name) ? exec[Number(name)] ?? '' : exec.groups?.[name] ?? '';
    return exec[Number(number)] ?? '';
  });
}

export function csjsRegexReplace(regex, input, replacement) {
  const g = csjsGlobal(regex);
  if (typeof replacement === 'function') {
    return input.replace(g, (...args) => {
      const hasGroups = typeof args[args.length - 1] === 'object';
      const index = args[args.length - (hasGroups ? 3 : 2)];
      const exec = args.slice(0, args.length - (hasGroups ? 3 : 2));
      exec.index = index;
      exec.groups = hasGroups ? args[args.length - 1] : undefined;
      return csjsToString(replacement(csjsMatchFrom(regex, input, exec)));
    });
  }
  return input.replace(g, (...args) => {
    const hasGroups = typeof args[args.length - 1] === 'object';
    const exec = args.slice(0, args.length - (hasGroups ? 3 : 2));
    exec.groups = hasGroups ? args[args.length - 1] : undefined;
    return csjsExpandReplacement(replacement, exec);
  });
}

export function csjsRegexEscape(text) {
  return String(text).replace(/[.*+?^${}()|[\]\\#\s]/g, (c) => (c === ' ' ? '\\ ' : c === '\t' ? '\\t' : c === '\n' ? '\\n' : '\\' + c));
}

// ---- System.Text.Json ---------------------------------------------------------------------
//
// A type description travels with every call, built by the transpiler from the C# type:
//   { c: Ctor, p: [[name, description, jsonName, readOnly], ...] }
//                                               a class and its public properties; jsonName is
//                                               a [JsonPropertyName] override or null, readOnly
//                                               is 1 for a property Deserialize must not set
//   { a: description }                          an array, list, or other sequence
//   { m: description }                          a dictionary (JSON object <-> Map)
//   { s: description }                          a HashSet
//   { d: 1 }                                    a DateTime
//   { e: 1 }                                    an enum (a number, as C# writes it)
//   null                                        anything that is already JSON-shaped
// The description is what lets Deserialize<T> produce real class instances and real Maps, and
// what lets Serialize write computed properties a plain JSON.stringify would never see.

function csjsJsonName(name, options) {
  const policy = options?.PropertyNamingPolicy;
  if (policy === 'camelCase') return name.length === 0 ? name : name[0].toLowerCase() + name.slice(1);
  if (policy === 'snake_case_lower' || policy === 'kebab-case-lower') {
    const sep = policy === 'snake_case_lower' ? '_' : '-';
    return name.replace(/([a-z0-9])([A-Z])/g, `$1${sep}$2`).replace(/([A-Z])([A-Z][a-z])/g, `$1${sep}$2`).toLowerCase();
  }
  return name;
}

function csjsToJsonValue(value, type, options) {
  if (value === null || value === undefined) return null;
  if (typeof value === 'bigint') return Number(value);
  if (value instanceof Date) return value.toISOString();
  if (value instanceof Map) {
    const result = {};
    for (const [k, v] of value) result[csjsToString(k)] = csjsToJsonValue(v, type?.m, options);
    return result;
  }
  if (value instanceof Set || Array.isArray(value)) {
    return [...value].map((v) => csjsToJsonValue(v, type?.a ?? type?.s, options));
  }
  if (typeof value === 'object') {
    const result = {};
    if (type?.p) {
      for (const [name, propertyType, jsonName] of type.p) {
        result[jsonName ?? csjsJsonName(name, options)] = csjsToJsonValue(value[name], propertyType, options);
      }
    } else {
      for (const [name, v] of Object.entries(value)) result[csjsJsonName(name, options)] = csjsToJsonValue(v, null, options);
    }
    return result;
  }
  return value;
}

export function csjsJsonSerialize(value, type, options) {
  const json = csjsToJsonValue(value, type, options);
  return options?.WriteIndented ? JSON.stringify(json, null, 2) : JSON.stringify(json);
}

function csjsFromJsonValue(json, type, options) {
  if (json === null || json === undefined || !type) return json ?? null;
  // The element description may itself be null ("plain JSON"), so test for the key, not its value.
  if ('d' in type) return new Date(json);
  if ('a' in type) return json.map((v) => csjsFromJsonValue(v, type.a, options));
  if ('s' in type) return new Set(json.map((v) => csjsFromJsonValue(v, type.s, options)));
  if ('m' in type) return new Map(Object.entries(json).map(([k, v]) => [k, csjsFromJsonValue(v, type.m, options)]));
  if (type.c) {
    const instance = Object.create(type.c.prototype);
    // Field defaults first, so a property absent from the JSON keeps its C# default.
    try {
      Object.assign(instance, new type.c());
    } catch {
      /* a constructor that requires arguments: start from an empty instance */
    }
    const keys = Object.keys(json);
    for (const [name, propertyType, jsonName, readOnly] of type.p ?? []) {
      if (readOnly) continue;
      const wanted = jsonName ?? csjsJsonName(name, options);
      const key = options?.PropertyNameCaseInsensitive
        ? keys.find((k) => k.toLowerCase() === wanted.toLowerCase())
        : keys.find((k) => k === wanted);
      if (key !== undefined) instance[name] = csjsFromJsonValue(json[key], propertyType, options);
    }
    return instance;
  }
  return json;
}

export function csjsJsonDeserialize(text, type, options) {
  return csjsFromJsonValue(JSON.parse(text), type, options);
}

// ---- Enums --------------------------------------------------------------------------------

/**
 * Enum.ToString: the member's name; for a [Flags] enum, the names of the set flags in ascending
 * value order joined by ", "; otherwise the number itself — never `undefined`.
 * `names` maps value to name (a declared enum's frozen object carries exactly that reverse map).
 */
export function csjsEnumName(names, value, flags) {
  if (value === null || value === undefined) return '';
  const direct = names[value];
  if (typeof direct === 'string') return direct;
  if (flags && value !== 0) {
    const members = Object.keys(names)
      .filter((k) => /^-?\d+$/.test(k) && typeof names[k] === 'string')
      .map(Number)
      .filter((v) => v !== 0)
      .sort((a, b) => a - b);
    let remaining = value;
    const parts = [];
    for (const v of members) {
      if ((value & v) === v) {
        parts.push(names[v]);
        remaining &= ~v;
      }
    }
    if (remaining === 0 && parts.length > 0) return parts.join(', ');
  }
  return String(value);
}

/** Enum.Parse: a member name (optionally case-insensitive), a number, or a comma-joined set of flags. */
export function csjsEnumParse(members, text, ignoreCase, typeName) {
  const value = csjsEnumTryParse(members, text, ignoreCase);
  if (value === null) {
    throw csjsError('ArgumentException', ['ArgumentException', 'SystemException', 'Exception'],
      `Requested value '${text}' was not found${typeName ? ` in ${typeName}` : ''}.`);
  }
  return value;
}

/** Enum.TryParse: the value, or null when the text names nothing. */
export function csjsEnumTryParse(members, text, ignoreCase) {
  const trimmed = String(text ?? '').trim();
  if (trimmed.length === 0) return null;
  if (/^[+-]?\d+$/.test(trimmed)) return Number(trimmed);
  let result = 0;
  for (const part of trimmed.split(',')) {
    const name = part.trim();
    const key = ignoreCase ? Object.keys(members).find((k) => k.toLowerCase() === name.toLowerCase()) : name;
    if (key === undefined || !Object.prototype.hasOwnProperty.call(members, key)) return null;
    result |= members[key];
  }
  return result;
}

// ---- StringBuilder ------------------------------------------------------------------------

/** A mutable string buffer. Array-of-parts beats string concatenation for large builds. */
export function csjsStringBuilder(initial) {
  let parts = initial === undefined || initial === null ? [] : [String(initial)];
  const flatten = () => {
    const text = parts.join('');
    parts = [text];
    return text;
  };
  return {
    append(value) {
      parts.push(csjsToString(value));
      return this;
    },
    appendLine(value) {
      parts.push(csjsToString(value), '\n');
      return this;
    },
    insert(index, value) {
      const text = flatten();
      parts = [text.slice(0, index) + csjsToString(value) + text.slice(index)];
      return this;
    },
    replace(oldValue, newValue) {
      parts = [flatten().replaceAll(oldValue, newValue)];
      return this;
    },
    remove(index, count) {
      const text = flatten();
      parts = [text.slice(0, index) + text.slice(index + count)];
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

/**
 * `checked` arithmetic and conversions: the result must fit the target type, otherwise C# throws
 * OverflowException. `kind` names the type (int, uint, long, ulong, short, ushort, byte, sbyte).
 */
export function csjsChecked(value, kind) {
  const ranges = {
    sbyte: [-128, 127], byte: [0, 255], short: [-32768, 32767], ushort: [0, 65535],
    int: [-2147483648, 2147483647], uint: [0, 4294967295],
    long: [-9223372036854775808n, 9223372036854775807n], ulong: [0n, 18446744073709551615n],
  };
  const [min, max] = ranges[kind];
  // A fractional value converted under `checked` truncates first, exactly as an explicit cast does.
  const n = typeof value === 'number' ? Math.trunc(value) : value;
  if (typeof n === 'number' && !Number.isFinite(n)) throw csjsOverflow();
  if (n < min || n > max) throw csjsOverflow();
  return n;
}

function csjsOverflow() {
  return csjsError('OverflowException', ['OverflowException', 'ArithmeticException', 'SystemException', 'Exception'],
    'Arithmetic operation resulted in an overflow.');
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
