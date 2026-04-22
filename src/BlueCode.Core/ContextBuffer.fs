module BlueCode.Core.ContextBuffer

open BlueCode.Core.Domain

/// Immutable bounded buffer of recent Steps. The agent loop (Phase 4)
/// keeps at most `capacity` most-recent steps as rolling memory (LOOP-06
/// default 3). Nothing is mutated in-place; `add` returns a new buffer.
///
/// This is not a full ring buffer (no wrap-around arithmetic) — it is an
/// append-and-truncate list, which is the simplest shape that satisfies
/// the bounded-retention contract and interoperates with F# list idioms.
type ContextBuffer = private {
    Capacity : int
    Items    : Step list   // most-recent FIRST
}

/// Create an empty buffer with the given capacity.
/// Capacity must be >= 1; smaller values are clamped to 1.
let create (capacity: int) : ContextBuffer =
    { Capacity = max 1 capacity; Items = [] }

/// Append a step to the buffer, dropping oldest items beyond capacity.
/// Pure — returns a new buffer.
let add (step: Step) (buffer: ContextBuffer) : ContextBuffer =
    let next = step :: buffer.Items
    let trimmed =
        if List.length next > buffer.Capacity
        then List.truncate buffer.Capacity next
        else next
    { buffer with Items = trimmed }

/// Read steps in most-recent-first order. Callers that want
/// chronological order can `List.rev` the result.
let toList (buffer: ContextBuffer) : Step list = buffer.Items

/// Current item count (0..Capacity).
let length (buffer: ContextBuffer) : int = List.length buffer.Items

/// Maximum capacity the buffer was created with.
let capacity (buffer: ContextBuffer) : int = buffer.Capacity
