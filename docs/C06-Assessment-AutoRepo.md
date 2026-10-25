# C06 — Assessment Form (CZ)

## Projekt

```text
Hodnotící tým: Padel Ostrava (BRU0082, TRD0006, KRA0671) 
Hodnocený tým: AutoRepo (Adam Murcko, mur0142, adam.murcko.st@vsb.cz)
Projekt: AutoRepo - rezervace aut (https://github.com/Zephy-X/AutaRepo)
Assessed commit/tag: cd1f93c341afadab1c5176b5303bf100fb8eea7b
Datum: 23.10.2026
```

## Stavy

`YES / PARTLY / NO / N/A`

Každý řádek musí obsahovat Evidence.

## Baseline

| ID | Odpověď | Evidence / komentář |
|---|---|---|
| B1 Stejná identifikovaná baseline pro docs/code/tests | **PARTLY** | **Evidence:** Commit `cd1f93c341afadab1c5176b5303bf100fb8eea7b` obsahuje sjednocený kód (`src/main/java/...`), testy (`src/test/java/...`) i dokumentaci (`docs/`). Nicméně v `docs/evidence-and-evolution.md` (sekce *Commit/tag*) tým sám uvádí: *"No dedicated C05 release tag was present during this verification"* a v `docs/specification/C04/part-2.md` (sekce K a M) zůstaly nevyplněné placeholdery `<fill with actual commit/tag after adding check artifact>`. |
| B2 Relevantní část aplikace nebo testy lze spustit | **YES** | **Evidence:** Pomocí přiloženého Maven wrapperu (`./mvnw test` a `./mvnw -q -Dtest=ReservationServiceTest test`) lze přímo spustit jednotkové testy (`ReservationServiceTest` — 10 testů, `ReservationRulesTest`) |

## C02 — Engineering Specification

**Vybraná operace:** `OP-03 — Confirm Reservation` (`docs/specification/operations/03-confirm-reservation/OP-03-confirm-reservation.md`)  
**Vybraný business rule / invariant:** `BR-02 — Exclusive Resource Invariant & Blocking States` (`docs/business-rules.md`)

| ID | Odpověď | Evidence / komentář |
|---|---|---|
| R1 Operation: input + precondition + postcondition/state | **YES** | **Evidence:** `docs/specification/operations/03-confirm-reservation/OP-03-confirm-reservation.md` jasně definuje vstup (`reservation_id`), preconditions (`Reservation exists`, `state == DRAFT`, `Target Resource is active`, `currentTime < Reservation.startTime`), postconditions a změnu stavu (`DRAFT -> CONFIRMED` pro standardní auta, `DRAFT -> PENDING_APPROVAL` s výpočtem `reviewDeadline` pro luxusní auta). |
| R2 Business rule/invariant je jednoznačný | **YES** | **Evidence:** `docs/business-rules.md` (sekce `BR-02`) přesně definuje, že v žádném potvrzeném stavu nesmí kolidovat dvě rezervace stejného vozu v blokujících stavech `CONFIRMED` (Hard Lock) nebo `PENDING_APPROVAL` (Soft Lock) při polouzavřeném intervalu $[start, end)$ dle vzorce $\max(A.start, B.start) < \min(A.end, B.end)$. Stavy `DRAFT`, `CANCELLED`, `REJECTED` a `EXPIRED` dostupnost neblokují. |
| R3 Operation/rule je konzistentní se state modelem | **YES** | **Evidence:** Přechody v `OP-03-confirm-reservation.md` a `docs/business-rules.md` (`BR-02`, `BR-05`) plně odpovídají stavovému diagramu `docs/specification/diagrams/state-diagram.mmd` i enumu `src/main/java/cz/vsb/autarepo/autorepo/feature/reservation/ReservationState.java` (`DRAFT`, `PENDING_APPROVAL`, `CONFIRMED`, `REJECTED`, `CANCELLED`, `EXPIRED`). |
| R4 Implementace odpovídá specifikované semantics | **PARTLY** | **Evidence:** Větvení `vehicle.isRequiresApproval()` a kontrola kolize přes `ensureNoBlockingConflict(...)` v `ReservationService.confirmReservation(Long id)` (řádky 95–120) odpovídají specifikaci. **Zjištěn však rozpor (viz Finding F1):** Specifikace `OP-03` (`REQ-03`, Preconditions, krok 3 scénáře a Failure Outcomes) i C05 DD tabulka vyžadují při potvrzení kontrolu, že je auto aktivní (`RESOURCE_INACTIVE`) a `currentTime < startTime`. Metoda `confirmReservation` v `ReservationService.java` však žádnou kontrolu `vehicle.isActive()` ani `now.isBefore(reservation.getStartTime())` neprovádí (kontroluje je pouze `createReservation`). |
| R5 Existuje vhodná verification evidence | **PARTLY** | **Evidence:** Existují testy `ReservationServiceTest` (`overlappingConfirmedIntervalRaisesConflict`, `routesApprovalRequiredVehicleToPendingApproval`) a `ReservationLifecycleIntegrationTest` (`luxuryReservationInPendingApprovalBlocksAvailability`), které ověřují kolizi BR-02 a větvení OP-03. Chybí však jakýkoliv test ověřující zamítnutí `confirmReservation` při neaktivním vozidle (`RESOURCE_INACTIVE`) nebo po začátku intervalu, a v `docs/evidence-and-evolution.md` chybí sekce *Evidence C02*. |

## C03 — Architecture

**Vybraná responsibility:** `Reservation Lifecycle Management` — rozhodování o přechodech stavů a vynucení BR-02 (`docs/specification/C04/part-2.md`, sekce C2 a G2)  
**Vybraná interaction:** `OP-03 Confirm Reservation` s větví kolize a schvalování (`docs/specification/C04/part-2.md`, sekce H1)  
**Vybrané ADR/decision:** `ADR-03 — Ownership of confirmation/lifecycle transition decisions under concurrency` — Alternativa A: In-process lifecycle ownership v `ReservationService` (`docs/specification/C04/part-2.md`, sekce F)

| ID | Odpověď | Evidence / komentář |
|---|---|---|
| A1 Responsibility + owner jsou explicitní | **YES** | **Evidence:** V `docs/specification/C04/part-2.md` (tabulky C2, G2 a G3) je `Reservation Lifecycle Management (ReservationService)` jednoznačně určen jako jediný vlastník rozhodnutí o přechodech stavů (`DRAFT -> CONFIRMED`, `DRAFT -> PENDING_APPROVAL`, `approve`, `reject`, `expire`, `cancel`). |
| A2 Skutečný code ownership odpovídá architektuře | **PARTLY** | **Evidence:** `ReservationService.java` skutečně řídí přechody stavů, ale v `docs/specification/C04/part-2.md` (diagram `H2`) je uvedena závislost `ReservationService --> ReservationRules` (`validateReservationWindow`, `shouldExpire`, `canCancel`). **Ve skutečném kódu (`ReservationService.java`) však `ReservationRules` vůbec není injektována ani volána** — `ReservationService` má vlastní privátní duplicitní metody `validateReservationWindow` a `shouldExpireReservation`, přičemž v `ReservationRules.java` je definován `MIN_LEAD_TIME = 2 hours`, zatímco `ReservationService.java` kontroluje pouze `!start.isAfter(now)` (viz Finding F2). |
| A3 Dynamic interaction odpovídá skutečnému call path | **PARTLY** | **Evidence:** Call path v `ReservationService.confirmReservation(Long id)` odpovídá sekvenčnímu diagramu `H1` (`findById` -> `vehicleService.findByIdForUpdate` -> `hasStateConflict` -> `requestApproval`/`confirm` -> `save` + `flush` -> `notificationService`). **Rozpor v chybové větvi:** Diagram `H1` uvádí při kolizi `throw ConflictOverlapException` (třída `ConflictOverlapException.java` v projektu existuje), ale `ReservationService.ensureNoBlockingConflict` (řádek 209) místo ní vyhazuje `new IllegalArgumentException("CONFLICT_OVERLAP")`. |
| A4 ADR/decision odpovídá současné implementaci | **YES** | **Evidence:** Rozhodnutí `ADR-03` (Alternativa A — in-process `ReservationService` v rámci jednoho Spring Boot procesu + pesimistický zámek přes `vehicleService.findByIdForUpdate` + volání ze scheduleru `ReservationExpirationScheduler`) přesně odpovídá implementaci v `ReservationService.java`. |

## C04 — Quality & Measurement

Quality concern: N/A (samostatný úkol C04 Quality & Measurement nebyl v tomto běhu kurzu zadán; v týdnu C04 se dokončovala 2. část C03 Architecture).  
Criterion: N/A

| ID | Odpověď | Evidence / komentář |
|---|---|---|
| Q1 Criterion je skutečně evaluable | **N/A** | **Evidence:** Samostatné zadání C04 (Quality & Measurement) nebylo v dosavadním průběhu semestru (C01–C05) požadováno. Soubor `docs/specification/C04/part-2.md` v repozitáři slouží k dokončení architektonické části `## C03 — Architecture Evidence`. |
| Q2 Measurement měří criterion ve správném contextu | **N/A** | **Evidence:** Není relevantní z důvodu absence úkolu C04 v harmonogramu cvičení (pravidlo C06: *„Nekontrolujete nic, co předchozí zadání nepožadovala“*). |
| Q3 Decision odpovídá výsledku měření | **N/A** | **Evidence:** Není relevantní (viz Q1 a Q2). |

## C05 — Detailed Design & Unit Verification

**Vybraná software unit:** `Reservation lifecycle responsibility` (`cz.vsb.autarepo.autorepo.feature.reservation.ReservationService`) — `docs/architecture-and-decisions.md` (sekce `## C05 Detailed Design`)

| ID | Odpověď | Evidence / komentář |
|---|---|---|
| D1 Design intent je explicitní a pochopitelný | **YES** | **Evidence:** `docs/architecture-and-decisions.md` (sekce `## C05 Detailed Design`) obsahuje jasně rozpracované *Detailed responsibilities*, tabulku vlastnictví (*Design elements and ownership*), kontrakt `hasStateConflict`, UML diagram tříd, tabulku operací s preconditions/postconditions, pseudokód algoritmů (`validateWindow`, `confirm`, `approve`) i sekvenci pro potvrzení luxusního vozu. |
| D2 DD odpovídá skutečné implementaci | **PARTLY** | **Evidence:** Většina operací, injektáž `Clock` i pseudokód souhlasí s `ReservationService.java`. Existují však dva rozpory: 1) V tabulce *Important methods / operations* v `docs/architecture-and-decisions.md` je pro `confirmReservation(id)` uvedeno Precondition: `Reservation is DRAFT; vehicle is active`, ale kód v `confirmReservation` aktivitu vozu neověřuje (Finding F1). 2) Nevyužitá třída `ReservationRules` s odlišnou validací 2hodinového předstihu `MIN_LEAD_TIME` oproti `ReservationService.validateReservationWindow` (Finding F2). |
| D3 Unit/component tests jsou odvoditelné z DD | **YES** | **Evidence:** V `docs/evidence-and-evolution.md` (sekce `## C05 DD Verification Evidence`) je tabulka 9 unit testů v `ReservationServiceTest.java` (`rejectsZeroLengthInterval`, `rejectsPastStart`, `rejectsDurationLongerThanFourteenDays`, `acceptsDurationOfExactlyFourteenDays`, `adjacentIntervalsRemainAvailable`, `overlappingConfirmedIntervalRaisesConflict`, `routesApprovalRequiredVehicleToPendingApproval`, `approvalExpiresAtStartTime`, `cancelsBeforeStartAndNotifies`, `rejectsPendingReservationAndNotifies`), které přímo mapují jednotlivé větve pseudokódu a kontraktů z C05 DD. |
| D4 Relevantní branches/boundaries/states/failures jsou ověřeny | **PARTLY** | **Evidence:** Hraniční případy pro délku 14 dní, přilehlé intervaly $[start, end)$, expiraci v čase `startTime` i kolize jsou v `ReservationServiceTest` pokryty. Chybí však test pro neaktivní vozidlo při `confirmReservation` (uvedené v preconditions C05 DD i C02 OP-03) a test souběhu na úrovni databázového zámku. |
| D5 Vybraný test má smysluplný oracle/expected result | **YES** | **Evidence:** Test `routesApprovalRequiredVehicleToPendingApproval` (a `approvalExpiresAtStartTime`) v `src/test/java/cz/vsb/autarepo/autorepo/feature/reservation/ReservationServiceTest.java` používá deterministický `Clock` a ověřuje přesný doménový záměr ze specifikace (stav `PENDING_APPROVAL`, výpočet `reviewDeadline` a zavolání `notificationService.notifyReservationPendingApproval`). |

## Candidate findings

### F1

```text
Source:
C02 / C05

Engineering claim:
Operace OP-03 (Confirm Reservation) smí potvrdit DRAFT rezervaci pouze tehdy, pokud je cílové vozidlo aktivní (vehicle.isActive == true) a aktuální čas je před začátkem rezervace (now < startTime); v opačném případě musí potvrzení zamítnout (RESOURCE_INACTIVE) a ponechat rezervaci ve stavu DRAFT.

Expected:
- docs/specification/operations/03-confirm-reservation/OP-03-confirm-reservation.md:
  * REQ-03: "The system shall transition a DRAFT Reservation only when the target Resource is active..."
  * Preconditions: "Target Resource (Car) is active. Current system time is strictly prior to reservation start (currentTime < Reservation.startTime)."
  * Main Success Scenario (krok 3): "System verifies target car is active and currentTime < Reservation.startTime."
  * Alternative / Failure Outcomes: "Resource Inactive: Car is disabled or inactive -> rejected with RESOURCE_INACTIVE; Reservation remains DRAFT."
- docs/architecture-and-decisions.md (sekce C05 Detailed Design -> Important methods / operations):
  * confirmReservation(id) Preconditions: "Reservation is DRAFT; vehicle is active."

Observed:
Metoda ReservationService.confirmReservation(Long id) vůbec nekontroluje, zda je vozidlo aktivní (!vehicle.isActive()), ani zda nenastal čas začátku rezervace (now < reservation.getStartTime()). Kontrola !vehicle.isActive() se nachází pouze v createReservation(). Pokud je vozidlo deaktivováno po vytvoření DRAFTu (nebo uživatel potvrdí starý DRAFT po čase startTime), confirmReservation() bez chyby přepne rezervaci do stavu CONFIRMED / PENDING_APPROVAL.

Evidence:
- src/main/java/cz/vsb/autarepo/autorepo/feature/reservation/ReservationService.java (řádky 95–120):
  Metoda confirmReservation(Long id) načte vozidlo přes vehicleService.findByIdForUpdate(...), zkontroluje pouze ensureNoBlockingConflict(...) a ihned volá reservation.requestApproval(now) nebo reservation.confirm(now).
- docs/specification/operations/03-confirm-reservation/OP-03-confirm-reservation.md (REQ-03, Preconditions, Failure Outcomes).
- docs/architecture-and-decisions.md (C05 Detailed Design, tabulka Important methods / operations).

Verification performed:
Statická inspekce call path v ReservationService.confirmReservation(Long id) a Reservation.confirm(LocalDateTime now) / Reservation.requestApproval(LocalDateTime now) + porovnání s createReservation(ReservationForm form) (řádky 80–82, kde kontrola if (!vehicle.isActive()) throw new IllegalStateException("RESOURCE_INACTIVE") existuje).

Result: VERIFIED FINDING
```

### F2

```text
Source:
C03 / C05

Engineering claim:
Architektonický návrh (C03 H1/H2) a doménová vrstva deklarují použití komponenty ReservationRules a doménové výjimky ConflictOverlapException při vyhodnocování pravidel v ReservationService.

Expected:
- docs/specification/C04/part-2.md (sekce H2 Focused Design Class Diagram):
  Diagram explicitně modeluje vazbu ReservationService --> ReservationRules (s metodami validateReservationWindow, shouldExpire, canCancel).
- docs/specification/C04/part-2.md (sekce H1 Design Sequence Diagram):
  Při zjištění kolize (alt conflict exists) diagram předepisuje: RM-->>VC: throw ConflictOverlapException.
- src/main/java/cz/vsb/autarepo/autorepo/common/domain/ReservationRules.java (řádek 13 a 27–38):
  Definuje pravidlo MIN_LEAD_TIME = Duration.ofHours(2) a vyhazuje INSUFFICIENT_LEAD_TIME, pokud start.isBefore(now.plus(MIN_LEAD_TIME)).

Observed:
1. Třída ReservationService vůbec neinjektuje ani nepoužívá komponentu ReservationRules. Místo toho má vlastní privátní metody validateReservationWindow() (řádky 220–230) a shouldExpireReservation() (řádky 213–218). Kvůli této duplicitě vznikl rozpor v doménové logice: ReservationRules vyžaduje rezervaci alespoň 2 hodiny předem (MIN_LEAD_TIME = 2h), zatímco ReservationService povolí rezervaci klidně 1 sekundu v budoucnosti (!start.isAfter(now)).
2. Třída ConflictOverlapException.java v balíčku common.exception existuje, ale v celém produkčním kódu není nikde použita — ReservationService.ensureNoBlockingConflict() (řádek 209) místo ní vyhazuje obecnou IllegalArgumentException("CONFLICT_OVERLAP").

Evidence:
- src/main/java/cz/vsb/autarepo/autorepo/feature/reservation/ReservationService.java (řádky 26–43, 207–230)
- src/main/java/cz/vsb/autarepo/autorepo/common/domain/ReservationRules.java (řádky 12–38)
- src/main/java/cz/vsb/autarepo/autorepo/common/exception/ConflictOverlapException.java
- docs/specification/C04/part-2.md (sekce H1 a H2)

Verification performed:
Porovnání UML diagramů H1/H2 v docs/specification/C04/part-2.md se zdrojovým kódem ReservationService.java a vyhledání všech referencí na ReservationRules a ConflictOverlapException napříč src/main/java.

Result: VERIFIED FINDING
```

### F3

```text
Source:
C02 / C03 / C05

Engineering claim:
Soubor docs/evidence-and-evolution.md a architektonická dokumentace musí obsahovat kompletní záznamy o verifikaci a přesné identifikátory odevzdaných commitů/tagů pro jednotlivé etapy (C02, C03, C05).

Expected:
- Zadání C02 (bod 15): Zapsat sekci "## Evidence C02: specifikace -> běžící aplikace" včetně commitu/tagu do docs/evidence-and-evolution.md.
- Zadání C03 (sekce K a M): Zaznamenat přesný commit/tag po úpravě implementace a architektonickou evidenci do docs/evidence-and-evolution.md.
- Zadání C05 (Definition of Done): Zaznamenat commit/tag pro C05 Detailed Design.

Observed:
1. V souboru docs/evidence-and-evolution.md zcela chybí sekce evidence pro C02 a C03 (soubor obsahuje pouze "# C01 Engineering Spike" a "## C05 DD Verification Evidence").
2. V souboru docs/specification/C04/part-2.md (který obsahuje C03 Architecture Evidence) zůstaly v sekcích K a M nevyplněné šablonové placeholdery: "<fill with actual commit/tag after adding check artifact>" a "<fill exact commit/tag>".
3. V docs/evidence-and-evolution.md (sekce Commit/tag pod C05) tým sám konstatuje, že tag pro C05 dosud nevytvořil ("No dedicated C05 release tag was present during this verification").

Evidence:
- docs/evidence-and-evolution.md (řádky 1–78)
- docs/specification/C04/part-2.md (řádek 275 a řádek 315)

Verification performed:
Kontrola obsahu docs/evidence-and-evolution.md a docs/specification/C04/part-2.md vůči povinným výstupům ze zadání C02, C03 a C05.

Result: VERIFIED FINDING
```

## Owner disposition

| Finding | ACCEPTED / REJECTED / DEFERRED | Reason/evidence | Action / no-change |
|---|---|---|---|
| F1 | | | |
| F2 | | | |
| F3 | | | |