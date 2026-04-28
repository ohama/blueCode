"""Buggy module: parse_age returns inconsistent types.

The function should always return an int (or raise). Currently:
- Valid input: returns int
- Invalid string: returns None (silently)
- Negative number: returns None (silently)

Callers expect int and crash later with TypeError when arithmetic operations
are applied to None.
"""


def parse_age(raw):
    """Parse a user-provided age. Should return int >= 0; raises ValueError otherwise."""
    try:
        n = int(raw)
    except (ValueError, TypeError):
        return None  # BUG: silently returns None instead of raising
    if n < 0:
        return None  # BUG: silently returns None instead of raising
    return n


def average_ages(raws):
    """Average a list of raw age inputs. Crashes on None values from parse_age."""
    parsed = [parse_age(r) for r in raws]
    return sum(parsed) / len(parsed)  # TypeError when any element is None
