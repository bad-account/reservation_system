# Project Frame

## Reservation domain
Co konkrétně rezervujeme?

## Purpose
2–3 věty: komu systém slouží a proč.

## Users / Stakeholders
1–3 role.

## Core concepts
Reservation, Resource, User + případně 0–3 další pojmy.

## Core operations
- Create reservation
- Confirm / approve reservation
- Cancel reservation
- Check availability

## Persistent state
Co ukládáme o Reservation a Resource.

## State-changing operation
Např. DRAFT → CONFIRMED.

## Common business rule
Confirmed reservations for the same resource must not overlap.

## Domain-specific business rule
Jedno vlastní pravidlo.

## External / system boundary
Jedna dependency. Defaultně Notification Service.

## Assumption
Jedna věc, kterou nyní považujete za pravdivou, ale není jistota.

## Unknown
Jedna důležitá věc, kterou nyní nevíte.
