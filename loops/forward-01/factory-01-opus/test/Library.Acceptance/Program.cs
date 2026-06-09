using System.Globalization;
using Library.Core;

// 自己受入ハーネス: Control Plan の unit 行(CP-CORE-*)の test_vectors を全被覆。
// Library.Core を直接呼ぶ(プロセス外形なし)。FAIL があれば exit code 非0。

var runner = new Runner();

// 補助: literal-Z 文字列 → DateTimeOffset(UTC)。
static DateTimeOffset T(string z)
{
    if (!Iso8601Z.TryParse(z, out var dto))
        throw new ArgumentException($"test literal not parseable: {z}");
    return dto;
}
static DateOnly D(string ymd) => DateOnly.ParseExact(ymd, "yyyy-MM-dd", CultureInfo.InvariantCulture);

// active な Loan を作る補助(member/book は判定に使わないので固定値)。
static Loan ActiveLoan(string id, string loanedZ, string dueYmd)
    => new(id, "bk_x", "mb_x", T(loanedZ), D(dueYmd), LoanStatus.Active, null, null);

// =====================================================================
// CP-CORE-AVAIL-001  在庫判定(availableCopies = copies − active、0で拒否、返却で復元)
// =====================================================================
runner.Section("CP-CORE-AVAIL-001");

// copies=1: 1件目成功・2件目 no_copies_available
runner.Check("AVAIL copies=1 1件目成功",
    LendingRules.EvaluateLoan(bookCopies: 1, bookActiveLoanCount: 0,
        memberActiveLoans: Array.Empty<Loan>(), loanedAtUtc: T("2026-06-10T10:00:00Z"))
        == LendingError.None);

runner.Check("AVAIL copies=1 2件目 no_copies_available",
    LendingRules.EvaluateLoan(bookCopies: 1, bookActiveLoanCount: 1,
        memberActiveLoans: Array.Empty<Loan>(), loanedAtUtc: T("2026-06-10T10:00:00Z"))
        == LendingError.NoCopiesAvailable);

// availableCopies 計算
runner.Check("AVAIL availableCopies(1,0)=1", LendingRules.AvailableCopies(1, 0) == 1);
runner.Check("AVAIL availableCopies(1,1)=0", LendingRules.AvailableCopies(1, 1) == 0);

// 返却後: availableCopies 復元・再貸出成功(返却で active が 0 に戻る → 在庫1)
runner.Check("AVAIL 返却後 復元(active=0)再貸出可",
    LendingRules.EvaluateLoan(bookCopies: 1, bookActiveLoanCount: 0,
        memberActiveLoans: Array.Empty<Loan>(), loanedAtUtc: T("2026-06-11T10:00:00Z"))
        == LendingError.None);

// copies=2: 2件まで成功・3件目拒否
runner.Check("AVAIL copies=2 1件目成功",
    LendingRules.EvaluateLoan(2, 0, Array.Empty<Loan>(), T("2026-06-10T10:00:00Z")) == LendingError.None);
runner.Check("AVAIL copies=2 2件目成功",
    LendingRules.EvaluateLoan(2, 1, Array.Empty<Loan>(), T("2026-06-10T10:00:00Z")) == LendingError.None);
runner.Check("AVAIL copies=2 3件目 no_copies_available",
    LendingRules.EvaluateLoan(2, 2, Array.Empty<Loan>(), T("2026-06-10T10:00:00Z")) == LendingError.NoCopiesAvailable);

// =====================================================================
// CP-CORE-LIMIT-001  会員上限(active ≤ 3)
// =====================================================================
runner.Section("CP-CORE-LIMIT-001");

// 3件目=成功(境界は4件目): 会員が active 2件のとき新規(=3件目)は成功
var two = new[]
{
    ActiveLoan("ln_1", "2026-06-10T10:00:00Z", "2026-06-24"),
    ActiveLoan("ln_2", "2026-06-10T11:00:00Z", "2026-06-24"),
};
runner.Check("LIMIT 3件目=成功(active2件→新規)",
    LendingRules.EvaluateLoan(99, 0, two, T("2026-06-10T12:00:00Z")) == LendingError.None);

// 4件目=loan_limit_exceeded: active 3件のとき新規(=4件目)は拒否
var three = new[]
{
    ActiveLoan("ln_1", "2026-06-10T10:00:00Z", "2026-06-24"),
    ActiveLoan("ln_2", "2026-06-10T11:00:00Z", "2026-06-24"),
    ActiveLoan("ln_3", "2026-06-10T12:00:00Z", "2026-06-24"),
};
runner.Check("LIMIT 4件目=loan_limit_exceeded(active3件→新規)",
    LendingRules.EvaluateLoan(99, 0, three, T("2026-06-10T13:00:00Z")) == LendingError.LoanLimitExceeded);

// 返却済みは数えない(3件中1返却→新規成功): returned は memberActiveLoans に含めない前提だが、
// 仮に混ざっても Count(active) で除外されることを確認する。
var threeWithOneReturned = new[]
{
    ActiveLoan("ln_1", "2026-06-10T10:00:00Z", "2026-06-24"),
    ActiveLoan("ln_2", "2026-06-10T11:00:00Z", "2026-06-24"),
    new Loan("ln_3", "bk_x", "mb_x", T("2026-06-10T12:00:00Z"), D("2026-06-24"),
        LoanStatus.Returned, T("2026-06-12T00:00:00Z"), 0),
};
runner.Check("LIMIT 返却済みは数えない(active2→新規成功)",
    LendingRules.EvaluateLoan(99, 0, threeWithOneReturned, T("2026-06-10T13:00:00Z")) == LendingError.None);

// =====================================================================
// CP-CORE-DUE-001  dueDateUtc = UTC暦日 + 14日(暦日加算)
// =====================================================================
runner.Section("CP-CORE-DUE-001");

runner.Check("DUE 2026-01-31T10:00:00Z → 2026-02-14(月またぎ)",
    LendingRules.ComputeDueDate(T("2026-01-31T10:00:00Z")) == D("2026-02-14"));
runner.Check("DUE 2026-12-25T00:00:00Z → 2027-01-08(年またぎ)",
    LendingRules.ComputeDueDate(T("2026-12-25T00:00:00Z")) == D("2027-01-08"));
runner.Check("DUE 2026-06-10T23:59:59Z → 2026-06-24(時刻無関係)",
    LendingRules.ComputeDueDate(T("2026-06-10T23:59:59Z")) == D("2026-06-24"));

// =====================================================================
// CP-CORE-FINE-001  fineAmount = max(0, 暦日差) × 100
// =====================================================================
runner.Section("CP-CORE-FINE-001");

var due = D("2026-06-24");
runner.Check("FINE 期限日当日 23:59:59Z → 0(FMEA-001)",
    LendingRules.ComputeFine(T("2026-06-24T23:59:59Z"), due) == 0);
runner.Check("FINE 期限翌日 00:00:00Z → 100",
    LendingRules.ComputeFine(T("2026-06-25T00:00:00Z"), due) == 100);
runner.Check("FINE 期限+3日 → 300",
    LendingRules.ComputeFine(T("2026-06-27T00:00:00Z"), due) == 300);
runner.Check("FINE 早期返却(期限前)→ 0",
    LendingRules.ComputeFine(T("2026-06-20T10:00:00Z"), due) == 0);

// =====================================================================
// CP-CORE-OVERDUE-001  延滞ブロック(any 判定・当日境界・返却済み除外)
// =====================================================================
runner.Section("CP-CORE-OVERDUE-001");

// 期限日当日の新規貸出 → ブロックしない(> のみ)
// 既存 active の due=06-24、新規貸出 loanedAt の暦日=06-24 → 24>24 偽 → not overdue
var oneActiveDue0624 = new[] { ActiveLoan("ln_a", "2026-06-10T10:00:00Z", "2026-06-24") };
runner.Check("OVERDUE 期限日当日はブロックしない(>のみ)",
    LendingRules.EvaluateLoan(99, 0, oneActiveDue0624, T("2026-06-24T23:59:59Z")) == LendingError.None);

// 期限+1日 → member_overdue_blocked(25>24)
runner.Check("OVERDUE 期限+1日 → member_overdue_blocked",
    LendingRules.EvaluateLoan(99, 0, oneActiveDue0624, T("2026-06-25T00:00:00Z")) == LendingError.MemberOverdueBlocked);

// 延滞貸出を返却済みにした後 → ブロックしない(returned は数えない)
var oneReturnedOverdue = new[]
{
    new Loan("ln_r", "bk_x", "mb_x", T("2026-06-10T10:00:00Z"), D("2026-06-24"),
        LoanStatus.Returned, T("2026-06-30T00:00:00Z"), 600)
};
runner.Check("OVERDUE 返却済みの過去延滞はブロックしない",
    LendingRules.EvaluateLoan(99, 0, oneReturnedOverdue, T("2026-07-01T00:00:00Z")) == LendingError.None);

// active 2件中1件のみ延滞 → ブロックする(any)
var twoOneOverdue = new[]
{
    ActiveLoan("ln_1", "2026-06-20T10:00:00Z", "2026-07-04"), // 期限内
    ActiveLoan("ln_2", "2026-06-01T10:00:00Z", "2026-06-15"), // 延滞元(基準>06-15)
};
runner.Check("OVERDUE 2件中1件のみ延滞 → ブロック(any)",
    LendingRules.EvaluateLoan(99, 0, twoOneOverdue, T("2026-06-25T00:00:00Z")) == LendingError.MemberOverdueBlocked);

// =====================================================================
// 判定順序の確認(仕様 §2.4: 延滞 > 上限 > 在庫)。Control Plan の核保証を補強。
// =====================================================================
runner.Section("ORDERING (spec §2.4)");

// 延滞 かつ 上限 かつ 在庫切れ → 最初に該当する延滞が返る
var threeOverdue = new[]
{
    ActiveLoan("ln_1", "2026-06-01T10:00:00Z", "2026-06-15"),
    ActiveLoan("ln_2", "2026-06-01T11:00:00Z", "2026-06-15"),
    ActiveLoan("ln_3", "2026-06-01T12:00:00Z", "2026-06-15"),
};
runner.Check("ORDER 延滞優先(上限・在庫切れより先)",
    LendingRules.EvaluateLoan(bookCopies: 1, bookActiveLoanCount: 1, threeOverdue, T("2026-06-25T00:00:00Z"))
        == LendingError.MemberOverdueBlocked);

// 非延滞・上限到達・在庫切れ → 上限が在庫より先
runner.Check("ORDER 上限優先(在庫切れより先)",
    LendingRules.EvaluateLoan(bookCopies: 1, bookActiveLoanCount: 1, three, T("2026-06-10T13:00:00Z"))
        == LendingError.LoanLimitExceeded);

// =====================================================================
// 結果
// =====================================================================
return runner.Report();


sealed class Runner
{
    private int _pass;
    private int _fail;

    public void Section(string name) => Console.WriteLine($"\n== {name} ==");

    public void Check(string label, bool ok)
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine($"  PASS  {label}");
        }
        else
        {
            _fail++;
            Console.WriteLine($"  FAIL  {label}");
        }
    }

    public int Report()
    {
        Console.WriteLine($"\n----------------------------------------");
        Console.WriteLine($"PASS: {_pass}  FAIL: {_fail}  TOTAL: {_pass + _fail}");
        if (_fail == 0)
        {
            Console.WriteLine("ALL PASS");
            return 0;
        }
        Console.WriteLine("HAS FAILURES");
        return 1;
    }
}
