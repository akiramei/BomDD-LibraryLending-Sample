namespace Library.Core;

public record Book(string Id, string Title, int Copies);

public record Member(string Id, string Name);

public enum LoanStatus { Active, Returned }

public record Loan(
    string Id,
    string BookId,
    string MemberId,
    DateTime LoanedAtUtc,
    DateOnly DueDateUtc,
    LoanStatus Status,
    DateTime? ReturnedAtUtc = null,
    int? FineAmount = null
);
