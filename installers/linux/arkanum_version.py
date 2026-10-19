#!/usr/bin/env python3
"""Arkanum product ordering; schema compatibility is checked independently.

Only 2026.1.2 is an authorized legacy source. Majors >=2000 are reserved
for unrecognized calendar-family numbers, not guessed to be SemVer.
"""
import json
import re
import sys

PATTERN = re.compile(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?')
LEGACY = '2026.1.2'


def parse(value) -> tuple[int, tuple[int, ...], tuple[str, ...]]:
    if value == LEGACY:
        return (0, (2026, 1, 2), ())
    match = PATTERN.fullmatch(value) if isinstance(value, str) else None
    if not match or not 1 <= int(match[1]) < 2000:
        raise ValueError('Malformed or unsupported version family; no changes made.')
    prerelease = tuple(match[4].split('.')) if match[4] else ()
    if any(part.isdigit() and len(part) > 1 and part[0] == '0' for part in prerelease):
        raise ValueError('Malformed numeric prerelease identifier; no changes made.')
    return (1, tuple(int(match[i]) for i in (1, 2, 3)), prerelease)


def compare(left, right):
    a, b = parse(left), parse(right)
    if a[0] != b[0]:
        return (a[0] > b[0]) - (a[0] < b[0])
    if a[1] != b[1]:
        return (a[1] > b[1]) - (a[1] < b[1])
    ap, bp = a[2], b[2]
    if not ap or not bp:
        return (not ap) - (not bp)
    for x, y in zip(ap, bp):
        if x == y:
            continue
        if x.isdigit() and y.isdigit():
            return (int(x) > int(y)) - (int(x) < int(y))
        if x.isdigit() != y.isdigit():
            return -1 if x.isdigit() else 1
        return (x > y) - (x < y)
    return (len(ap) > len(bp)) - (len(ap) < len(bp))


def validate_target(value):
    if parse(value)[0] != 1:
        raise ValueError('Legacy releases are immutable, not new package targets.')


def check(installed, target, action, minimum=None):
    validate_target(target)
    order = compare(target, installed)
    if installed == LEGACY and compare(target, '1.0.0') < 0:
        raise ValueError('Legacy transition requires 1.0.0 or later, not its prerelease.')
    if order < 0:
        raise ValueError('Downgrade rejected; use the documented backup rollback procedure.')
    if action == 'repair' and order != 0:
        raise ValueError('Repair requires the same version; use upgrade for a newer release.')
    if action not in ('upgrade', 'repair'):
        raise ValueError('Existing installation requires upgrade or same-version repair.')
    if minimum and compare(installed, minimum) < 0:
        raise ValueError('Installed version is below minimumInstalledVersion.')
    return 'repair' if order == 0 else 'upgrade'


def deb_version(value):
    validate_target(value)
    # Native DEB staging currently supports final releases only. Never silently
    # approximate arbitrary SemVer prerelease precedence with Debian's grammar.
    if parse(value)[2] or '+' in value:
        raise ValueError('Native DEB supports final versions without build metadata only; use TAR for prereleases.')
    return '1:' + value


def read_field(path, field):
    with open(path, encoding='utf-8-sig') as handle:
        value = json.load(handle).get(field)
    if field == 'version':
        validate_target(value)
    elif field == 'installedVersion':
        parse(value)
    elif field == 'minimumInstalledVersion':
        if value is None:
            return ''
        parse(value)
    elif field == 'packageKind':
        if value not in ('full', 'update'):
            raise ValueError('Unsupported packageKind.')
    else:
        raise ValueError('Unknown version policy field.')
    return value


if __name__ == '__main__':
    try:
        if sys.argv[1] == 'check':
            print(check(*sys.argv[2:]))
        elif sys.argv[1] == 'deb':
            print(deb_version(sys.argv[2]))
        elif sys.argv[1] == 'validate':
            validate_target(sys.argv[2])
        elif sys.argv[1] == 'read':
            print(read_field(sys.argv[2], sys.argv[3]))
        else:
            raise ValueError('Unknown version policy command.')
    except (ValueError, IndexError, TypeError, OSError, AttributeError) as exc:
        print(str(exc), file=sys.stderr)
        sys.exit(1)
