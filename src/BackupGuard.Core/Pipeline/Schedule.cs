namespace BackupGuard.Core.Pipeline;

public static class Schedule
{
    /// <summary>
    /// Ocorrência mais recente do horário esperado cujo prazo (horário + tolerância) já venceu.
    /// Ex.: esperado 23:00 com 3h de tolerância, às 01:00 do dia seguinte ainda não venceu —
    /// a ocorrência vencida é a de anteontem às 23:00.
    /// </summary>
    public static DateTime LastDueOccurrence(DateTime nowLocal, TimeOnly expected, TimeSpan grace)
    {
        var candidate = nowLocal.Date + expected.ToTimeSpan();
        while (candidate + grace > nowLocal)
            candidate = candidate.AddDays(-1);
        return candidate;
    }
}
