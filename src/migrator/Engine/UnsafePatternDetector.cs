
namespace migrator.Engine;



public static class UnsafePatternDetector
{
    //used when Transaction: on
    public static void AssertSafe(string sql, string provider)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return;

        if (!UnsafeSqlRules.Rules.TryGetValue(provider, out var rules))
            return;

        foreach (var rule in rules)
        {
            if (!rule.Pattern.IsMatch(sql))
                continue;

            switch (rule.Risk)
            {
                case SqlRiskLevel.ForbiddenInTransaction:
                case SqlRiskLevel.ImplicitCommit:
                    throw new InvalidOperationException(
                        $"Unsafe migration inside transaction. {rule.Reason}");

                case SqlRiskLevel.OperationalRisk:
                    Utils.SendWarningMessage(rule.Reason);
                    break;
            }
        }
    }



    //used when Transaction: off
    public static void AssertSafeOutsideTransaction(string sql, string provider)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return;

        if (!UnsafeSqlRules.Rules.TryGetValue(provider, out var rules))
            return;

        foreach (var rule in rules)
        {
            if (!rule.Pattern.IsMatch(sql))
                continue;

            if (rule.Risk == SqlRiskLevel.OperationalRisk)
            {
                Utils.SendWarningMessage(rule.Reason);
            }
        }
    }
}

