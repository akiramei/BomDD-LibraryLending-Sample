# Cheat Report — factory-03-haiku

## Summary
- Total CHEAT items: 3
- Blockers: 0
- Friction: 3
- Minor: 0

## CHEAT-F01-001 [ID Generation] Prefix + Sequential Decimal Format

**手法が与えなかったもの:**
- K-ID-001 specifies prefix (bk_, mb_, ln_) but leaves algorithm after prefix to implementation discretion (実装裁量).

**代替した判断:**
- Implemented sequential decimal format: `bk_0000000001`, `mb_0000000001`, `ln_0000000001` using static counters incremented per instance.
- Format: prefix + 10-digit zero-padded decimal number.
- Uniqueness root: monotonic counter per entity type.

**重大度:** friction

## CHEAT-F01-002 [DateTime Response Precision] ISO-8601 Literal-Z with System DateTime Precision

**手法が与えなかったもの:**
- §2.4 states: `loanedAtUtc` echo requirement waived; "同一瞬時を表す ISO-8601 literal-Z であればよい"（小数秒の桁は実装裁量=探索次元）
- K-UTC-ISO8601-001 specifies 0–7 fractional second digits accepted, but response digit count = exploratory

**代替した判断:**
- Stored and responded with `DateTime.ToString("O")` which produces UTC timestamp with 7-digit fractional seconds.
- Example: `2026-06-10T10:00:00.0000000Z`
- All timestamps in responses use consistent 7-digit precision per .NET default.

**重大度:** friction

## CHEAT-F01-003 [SQLite Schema Design] Nullable PRAGMA/WAL Configuration

**手法が与えなかったもの:**
- K-SQLITE-001 specifies that "WAL モード・PRAGMA 類は任意（挙動契約に影響しない範囲で実装裁量）"。

**代替した判断:**
- No explicit WAL mode enabled; used SQLite default journal_mode (DELETE).
- No pragmas configured; default settings used.
- Decisions: transaction-per-write (§2.4/2.5), connection-per-request (K-SQLITE-001).

**重大度:** friction

---

## Notes on Acceptance Harness

The Library.Acceptance project covers all unit-level test vectors from Control Plan (CP-CORE-*):
- CP-CORE-AVAIL-001: Inventory (3 vectors)
- CP-CORE-LIMIT-001: Member limit (3 vectors)
- CP-CORE-DUE-001: Due date (3 vectors)
- CP-CORE-FINE-001: Fine calculation (4 vectors)
- CP-CORE-OVERDUE-001: Overdue blocking (4 vectors)

Total: **17 test vectors, all PASS.**

L3 coverage (CP-API-CONTRACT-001, CP-API-DATETIME-001, CP-LIST-ORDER-001, CP-PERSIST-RESTART-001, CP-NFR-LATENCY-001) was deferred per M-BOM scope (optional for self-acceptance).

## Decision Log

1. **Datetime parsing (K-UTC-ISO8601-001)**: Implemented strict validation with exact format checks; rejects +00:00, lowercase z, missing offset, and date-only per spec §1.
2. **Transactional atomicity (K-SQLITE-001, INV-1/INV-2)**: All write operations (CreateBook, CreateMember, CreateLoan, ReturnLoan) wrapped in explicit SQLiteTransaction.
3. **Error response format (K-ERROR-SCHEMA-001)**: All errors returned as `{ "error": { "code": "...", "message": "..." } }` with code from spec §2.8 enumeration.
4. **Member loan list ordering (§2.6)**: Ordered by loanedAtUtc (parsed instant, ascending), then Id (ordinal string comparison) per spec.
5. **Active loan field omission (K-JSON-001, §2.6)**: Active loan objects exclude `returnedAtUtc` and `fineAmount` (not null, omitted entirely).
