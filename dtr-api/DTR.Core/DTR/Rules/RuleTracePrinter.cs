 

namespace DTR.Core;

public static class RuleTracePrinter
{
    //public static void PrintLedgerSummary(TimeRangeLedger ledger, DateOnly date, Guid employeeId)
    //{
    //    var keys = ledger.GetKeys(date, employeeId);

    //    foreach (var key in keys)
    //    {
    //        var slice = ledger.GetAllocated(key);
    //        var label = key.Column; // You can also map this to DisplayName if available

    //        Console.WriteLine($"{label,-12} → {Format(slice)}");
    //    }
    //}

    //private static string Format(TimeRange range)
    //{
    //    if (range.TimeRecords.Count == 0)
    //        return "—";

    //    return string.Join(", ", range.TimeRecords.Select(r =>
    //        $"[{r.Start:HH:mm}–{r.End:HH:mm}]"
    //    ));
    //}
}