# Project Frame

## Reservation domain
Domain for padel courts reservation 

## Purpose
Reservation system for the new padel courts in Ostrava. 
It is for people who are searching for padel courts to play on with their family or friends. 

## Users / Stakeholders
Admin
User

## Core concepts
Reservation, Court, Time-slot, User 

## Core operations
- Create reservation
- Confirm / approve reservation
- Cancel reservation
- Check availability

## Persistent state
Reservation - User ID, name, surname, time slot, court number, state
Court - number, availability

## State-changing operation
DRAFT → CONFIRMED
DRAFT → REJECTED
CONFIRMED → CANCELED
CONFIRMED → EXPIRED


## Common business rule
Confirmed reservations for the same resource must not overlap.

## Domain-specific business rule
User can have at most 2 active reservations in advance.

## External / system boundary
Notification Service (email notification one day before reservation)
Access Control System (QR code generation for easy court access)

## Assumption
Courts are going to have fixed opening hours (8:00-22:00) and fixed time slots (60 minutes).

## Unknown
??????????
